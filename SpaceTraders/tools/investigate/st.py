"""Read-only access to the SpaceTraders bot on the home cluster, for the st-investigate skill.

    python tools/investigate/st.py check
    python tools/investigate/st.py prom 'spacetraders_anomaly_active == 1'
    python tools/investigate/st.py prom 'sum by (type) (rate(spacetraders_messages_handled_total[5m]))' --range 6h
    python tools/investigate/st.py logs '{namespace="spacetraders"} | json | EventKind=~"Anomaly.*"' --since 7d
    python tools/investigate/st.py logs '{namespace="spacetraders"} |~ "\\"@l\\":\\"(Warning|Error|Fatal)\\""' --since 7d --group
    python tools/investigate/st.py sql 'SELECT "ShipSymbol", "Type", "DestWaypoint" FROM ship_assignment_records'

Prometheus and Loki are reached through `kubectl port-forward`, which each call starts and stops (or
reuses, when something already answers on the local port). The database is read as
`spacetraders_ro`, a login that can only read, with psql, or with the psql of the postgres Docker
image when psql isn't installed. Its password comes from psql's password file, never from the
command line. Times are UTC. Anything shaped like a token or a password is masked.
"""
import argparse
import collections
import contextlib
import datetime
import json
import os
import re
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

NAMESPACE = "spacetraders"
# (namespace, service, local port, service port, readiness path)
PROMETHEUS = ("monitoring", "svc/prometheus-server", 19090, 80, "/-/ready")
LOKI = ("monitoring", "svc/grafana-loki", 13100, 3100, "/ready")
DB_HOST, DB_PORT, DB_NAME, DB_USER = "192.168.1.232", "5432", "spacetraders", "spacetraders_ro"
PSQL_IMAGE = "postgres:17"

# The Kubernetes target labels every series carries; `--all-labels` shows them.
TARGET_LABELS = {"app", "instance", "job", "namespace", "node", "pod", "pod_template_hash"}
# CLEF fields that the line itself already shows.
CLEF_FIELDS = {"@t", "@m", "@i", "@l", "@x", "@r", "@tr", "@sp"}
SECRETS = [
    (re.compile(r"eyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+|eyJ[A-Za-z0-9_\-]{10,}"), "<jwt>"),
    (re.compile(r"(?i)(password\s*=\s*)[^;\s\"']+"), r"\1***"),
]
LOKI_BATCH = 5000


def mask(text: str) -> str:
    for pattern, replacement in SECRETS:
        text = pattern.sub(replacement, text)
    return text


def fail(message: str) -> None:
    sys.exit(f"st.py: {message}")


def parse_duration(text: str) -> float:
    match = re.fullmatch(r"(\d+(?:\.\d+)?)([smhdw])", text.strip())
    if not match:
        fail(f"'{text}' isn't a duration such as 30m, 6h or 7d.")
    return float(match.group(1)) * {"s": 1, "m": 60, "h": 3600, "d": 86400, "w": 604800}[match.group(2)]


def parse_time(text: str) -> float:
    """An ISO time (UTC unless it says otherwise), 'now', or a duration ago such as 2h."""
    if text == "now":
        return time.time()
    if re.fullmatch(r"\d+(?:\.\d+)?[smhdw]", text):
        return time.time() - parse_duration(text)
    try:
        moment = datetime.datetime.fromisoformat(text.replace("Z", "+00:00"))
    except ValueError:
        fail(f"'{text}' isn't a time: use ISO (2026-10-02T08:50Z), 'now', or a duration ago (2h).")
    if moment.tzinfo is None:
        moment = moment.replace(tzinfo=datetime.timezone.utc)
    return moment.timestamp()


def utc(seconds: float, millis: bool = False) -> str:
    moment = datetime.datetime.fromtimestamp(seconds, datetime.timezone.utc)
    return moment.strftime("%m-%d %H:%M:%S.%f")[:-3] if millis else moment.strftime("%m-%d %H:%M:%S")


def window(args: argparse.Namespace) -> tuple[float, float]:
    end = parse_time(args.to) if args.to else time.time()
    start = parse_time(args.start) if args.start else end - parse_duration(args.since)
    if start >= end:
        fail("the window is empty: --from must come before --to.")
    return start, end


