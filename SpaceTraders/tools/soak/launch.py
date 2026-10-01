"""Runs a published SpaceTraders API host for a soak test (PLAN.md slice 1.14).

Configured like the cluster (Production: JSON logs, the same Serilog levels), but against a local
soak database and on loopback only (the API on 49306, the metrics on 9090), without an internal
API key. The account token, agent name and
faction come from the API project's user secrets; they reach the child process through its
environment and are never printed. The user secrets' own connection string points at the cluster's
shared Postgres, so the soak database's connection string always replaces it.

Usage: python launch.py --app <publish folder> [--out soak-output]
"""
import argparse
import datetime as dt
import json
import os
import pathlib
import subprocess
import sys

CONNECTION = "Host=127.0.0.1;Port=55432;Database=spacetraders;Username=postgres;Password=soak"
URLS = "http://127.0.0.1:49306"
METRICS_PORT = "9090"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--app", required=True, help="folder with the published SpaceTraders.API.dll")
    parser.add_argument("--out", default="soak-output", help="folder for logs and samples")
    args = parser.parse_args()
    app = pathlib.Path(args.app).resolve()
    logs = pathlib.Path(args.out).resolve() / "logs"
    logs.mkdir(parents=True, exist_ok=True)

    secrets_path = pathlib.Path(os.environ["APPDATA"]) / "Microsoft" / "UserSecrets" / "SpaceTraders-API" / "secrets.json"
    secrets = json.loads(secrets_path.read_text(encoding="utf-8-sig"))["SpaceTraders"]

    env = {k: v for k, v in os.environ.items()
           if not k.upper().startswith(("SPACETRADERS", "CONNECTIONSTRINGS", "ASPNETCORE_", "DOTNET_ENVIRONMENT"))}
    env.update({
        "ASPNETCORE_ENVIRONMENT": "Production",
        "ASPNETCORE_URLS": URLS,
        "Metrics__Port": METRICS_PORT,
        "Metrics__Hostname": "127.0.0.1",
        "ConnectionStrings__DefaultConnection": CONNECTION,
        "SpaceTraders__AccountToken": secrets["AccountToken"],
        "SpaceTraders__AgentName": secrets["AgentName"],
        "SpaceTraders__AgentFaction": secrets.get("AgentFaction") or "COSMIC",
    })

    exit_file = logs / "exit.txt"
    if exit_file.exists():
        exit_file.rename(logs / f"exit-{dt.datetime.now(dt.timezone.utc):%Y%m%dT%H%M%S}.txt")

    with open(logs / "app.log", "ab") as out, open(logs / "app.err.log", "ab") as err:
        started = dt.datetime.now(dt.timezone.utc)
        proc = subprocess.Popen(["dotnet", "SpaceTraders.API.dll"], cwd=app, env=env, stdout=out, stderr=err)
        (logs / "app.pid").write_text(str(proc.pid))
        print(f"started {app.name} as pid {proc.pid} at {started.isoformat()}", flush=True)
        code = proc.wait()

    ended = dt.datetime.now(dt.timezone.utc)
    exit_file.write_text(f"{code} {ended.isoformat()}\n")
    print(f"exited with code {code} at {ended.isoformat()}", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
