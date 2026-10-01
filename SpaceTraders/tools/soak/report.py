"""Turns the soak-test samples and the bot's log into a markdown report (report.md).

Reads samples.jsonl (sampler.py), logs/app.log, and the live api_endpoint_usages table.
"""
import argparse
import collections
import datetime as dt
import json
import pathlib
import re
import subprocess

SOAK = pathlib.Path("soak-output").resolve()  # replaced by --out
JWT = re.compile(r"eyJ[A-Za-z0-9_\-]{10,}(\.[A-Za-z0-9_\-]+)*")
BUCKET_MIN = 15


def parse_time(value: str) -> dt.datetime:
    value = value.replace("Z", "+00:00")
    # .NET writes 7 fractional digits; Python takes at most 6.
    value = re.sub(r"\.(\d{6})\d+", r".\1", value)
    return dt.datetime.fromisoformat(value)


def load_samples() -> list[dict]:
    return [json.loads(line) for line in (SOAK / "samples.jsonl").read_text(encoding="utf-8").splitlines() if line]


def load_log() -> list[dict]:
    events = []
    with open(SOAK / "logs" / "app.log", "rb") as f:
        for raw in f:
            line = raw.decode("utf-8", "replace").replace("\x1a", "?").strip()
            if not line:
                continue
            try:
                event = json.loads(line)
            except json.JSONDecodeError:
                events.append({"@t": None, "@m": line[:200], "@l": "NonJson"})
                continue
            event.setdefault("@l", "Information")
            events.append(event)
    return events


def psql(sql: str) -> list[list[str]]:
    result = subprocess.run(["docker", "exec", "spacetraders-soak-pg", "psql", "-U", "postgres", "-d", "spacetraders",
                             "-AtF", "\t", "-c", sql], capture_output=True, text=True, timeout=120)
    return [line.split("\t") for line in result.stdout.splitlines() if line]


