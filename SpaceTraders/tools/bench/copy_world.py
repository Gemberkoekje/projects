"""Copy the cluster's SpaceTraders database into a local Postgres, for the tick bench (PLAN.md slice 6.60).

Read-only on the cluster: it dumps as `spacetraders_ro` (tools/investigate), with the session read-only too, and never
the agent token (`stored_credentials` is left out entirely). The local copy is a throwaway container on loopback; every
run replaces it. The game's production chains (public, no token) are saved beside it, as the bench's stub port serves them.

    python tools/bench/copy_world.py --out ../bench

Then run the bench with the connection string it prints (see README.md).
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import time
import urllib.request

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "investigate"))
import st  # noqa: E402  (tools/investigate/st.py: the read-only login and its password file)

CONTAINER = "spacetraders-bench-pg"
IMAGE = "postgres:18"  # pg_dump must be at least the server's version (the cluster runs 18)
LOCAL_PORT = "55433"
LOCAL_DB = "spacetraders"
SUPPLY_CHAIN_URL = "https://api.spacetraders.io/v2/market/supply-chain"

# Tables whose rows the plans never read: left empty in the copy, which keeps it small.
EMPTY_TABLES = ["activity_logs", "startup_snapshots", "api_endpoint_usages"]


def run(command: list[str], **kwargs) -> subprocess.CompletedProcess:
    return subprocess.run(command, check=True, **kwargs)


def start_local_postgres() -> None:
    subprocess.run(["docker", "rm", "-f", CONTAINER], capture_output=True)
    run(["docker", "run", "-d", "--name", CONTAINER, "-e", f"POSTGRES_DB={LOCAL_DB}", "-e", "POSTGRES_USER=postgres",
         "-e", "POSTGRES_HOST_AUTH_METHOD=trust", "-p", f"127.0.0.1:{LOCAL_PORT}:5432", IMAGE], capture_output=True)
    for _ in range(60):
        ready = subprocess.run(["docker", "exec", CONTAINER, "pg_isready", "-U", "postgres", "-d", LOCAL_DB],
                               capture_output=True)
        if ready.returncode == 0:
            return
        time.sleep(1)
    st.fail(f"{CONTAINER} didn't get ready within a minute.")


def cluster_environment() -> dict[str, str]:
    # Read-only twice, as tools/investigate/st.py: the login's own default, and this session's.
    return dict(os.environ, PGPASSWORD=st.read_password(), PGOPTIONS="-c default_transaction_read_only=on",
                PGCONNECT_TIMEOUT="10")


def cluster(tool: str, *arguments: str) -> list[str]:
    return ["docker", "run", "--rm", "-i", "-e", "PGPASSWORD", "-e", "PGOPTIONS", "-e", "PGCONNECT_TIMEOUT", IMAGE,
            tool, "-h", st.DB_HOST, "-p", st.DB_PORT, "-U", st.DB_USER, "-d", st.DB_NAME, *arguments]


def local(*arguments: str) -> list[str]:
    return ["docker", "exec", "-i", CONTAINER, "psql", "-U", "postgres", "-d", LOCAL_DB, "-X", "-q",
            "-v", "ON_ERROR_STOP=1", *arguments]


def copy_database() -> None:
    """The schema with pg_dump, then each table's rows with COPY: the read-only login may read the tables, not the
    sequences pg_dump would read their values from, so the sequences are set from the copied rows."""
    environment = cluster_environment()
    schema = run(cluster("pg_dump", "--schema=public", "--schema-only", "--no-owner", "--no-privileges",
                         "--exclude-table=stored_credentials"), env=environment, capture_output=True)
    run(local("-c", "DROP SCHEMA public CASCADE"), capture_output=True)  # the dump creates it
    run(local(), input=schema.stdout, capture_output=True)

    tables = run(local("-Atc", "SELECT tablename FROM pg_tables WHERE schemaname = 'public' ORDER BY tablename"),
                 capture_output=True, text=True).stdout.split()
    for table in tables:
        if table in EMPTY_TABLES:
            continue
        rows = run(cluster("psql", "-X", "-q", "-v", "ON_ERROR_STOP=1", "-c",
                           f"\\copy public.\"{table}\" TO STDOUT (FORMAT binary)"),
                   env=environment, capture_output=True)
        # Foreign keys wait for the other tables: replica mode skips their triggers while the rows go in.
        run(local("-c", "SET session_replication_role = replica",
                  "-c", f"\\copy public.\"{table}\" FROM STDIN (FORMAT binary)"),
            input=rows.stdout, capture_output=True)

    run(local("-c", """
        DO $$
        DECLARE c record; next bigint;
        BEGIN
          FOR c IN SELECT table_name, column_name FROM information_schema.columns
                   WHERE table_schema = 'public' AND (is_identity = 'YES' OR column_default LIKE 'nextval%')
          LOOP
            EXECUTE format('SELECT COALESCE(max(%I), 0) + 1 FROM public.%I', c.column_name, c.table_name) INTO next;
            PERFORM setval(pg_get_serial_sequence(format('public.%I', c.table_name), c.column_name), next, false);
          END LOOP;
        END $$;"""), capture_output=True)


def save_supply_chain(out: str) -> str:
    request = urllib.request.Request(SUPPLY_CHAIN_URL, headers={"Accept": "application/json", "User-Agent": "bench"})
    with urllib.request.urlopen(request, timeout=30) as response:
        chains = json.load(response)["data"]["exportToImportMap"]
    path = os.path.join(out, "supply-chain.json")
    with open(path, "w", encoding="utf-8") as file:
        json.dump(chains, file, indent=1, sort_keys=True)
    return path


def counts() -> str:
    query = ("SELECT (SELECT count(*) FROM cached_ships) || ' ships, ' || (SELECT count(*) FROM cached_markets) || "
             "' markets, ' || (SELECT count(*) FROM cached_systems) || ' systems, ' || "
             "(SELECT count(*) FROM market_price_samples) || ' price samples'")
    result = run(["docker", "exec", CONTAINER, "psql", "-U", "postgres", "-d", LOCAL_DB, "-Atc", query],
                 capture_output=True, text=True)
    return result.stdout.strip()


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--out", required=True, help="a folder outside the source tree for the supply chain file")
    args = parser.parse_args()
    out = os.path.abspath(args.out)
    os.makedirs(out, exist_ok=True)

    started = time.monotonic()
    start_local_postgres()
    copy_database()
    chain_path = save_supply_chain(out)
    print(f"Copied in {time.monotonic() - started:.0f} s: {counts()}.")
    print(f"SPACETRADERS_BENCH_DB=Host=127.0.0.1;Port={LOCAL_PORT};Database={LOCAL_DB};Username=postgres")
    print(f"SPACETRADERS_BENCH_SUPPLY_CHAIN={chain_path}")


if __name__ == "__main__":
    main()
