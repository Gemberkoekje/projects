"""Soak-test sampler (PLAN.md slice 1.14).

Every interval it records, for the running bot and its database:
- per table: exact rows, total bytes, and cumulative inserts, updates and deletes
  (pg_stat_user_tables), so a bounded table that is rewritten in a loop still shows;
- the database size, its schemas (no `wolverine` schema may appear) and transaction counts;
- the bot's /metrics (a raw copy is kept per sample), its health, and its status endpoints;
- log volume from the bot's JSON log (CLEF): lines by level and by message template, plus the
  lines slice 1.14 is about: circuit-breaker trips, 429s, reset detection, errors.

Each sample re-reads the whole log, so counts are cumulative and a restart of the sampler loses
nothing. Output: samples.jsonl (one object per sample), tables.csv, summary.log (one line per
sample), metrics/NN.txt and context/NN.json.
"""
import argparse
import collections
import csv
import datetime as dt
import json
import pathlib
import re
import subprocess
import sys
import time
import urllib.error
import urllib.request

SOAK = pathlib.Path("soak-output").resolve()  # replaced by --out

TABLES_SQL = r"""
select n.nspname, c.relname,
       (xpath('/row/c/text()', query_to_xml(format('select count(*) as c from %I.%I', n.nspname, c.relname), false, true, '')))[1]::text::bigint,
       pg_total_relation_size(c.oid),
       coalesce(s.n_tup_ins, 0), coalesce(s.n_tup_upd, 0), coalesce(s.n_tup_del, 0), coalesce(s.n_dead_tup, 0)
from pg_class c
join pg_namespace n on n.oid = c.relnamespace
left join pg_stat_user_tables s on s.relid = c.oid
where c.relkind in ('r', 'p')
  and n.nspname not in ('pg_catalog', 'information_schema')
  and n.nspname not like 'pg\_toast%'
order by 1, 2;
"""

DATABASE_SQL = r"""
select pg_database_size(current_database()), xact_commit, xact_rollback, tup_inserted, tup_updated, tup_deleted
from pg_stat_database where datname = current_database();
"""

SCHEMAS_SQL = r"""
select coalesce(string_agg(nspname, ',' order by nspname), '')
from pg_namespace where nspname not like 'pg\_%' and nspname <> 'information_schema';
"""

# Log lines slice 1.14 cares about, matched on the rendered message (@m).
FLAGS = {
    "breaker": re.compile(r"goal steps in a minute"),
    "throttled": re.compile(r"^429 from"),
    "reset": re.compile(r"ResetDetected"),
    "api_unavailable": re.compile(r"^ApiUnavailable"),
    "db_size": re.compile(r"^DbSize"),
    "already_at": re.compile(r"is already at .*nothing to do"),
    "wolverine_retry": re.compile(r"^Wolverine retry"),
    "tick_step_failed": re.compile(r"failed; the rest of the tick carries on"),
}

KEPT_METRIC_PREFIXES = ("spacetraders_", "process_", "dotnet_total_memory_bytes", "dotnet_collection_count_total")


def utcnow() -> dt.datetime:
    return dt.datetime.now(dt.timezone.utc)


def psql(args, sql: str) -> list[list[str]]:
    result = subprocess.run(
        ["docker", "exec", args.container, "psql", "-U", "postgres", "-d", args.database,
         "-v", "ON_ERROR_STOP=1", "-AtF", "\t", "-c", sql],
        capture_output=True, text=True, timeout=300)
    if result.returncode != 0:
        raise RuntimeError(result.stderr.strip())
    return [line.split("\t") for line in result.stdout.splitlines() if line]


def http_get(url: str, timeout: float = 20) -> tuple[int | None, str]:
    try:
        with urllib.request.urlopen(url, timeout=timeout) as response:
            return response.status, response.read().decode("utf-8", "replace")
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8", "replace")
    except Exception as e:  # noqa: BLE001 - a sample records whatever went wrong
        return None, repr(e)


def parse_metrics(text: str) -> dict[str, float]:
    values: dict[str, float] = {}
    for line in text.splitlines():
        if not line or line.startswith("#"):
            continue
        match = re.match(r"^([a-zA-Z_:][a-zA-Z0-9_:]*)(\{[^}]*\})?\s+(\S+)", line)
        if not match:
            continue
        try:
            values[match.group(1) + (match.group(2) or "")] = float(match.group(3))
        except ValueError:
            continue
    return values