def mask(text: str) -> str:
    return JWT.sub("<jwt>", text).replace("|", "\\|")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", default="soak-output", help="folder with the samples and the bot's log")
    global SOAK
    SOAK = pathlib.Path(parser.parse_args().out).resolve()

    samples = load_samples()
    events = load_log()
    first, last = samples[0], samples[-1]
    start = parse_time(next(e["@t"] for e in events if e.get("@t")))
    end = parse_time(last["t"])
    hours = (end - start).total_seconds() / 3600
    out: list[str] = []
    w = out.append

    w(f"# Soak test report\n")
    w(f"- Bot started {start:%Y-%m-%d %H:%M} UTC; last sample {end:%H:%M} UTC ({hours:.2f} h, {len(samples)} samples).")
    w(f"- Last app exit: {last.get('app_exit') or 'still running'}.\n")

    # --- Wolverine / schemas
    schemas = sorted({s for sample in samples for s in sample.get("schemas", [])})
    wolverine_tables = sorted({f"{t['schema']}.{t['table']}" for sample in samples for t in sample["tables"]
                               if "wolverine" in (t["schema"] + t["table"]).lower()})
    w("## Wolverine storage\n")
    w(f"- Schemas seen in any sample: {', '.join(schemas)}.")
    w(f"- Tables with 'wolverine' in schema or name, in any sample: {', '.join(wolverine_tables) or 'none'}.\n")

    # --- Breaker, 429
    def metric_sum(sample, prefix):
        return sum(v for k, v in sample["metrics"].items() if k.startswith(prefix))

    w("## Circuit breaker, 429s, resets\n")
    w(f"- `spacetraders_goal_breaker_trips_total` at the end: {metric_sum(last, 'spacetraders_goal_breaker_trips_total'):.0f}; "
      f"breaker log lines: {last['log']['flags'].get('breaker', 0)}.")
    w(f"- `spacetraders_api_throttled_total` at the end: {metric_sum(last, 'spacetraders_api_throttled_total'):.0f}; "
      f"429 log lines: {last['log']['flags'].get('throttled', 0)}.")
    w(f"- ResetDetected: {last['log']['flags'].get('reset', 0)}, ApiUnavailable: {last['log']['flags'].get('api_unavailable', 0)}, "
      f"DbSize warnings: {last['log']['flags'].get('db_size', 0)}, Wolverine retries: {last['log']['flags'].get('wolverine_retry', 0)}, "
      f"failed tick steps: {last['log']['flags'].get('tick_step_failed', 0)}, lines with an exception: {last['log']['flags'].get('exceptions', 0)}.")
    w(f"- Lines that look like a JWT: {last['log']['token_like_lines']}.\n")

    # --- Tables
    w("## Tables (first vs last sample)\n")
    w("| Table | Rows first | Rows last | Δ rows | Bytes first | Bytes last | Inserts | Updates | Deletes |")
    w("|---|---:|---:|---:|---:|---:|---:|---:|---:|")
    by_name_first = {t["table"]: t for t in first["tables"]}
    for t in sorted(last["tables"], key=lambda t: -(t["ins"] + t["upd"] + t["del"])):
        f0 = by_name_first.get(t["table"], {"rows": 0, "bytes": 0})
        w(f"| `{t['table']}` | {f0['rows']} | {t['rows']} | {t['rows'] - f0['rows']:+} | {f0['bytes']:,} | {t['bytes']:,} | "
          f"{t['ins']:,} | {t['upd']:,} | {t['del']:,} |")
    w("")

    # --- Per-sample trend
    w("## Per sample\n")
    w("| # | Time | DB MB | Rows | Writes (ins+upd+del) | Log lines | API calls | Credits | Working set MB |")
    w("|---:|---|---:|---:|---:|---:|---:|---:|---:|")
    for s in samples:
        rows = sum(t["rows"] for t in s["tables"])
        writes = sum(t["ins"] + t["upd"] + t["del"] for t in s["tables"])
        ws = s["metrics"].get("process_working_set_bytes", 0) / 1e6
        w(f"| {s['n']} | {s['t'][11:16]} | {s.get('db', {}).get('bytes', 0) / 1e6:.2f} | {rows} | {writes:,} | "
          f"{s['log']['lines']:,} | {metric_sum(s, 'spacetraders_api_requests_total') or metric_sum(s, 'spacetraders_api_calls_total'):,.0f} | {s.get('credits')} | {ws:.0f} |")
    w("")

    # --- Log volume per bucket
    buckets: dict[int, collections.Counter] = collections.defaultdict(collections.Counter)
    messages: collections.Counter = collections.Counter()
    templates: dict[str, dict] = {}
    for e in events:
        if not e.get("@t"):
            continue
        t = parse_time(e["@t"])
        if t > end:
            continue  # after the last sample: keeps the rates and the sampled window the same
        b = int((t - start).total_seconds() // (BUCKET_MIN * 60))
        buckets[b][e["@l"]] += 1
        buckets[b]["_all"] += 1
        m = e.get("@m", "")
        if m.startswith("Successfully processed message"):
            buckets[b]["_wolverine_success"] += 1
            messages[e.get("Name", "?").split(".")[-1]] += 1
        key = e.get("@i") or m[:60]
        entry = templates.setdefault(key, {"count": 0, "level": e["@l"], "sample": m, "source": e.get("SourceContext", "")})
        entry["count"] += 1

    # Runs: each process start logs "Now listening on".
    runs: list[dict] = []
    for e in events:
        if not e.get("@t"):
            continue
        t = parse_time(e["@t"])
        if t > end:
            break
        if e.get("@m", "").startswith("Now listening on") or not runs:
            runs.append({"start": t, "end": t, "levels": collections.Counter(), "wolverine": 0, "lines": 0})
        run = runs[-1]
        run["end"] = t
        run["lines"] += 1
        run["levels"][e["@l"]] += 1
        if e.get("@m", "").startswith("Successfully processed message"):
            run["wolverine"] += 1
    w("## Runs\n")
    w("| Run | From | To | Hours | Lines | Lines/hour | Warn | Error | Wolverine success lines |")
    w("|---:|---|---|---:|---:|---:|---:|---:|---:|")
    for i, run in enumerate(runs, 1):
        run_end = runs[i]["start"] if i < len(runs) else end
        run_hours = max((run_end - run["start"]).total_seconds() / 3600, 1e-6)
        w(f"| {i} | {run['start']:%H:%M} | {run_end:%H:%M} | {run_hours:.2f} | {run['lines']:,} | {run['lines'] / run_hours:,.0f} | "
          f"{run['levels']['Warning']} | {run['levels']['Error'] + run['levels']['Fatal']} | {run['wolverine']} |")
    w("")

    total = sum(c["_all"] for c in buckets.values())
    w("## Log volume\n")
    w(f"- {total:,} lines in {hours:.2f} h: {total / hours:,.0f} an hour, {total / hours * 24:,.0f} a day at this rate "
      f"(budget: 50,000 a day).")
    wolverine_total = sum(c["_wolverine_success"] for c in buckets.values())
    w(f"- Of those, {wolverine_total:,} are Wolverine's \"Successfully processed message\" lines "
      f"({wolverine_total / max(total, 1):.0%}).\n")
    w("| Bucket (min) | Lines | Lines/min | Info | Warn | Error | Wolverine success |")
    w("|---|---:|---:|---:|---:|---:|---:|")
    for b in sorted(buckets):
        c = buckets[b]
        w(f"| {b * BUCKET_MIN}–{(b + 1) * BUCKET_MIN} | {c['_all']} | {c['_all'] / BUCKET_MIN:.1f} | {c['Information']} | "
          f"{c['Warning']} | {c['Error'] + c['Fatal']} | {c['_wolverine_success']} |")
    w("")

    w("## Messages handled (Wolverine success lines, by type)\n")
    w("| Message | Count |")
    w("|---|---:|")
    for name, count in messages.most_common():
        w(f"| `{name}` | {count} |")
    w("")

    w("## Top log templates\n")
    w("| Count | Level | Source | Example |")
    w("|---:|---|---|---|")
    for key, entry in sorted(templates.items(), key=lambda kv: -kv[1]["count"])[:30]:
        w(f"| {entry['count']} | {entry['level']} | `{entry['source'].split('.')[-1]}` | {mask(entry['sample'][:160])} |")
    w("")

    w("## Warnings and errors (all templates)\n")
    w("| Count | Level | Example |")
    w("|---:|---|---|")
    for key, entry in sorted(templates.items(), key=lambda kv: -kv[1]["count"]):
        if entry["level"] in ("Warning", "Error", "Fatal", "NonJson"):
            w(f"| {entry['count']} | {entry['level']} | {mask(entry['sample'][:220])} |")
    w("")

    # --- API calls by endpoint
    rows = psql("select \"HttpMethod\", regexp_replace(\"Endpoint\", '(SPECTER-DEBUG2-)[0-9]+|X1-[A-Z0-9]+-[A-Z0-9]+|[a-z0-9]{25}', '{id}', 'g'), "
                "sum(\"Calls\") from api_endpoint_usages group by 1, 2 order by 3 desc;")
    if rows:
        calls = sum(int(r[2]) for r in rows)
        w("## API calls by endpoint (api_endpoint_usages)\n")
        w(f"- {calls:,} calls recorded ({calls / hours:,.0f} an hour).\n")
        w("| Method | Endpoint | Calls |")
        w("|---|---|---:|")
        for r in rows[:25]:
            w(f"| {r[0]} | `{r[1]}` | {int(r[2]):,} |")
        w("")

    (SOAK / "report.md").write_text("\n".join(out) + "\n", encoding="utf-8")
    print(f"wrote {SOAK / 'report.md'} ({len(out)} lines)")


if __name__ == "__main__":
    main()