def auto_step(start: float, end: float, points: int = 120) -> int:
    return max(15, int((end - start) / points) // 15 * 15)


def answers(url: str) -> bool:
    try:
        with urllib.request.urlopen(url, timeout=2) as response:
            return response.status == 200
    except (urllib.error.URLError, OSError):
        return False


@contextlib.contextmanager
def forwarded(target):
    """Yields the base URL of a cluster service, through a port-forward of its own if needed."""
    namespace, service, local_port, service_port, ready = target
    base = f"http://localhost:{local_port}"
    if answers(base + ready):
        yield base
        return
    process = subprocess.Popen(
        ["kubectl", "-n", namespace, "port-forward", service, f"{local_port}:{service_port}"],
        stdout=subprocess.DEVNULL, stderr=subprocess.PIPE, text=True)
    try:
        deadline = time.monotonic() + 20
        while not answers(base + ready):
            if process.poll() is not None:
                fail(f"kubectl port-forward {service} stopped: {process.stderr.read().strip()}")
            if time.monotonic() > deadline:
                fail(f"{service} didn't answer on {base}{ready} within 20 seconds.")
            time.sleep(0.3)
        yield base
    finally:
        process.terminate()
        try:
            process.wait(5)
        except subprocess.TimeoutExpired:
            process.kill()


def get_json(url: str, params: dict) -> dict:
    full = url + "?" + urllib.parse.urlencode(params)
    try:
        with urllib.request.urlopen(full, timeout=120) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        fail(f"{error.code} from {url}: {error.read().decode('utf-8', 'replace')[:800]}")


# --- Prometheus -------------------------------------------------------------------------------


def number(value: str) -> str:
    try:
        parsed = float(value)
    except ValueError:
        return value
    return str(int(parsed)) if parsed.is_integer() and abs(parsed) < 1e15 else f"{parsed:.6g}"


def series_name(metric: dict, all_labels: bool) -> str:
    name = metric.get("__name__", "")
    labels = {k: v for k, v in metric.items()
              if k != "__name__" and (all_labels or k not in TARGET_LABELS)}
    inner = ",".join(f'{k}="{v}"' for k, v in sorted(labels.items()))
    return f"{name}{{{inner}}}" if inner or not name else name


def print_prometheus(data: dict, all_labels: bool, all_points: bool, query: str) -> None:
    kind, result = data["resultType"], data["result"]
    def name_of(metric: dict) -> str:
        return series_name(metric, all_labels) if metric else query
    if kind in ("scalar", "string"):
        print(utc(result[0]), number(result[1]))
        return
    if not result:
        print("(no series)")
        return
    if kind == "vector":
        rows = sorted((name_of(s["metric"]), number(s["value"][1])) for s in result)
        width = min(max(len(name) for name, _ in rows), 140)
        for name, value in rows:
            print(f"{name:<{width}}  {value}")
        return
    for series in sorted(result, key=lambda s: name_of(s["metric"])):
        points, last = [], None
        values = series["values"]
        for index, (stamp, value) in enumerate(values):
            if all_points or value != last or index == len(values) - 1:
                points.append(f"{utc(stamp)}={number(value)}")
            last = value
        print(name_of(series["metric"]))
        print("    " + "  ".join(points))


def command_prom(args: argparse.Namespace) -> None:
    with forwarded(PROMETHEUS) as base:
        if args.range:
            end = parse_time(args.to) if args.to else time.time()
            start = end - parse_duration(args.range)
            step = parse_duration(args.step) if args.step else auto_step(start, end)
            data = get_json(base + "/api/v1/query_range",
                            {"query": args.query, "start": start, "end": end, "step": step})
        else:
            params = {"query": args.query}
            if args.at:
                params["time"] = parse_time(args.at)
            data = get_json(base + "/api/v1/query", params)
    print_prometheus(data["data"], args.all_labels, args.all_points, args.query)


# --- Loki -------------------------------------------------------------------------------------


def fetch_logs(base: str, query: str, start_ns: int, end_ns: int, limit: int, newest: bool,
               step: int):
    """Log lines in time order (the newest `limit` ones, or the oldest), or a metric result."""
    rows, direction = [], "backward" if newest else "forward"
    while len(rows) < limit:
        batch = min(LOKI_BATCH, limit - len(rows))
        data = get_json(base + "/loki/api/v1/query_range",
                        {"query": query, "start": start_ns, "end": end_ns, "limit": batch,
                         "direction": direction, "step": step})["data"]
        if data["resultType"] != "streams":
            return data
        got = sorted(((int(stamp), line, stream["stream"])
                      for stream in data["result"] for stamp, line in stream["values"]),
                     key=lambda row: row[0], reverse=newest)[:batch]
        rows.extend(got)
        if len(got) < batch:
            break
        if newest:
            end_ns = got[-1][0]
        else:
            start_ns = got[-1][0] + 1
    rows.sort(key=lambda row: row[0])
    return rows


def one_line(text: str, full: bool, width: int = 400) -> str:
    if full:
        return text
    text = " ".join(text.split())
    return text if len(text) <= width else text[:width] + " …"


def print_line(stamp_ns: int, line: str, stream: dict, props: bool, exceptions: bool,
               show_pod: bool, full: bool) -> None:
    when = utc(stamp_ns / 1e9, millis=True)
    if show_pod:  # a restart or a deploy shows as a new pod suffix
        when += f" {stream.get('pod', '?')[-5:]}"
    try:
        event = json.loads(line)
    except json.JSONDecodeError:
        print(mask(f"{when} [{stream.get('container', '?')}] {one_line(line, full)}"))
        return
    if not isinstance(event, dict):
        print(mask(f"{when} {one_line(line, full)}"))
        return
    message = one_line(str(event.get("@m", line)), full)
    text = f"{when} {event.get('@l', 'Information')[:4]:4} {message}"
    if props:
        extra = {k: v for k, v in event.items() if k not in CLEF_FIELDS}
        text += "  " + json.dumps(extra, ensure_ascii=False)
    if exceptions and "@x" in event:
        text += "\n      " + event["@x"].replace("\n", "\n      ")
    print(mask(text))


def print_groups(rows) -> None:
    """One row per statement: level, template id (@i), class, count, first and last seen."""
    groups = collections.OrderedDict()
    for stamp, line, _ in rows:
        try:
            event = json.loads(line)
        except json.JSONDecodeError:
            event = {"@m": line.strip()}
        if not isinstance(event, dict):
            event = {"@m": line.strip()}
        source = str(event.get("SourceContext", "")).split(".")[-1]
        key = (event.get("@l", "Information"), event.get("@i", "-"), source)
        group = groups.setdefault(key, {"count": 0, "first": stamp, "last": stamp,
                                        "message": event.get("@m", "")})
        group["count"] += 1
        group["last"] = stamp
    for (level, template, source), group in sorted(groups.items(), key=lambda kv: -kv[1]["count"]):
        print(mask(f"{group['count']:6} {level[:4]:4} @i={template} {source} "
                   f"{utc(group['first'] / 1e9)}..{utc(group['last'] / 1e9)} | "
                   f"{one_line(str(group['message']), False, 220)}"))


def command_logs(args: argparse.Namespace) -> None:
    start, end = window(args)
    limit = args.limit or (1_000_000 if args.group else 300)
    step = parse_duration(args.step) if args.step else auto_step(start, end)
    with forwarded(LOKI) as base:
        result = fetch_logs(base, args.query, int(start * 1e9), int(end * 1e9), limit,
                            newest=not args.oldest, step=int(step))
    if isinstance(result, dict):
        print_prometheus(result, args.all_labels, args.all_points, args.query)
        return
    if args.group:
        print_groups(result)
    else:
        show_pod = len({stream.get("pod") for _, _, stream in result}) > 1
        for stamp, line, stream in result:
            print_line(stamp, line, stream, args.props, args.exceptions, show_pod, args.full)
    more = " (the limit: there may be more, see --limit)" if len(result) >= limit else ""
    sys.stdout.flush()
    print(f"-- {len(result)} lines, {utc(start)} to {utc(end)} UTC{more}", file=sys.stderr)


# --- Postgres ---------------------------------------------------------------------------------


def password_file() -> str:
    if os.environ.get("PGPASSFILE"):
        return os.environ["PGPASSFILE"]
    if os.name == "nt":
        return os.path.join(os.environ["APPDATA"], "postgresql", "pgpass.conf")
    return os.path.expanduser("~/.pgpass")


def split_password_line(line: str) -> list[str]:
    fields, current, index = [], [], 0
    while index < len(line):
        char = line[index]
        if char == "\\" and index + 1 < len(line):
            current.append(line[index + 1])
            index += 2
            continue
        if char == ":" and len(fields) < 4:
            fields.append("".join(current))
            current = []
        else:
            current.append(char)
        index += 1
    fields.append("".join(current))
    return fields


def read_password() -> str:
    path = password_file()
    wanted = f"{DB_HOST}:{DB_PORT}:{DB_NAME}:{DB_USER}:<password>"
    if not os.path.exists(path):
        fail(f"no password file at {path}. Ask the user to create it with the line {wanted}")
    raw = open(path, "rb").read()
    text = raw.decode("utf-16") if raw[:2] in (b"\xff\xfe", b"\xfe\xff") else raw.decode("utf-8-sig")
    for line in text.splitlines():
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        fields = split_password_line(line.strip())
        if len(fields) == 5 and all(field in ("*", want) for field, want
                                    in zip(fields[:4], (DB_HOST, DB_PORT, DB_NAME, DB_USER))):
            return fields[4]
    fail(f"{path} has no line for {DB_USER}. Ask the user to add {wanted}")


def run_sql(query: str) -> int:
    arguments = ["-h", DB_HOST, "-p", DB_PORT, "-U", DB_USER, "-d", DB_NAME, "-X", "-q",
                 "-v", "ON_ERROR_STOP=1", "-P", "pager=off", "-f", "-"]
    # Read-only twice: the login's own default, and this session's.
    environment = dict(os.environ, PGOPTIONS="-c default_transaction_read_only=on",
                       PGCONNECT_TIMEOUT="10")
    native = shutil.which("psql")
    if native:
        command = [native, *arguments]  # libpq finds the password file itself
    else:
        environment["PGPASSWORD"] = read_password()
        command = ["docker", "run", "--rm", "-i", "-e", "PGPASSWORD", "-e", "PGOPTIONS",
                   "-e", "PGCONNECT_TIMEOUT", PSQL_IMAGE, "psql", *arguments]
    completed = subprocess.run(command, input=query, env=environment, capture_output=True,
                               text=True, encoding="utf-8", errors="replace")
    sys.stdout.write(mask(completed.stdout))
    errors = "\n".join(line for line in completed.stderr.splitlines()
                       if "psql major version" not in line and "Some psql features" not in line)
    if errors.strip():
        sys.stderr.write(mask(errors) + "\n")
    return completed.returncode


def command_sql(args: argparse.Namespace) -> None:
    query = sys.stdin.read() if args.query == "-" else args.query
    if "stored_credentials" in query.lower():
        fail("stored_credentials holds the agent token: never read it.")
    sys.exit(run_sql(query))


# --- check ------------------------------------------------------------------------------------


def kubectl_json(*arguments: str):
    completed = subprocess.run(["kubectl", *arguments, "-o", "json"], capture_output=True,
                               text=True, encoding="utf-8", errors="replace")
    if completed.returncode != 0:
        raise RuntimeError(completed.stderr.strip())
    return json.loads(completed.stdout)


def check_pods() -> None:
    print("== Pods (namespace spacetraders)")
    for pod in kubectl_json("-n", NAMESPACE, "get", "pods")["items"]:
        for status in pod["status"].get("containerStatuses", []):
            started = pod["status"].get("startTime", "?")
            image = status["image"].rsplit(":", 1)[-1][:7]
            state = next(iter(status.get("state", {})), "?")
            print(f"   {pod['metadata']['name']}  {state}  ready={status['ready']}  "
                  f"restarts={status['restartCount']}  started={started}  image={image}")
    def stamp(event: dict) -> str:
        return event.get("lastTimestamp") or event.get("eventTime") or ""
    warnings = sorted((event for event in kubectl_json("-n", NAMESPACE, "get", "events")["items"]
                       if event.get("type") == "Warning"), key=stamp)
    print(f"   warning events (Kubernetes keeps them for about an hour): {len(warnings)}")
    for event in warnings[-10:]:
        print(f"   {stamp(event)} {event['involvedObject'].get('name')} {event.get('reason')} "
              f"x{event.get('count', 1)}: {event.get('message', '')[:160]}")


def check_prometheus() -> None:
    print("== Prometheus")
    with forwarded(PROMETHEUS) as base:
        up = get_json(base + "/api/v1/query", {"query": f'up{{namespace="{NAMESPACE}"}}'})
        names = get_json(base + "/api/v1/query",
                         {"query": 'count(count by (__name__) ({__name__=~"spacetraders_.*"}))'})
        active = get_json(base + "/api/v1/query", {"query": "spacetraders_anomaly_active == 1"})
    for series in up["data"]["result"]:
        print(f"   up{{pod=\"{series['metric'].get('pod')}\"}} = {series['value'][1]}")
    if not up["data"]["result"]:
        print("   no 'up' series for the namespace: Prometheus doesn't scrape the bot")
    count = names["data"]["result"][0]["value"][1] if names["data"]["result"] else "0"
    print(f"   spacetraders_* metric names: {count}")
    print(f"   active anomalies: {len(active['data']['result'])}")
    for series in active["data"]["result"]:
        print(f"     {series['metric'].get('rule')} {series['metric'].get('subject')}")


def check_loki() -> None:
    print("== Loki")
    with forwarded(LOKI) as base:
        data = get_json(base + "/loki/api/v1/query",
                        {"query": f'sum(count_over_time({{namespace="{NAMESPACE}"}}[1h]))'})
    result = data["data"]["result"]
    print(f"   lines in the last hour: {number(result[0]['value'][1]) if result else 0}")


def check_database() -> None:
    print("== Postgres (as spacetraders_ro)")
    code = run_sql("""
SELECT current_user AS login,
       (SELECT rolsuper FROM pg_roles WHERE rolname = current_user) AS superuser,
       current_setting('default_transaction_read_only') AS read_only,
       CASE WHEN to_regclass('public.stored_credentials') IS NULL THEN NULL
            ELSE has_table_privilege('public.stored_credentials', 'SELECT') END AS can_read_agent_token,
       pg_size_pretty(pg_database_size(current_database())) AS database_size;
""")
    if code != 0:
        print("   failed: see above")


def command_check(_: argparse.Namespace) -> None:
    for check in (check_pods, check_prometheus, check_loki, check_database):
        try:
            check()
        except SystemExit as error:
            print(f"   failed: {error}")
        except Exception as error:  # report every source, whatever fails
            print(f"   failed: {error}")
    print("A database line with can_read_agent_token = t means step 3 of gembernodes'"
          " apps/spacetraders/README.md is still to do: never read stored_credentials.")


def main() -> None:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    commands = parser.add_subparsers(dest="command", required=True)

    check = commands.add_parser("check", help="what each source answers: pods, Prometheus, Loki, Postgres")
    check.set_defaults(run=command_check)

    prom = commands.add_parser("prom", help="a PromQL query, now or over a range")
    prom.add_argument("query")
    prom.add_argument("--range", help="a range query over this long, such as 6h or 7d")
    prom.add_argument("--step", help="the range query's resolution (default: about 120 points)")
    prom.add_argument("--to", help="the end of the range (default: now)")
    prom.add_argument("--at", help="an instant query at this time instead of now")
    prom.add_argument("--all-labels", action="store_true", help="keep pod, instance and the other target labels")
    prom.add_argument("--all-points", action="store_true", help="print every point, not only changes")
    prom.set_defaults(run=command_prom)

    logs = commands.add_parser("logs", help="a LogQL query: lines, statements (--group) or a metric")
    logs.add_argument("query")
    logs.add_argument("--since", default="1h", help="how far back (default 1h), such as 30m, 2d")
    logs.add_argument("--from", dest="start", help="the start, an ISO time (UTC unless it says otherwise)")
    logs.add_argument("--to", help="the end (default: now)")
    logs.add_argument("--limit", type=int, help="lines to fetch (default 300; all with --group)")
    logs.add_argument("--oldest", action="store_true", help="the oldest lines in the window, not the newest")
    logs.add_argument("--group", action="store_true", help="count lines per statement (@i): count, first, last")
    logs.add_argument("--props", action="store_true", help="print each line's properties")
    logs.add_argument("--exceptions", action="store_true", help="print each line's exception")
    logs.add_argument("--full", action="store_true", help="whole messages, not one line of 400 characters")
    logs.add_argument("--step", help="for a metric query, its resolution")
    logs.add_argument("--all-labels", action="store_true", help=argparse.SUPPRESS)
    logs.add_argument("--all-points", action="store_true", help=argparse.SUPPRESS)
    logs.set_defaults(run=command_logs)

    sql = commands.add_parser("sql", help="SQL as spacetraders_ro ('-' reads it from stdin)")
    sql.add_argument("query")
    sql.set_defaults(run=command_sql)

    args = parser.parse_args()
    args.run(args)


if __name__ == "__main__":
    main()