def scan_log(path: pathlib.Path) -> dict:
    levels: collections.Counter = collections.Counter()
    flags: collections.Counter = collections.Counter()
    templates: dict[str, dict] = {}
    non_json = 0
    non_json_samples: list[str] = []
    token_like = 0
    first_t = last_t = None
    lines = 0
    if path.exists():
        with open(path, "rb") as f:
            for raw in f:
                # Locally the console's code page (850) turns characters such as "→" into 0x1A, which
                # JSON doesn't allow in a string. On the cluster (UTF-8) that can't happen.
                line = raw.decode("utf-8", "replace").replace("\x1a", "?").strip()
                if not line:
                    continue
                lines += 1
                if "eyJ" in line:
                    token_like += 1
                try:
                    event = json.loads(line)
                except json.JSONDecodeError:
                    non_json += 1
                    if len(non_json_samples) < 5:
                        non_json_samples.append(line[:200])
                    continue
                level = event.get("@l", "Information")
                levels[level] += 1
                message = event.get("@m", "")
                t = event.get("@t")
                first_t = first_t or t
                last_t = t or last_t
                for name, pattern in FLAGS.items():
                    if pattern.search(message):
                        flags[name] += 1
                if "@x" in event:
                    flags["exceptions"] += 1
                key = event.get("@i") or message[:60]
                entry = templates.setdefault(key, {"count": 0, "level": level, "source": event.get("SourceContext", ""),
                                                   "sample": message[:240], "first": t})
                entry["count"] += 1
                entry["last"] = t
    return {"lines": lines, "levels": dict(levels), "flags": dict(flags), "templates": templates,
            "non_json": non_json, "non_json_samples": non_json_samples, "token_like_lines": token_like,
            "first_t": first_t, "last_t": last_t}


def ship_summary(ships_json: str) -> str:
    try:
        ships = json.loads(ships_json)
    except json.JSONDecodeError:
        return "?"
    if isinstance(ships, dict):
        ships = ships.get("ships") or ships.get("items") or []
    parts = []
    for ship in ships if isinstance(ships, list) else []:
        if not isinstance(ship, dict):
            continue
        lowered = {k.lower(): v for k, v in ship.items()}
        symbol = str(lowered.get("symbol") or lowered.get("shipsymbol") or "?").split("-")[-1]
        status = lowered.get("navstatus") or lowered.get("status") or "?"
        goal = lowered.get("goalkind") or lowered.get("activegoalkind") or "-"
        parts.append(f"{symbol}:{status}/{goal}")
    return ",".join(parts) or "-"


def take_sample(args, number: int, started: dt.datetime, previous: dict | None) -> dict:
    now = utcnow()
    sample: dict = {"n": number, "t": now.isoformat(timespec="seconds"),
                    "elapsed_min": round((now - started).total_seconds() / 60, 1)}

    # Database
    try:
        tables = [{"schema": r[0], "table": r[1], "rows": int(r[2]), "bytes": int(r[3]), "ins": int(r[4]),
                   "upd": int(r[5]), "del": int(r[6]), "dead": int(r[7])} for r in psql(args, TABLES_SQL)]
        db = psql(args, DATABASE_SQL)[0]
        sample["db"] = {"bytes": int(db[0]), "xact_commit": int(db[1]), "xact_rollback": int(db[2]),
                        "tup_inserted": int(db[3]), "tup_updated": int(db[4]), "tup_deleted": int(db[5])}
        sample["schemas"] = psql(args, SCHEMAS_SQL)[0][0].split(",")
        sample["tables"] = tables
    except Exception as e:  # noqa: BLE001
        sample["db_error"] = repr(e)
        sample["tables"] = []

    # The bot
    base = args.base_url.rstrip("/")
    status, metrics_text = http_get(base + "/metrics")
    sample["metrics_status"] = status
    (SOAK / "metrics").mkdir(exist_ok=True)
    (SOAK / "metrics" / f"{number:02d}.txt").write_text(metrics_text, encoding="utf-8")
    metrics = parse_metrics(metrics_text) if status == 200 else {}
    sample["metrics"] = {k: v for k, v in metrics.items()
                         if k.startswith(KEPT_METRIC_PREFIXES) or "wolverine" in k.lower()}
    context = {}
    for path in ("/health/startup", "/health/ready", "/health/automation", "/status/agent", "/status/ships",
                 "/status/contracts", "/fleet/assignments", "/status/rate-limit"):
        code, body = http_get(base + path)
        try:
            context[path] = {"status": code, "body": json.loads(body) if body else None}
        except json.JSONDecodeError:
            context[path] = {"status": code, "body": body[:2000]}
    (SOAK / "context").mkdir(exist_ok=True)
    (SOAK / "context" / f"{number:02d}.json").write_text(json.dumps(context, indent=1), encoding="utf-8")
    sample["health_startup"] = context["/health/startup"]["status"], str(context["/health/startup"]["body"])[:40]
    agent = context["/status/agent"]["body"] if isinstance(context["/status/agent"]["body"], dict) else {}
    sample["credits"] = {k.lower(): v for k, v in agent.items()}.get("credits")
    sample["ships"] = ship_summary(json.dumps(context["/status/ships"]["body"]))

    # The app process
    exit_file = SOAK / "logs" / "exit.txt"
    sample["app_exit"] = exit_file.read_text().strip() if exit_file.exists() else None

    # Logs
    log = scan_log(SOAK / "logs" / "app.log")
    sample["log"] = {k: v for k, v in log.items() if k != "templates"}
    (SOAK / "templates.json").write_text(json.dumps(log["templates"], indent=1), encoding="utf-8")

    with open(SOAK / "samples.jsonl", "a", encoding="utf-8") as f:
        f.write(json.dumps(sample) + "\n")
    with open(SOAK / "tables.csv", "a", newline="", encoding="utf-8") as f:
        writer = csv.writer(f)
        if f.tell() == 0:
            writer.writerow(["n", "t", "elapsed_min", "schema", "table", "rows", "bytes", "ins", "upd", "del", "dead"])
        for t in sample["tables"]:
            writer.writerow([number, sample["t"], sample["elapsed_min"], t["schema"], t["table"], t["rows"], t["bytes"],
                             t["ins"], t["upd"], t["del"], t["dead"]])

    write_summary(sample, previous)
    return sample


def write_summary(sample: dict, previous: dict | None) -> None:
    def delta(value, old):
        return f"{value - old:+}" if old is not None else "n/a"

    prev_db = (previous or {}).get("db", {}).get("bytes")
    prev_rows = sum(t["rows"] for t in (previous or {}).get("tables", [])) if previous else None
    prev_lines = (previous or {}).get("log", {}).get("lines")
    db_bytes = sample.get("db", {}).get("bytes", 0)
    rows = sum(t["rows"] for t in sample["tables"])
    writes = sum(t["ins"] + t["upd"] + t["del"] for t in sample["tables"])
    prev_writes = sum(t["ins"] + t["upd"] + t["del"] for t in (previous or {}).get("tables", [])) if previous else None
    wolverine = [f"{t['schema']}.{t['table']}" for t in sample["tables"]
                 if "wolverine" in (t["schema"] + t["table"]).lower()]
    wolverine_text = "none" if not wolverine and "wolverine" not in sample.get("schemas", []) else ",".join(wolverine) or "schema"
    m = sample["metrics"]
    api = sum(v for k, v in m.items() if k.startswith("spacetraders_api_calls_total"))
    prev_api = sum(v for k, v in (previous or {}).get("metrics", {}).items()
                   if k.startswith("spacetraders_api_calls_total")) if previous else None
    throttled = sum(v for k, v in m.items() if k.startswith("spacetraders_api_throttled_total"))
    trips = sum(v for k, v in m.items() if k.startswith("spacetraders_goal_breaker_trips_total"))
    levels = sample["log"]["levels"]
    prev_levels = (previous or {}).get("log", {}).get("levels", {})
    level_delta = " ".join(f"{name[0]}:{levels.get(name, 0) - prev_levels.get(name, 0)}"
                           for name in ("Information", "Warning", "Error", "Fatal") if levels.get(name, 0))
    flags = sample["log"]["flags"]
    line = (f"#{sample['n']:02d} {sample['t'][11:16]}Z +{sample['elapsed_min']:.0f}m"
            f" db={db_bytes / 1e6:.2f}MB({'n/a' if prev_db is None else f'{(db_bytes - prev_db) / 1e6:+.2f}'})"
            f" rows={rows}({delta(rows, prev_rows)}) writes={writes}({delta(writes, prev_writes)})"
            f" wolverine={wolverine_text}"
            f" log={sample['log']['lines']}({delta(sample['log']['lines'], prev_lines)}; {level_delta or '-'})"
            f" api={api:.0f}({delta(int(api), int(prev_api)) if prev_api is not None else 'n/a'})"
            f" 429={throttled:.0f} breaker={trips:.0f}/{flags.get('breaker', 0)}"
            f" credits={sample.get('credits')} ships={sample.get('ships')}"
            f" startup={sample['health_startup'][0]}"
            + (f" APP-EXITED({sample['app_exit']})" if sample.get("app_exit") else "")
            + (f" DB-ERROR({sample['db_error'][:80]})" if sample.get("db_error") else ""))
    with open(SOAK / "summary.log", "a", encoding="utf-8") as f:
        f.write(line + "\n")
    print(line, flush=True)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--hours", type=float, default=4)
    parser.add_argument("--interval-min", type=float, default=15)
    parser.add_argument("--container", default="spacetraders-soak-pg")
    parser.add_argument("--database", default="spacetraders")
    parser.add_argument("--base-url", default="http://127.0.0.1:49306/spacetraders/api")
    parser.add_argument("--out", default="soak-output", help="folder that launch.py writes the bot's log to")
    args = parser.parse_args()

    global SOAK
    SOAK = pathlib.Path(args.out).resolve()
    SOAK.mkdir(parents=True, exist_ok=True)

    started = utcnow()
    existing = (SOAK / "samples.jsonl").read_text(encoding="utf-8").splitlines() if (SOAK / "samples.jsonl").exists() else []
    previous = json.loads(existing[-1]) if existing else None
    number = previous["n"] + 1 if previous else 0
    if previous:
        started = dt.datetime.fromisoformat(previous["t"]) - dt.timedelta(minutes=previous["elapsed_min"])

    deadline = started + dt.timedelta(hours=args.hours)
    while True:
        previous = take_sample(args, number, started, previous)
        number += 1
        next_at = started + dt.timedelta(minutes=args.interval_min * number)
        if next_at > deadline + dt.timedelta(seconds=30):
            return 0
        time.sleep(max(0.0, (next_at - utcnow()).total_seconds()))


if __name__ == "__main__":
    sys.exit(main())
