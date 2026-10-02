# Investigation tools

`st.py` reads the bot's data on the home cluster, read-only, for the `st-investigate` skill
(`.claude/skills/st-investigate/SKILL.md`, `PLAN.md` slice 5.1). It needs Python 3.11 or later,
`kubectl` with the cluster's context, and psql or Docker. Run it from the `SpaceTraders` folder, in
bash (Git Bash on Windows; Windows PowerShell mangles the double quotes in queries):

```bash
python tools/investigate/st.py check
python tools/investigate/st.py prom 'spacetraders_anomaly_active == 1'
python tools/investigate/st.py logs '{namespace="spacetraders"} | json | EventKind != ""' --since 6h
python tools/investigate/st.py sql 'SELECT "ShipSymbol", "Type", "DestWaypoint" FROM ship_assignment_records'
```

| Command | Reads | How |
|---|---|---|
| `check` | Each source in turn: the pods, Prometheus, Loki and the database | Below |
| `prom` | Prometheus: a PromQL query now, at a time (`--at`) or over a range (`--range`) | `kubectl port-forward` to `monitoring/prometheus-server`, on local port 19090 |
| `logs` | Loki: log lines, the newest first unless `--oldest`; `--group` counts them per statement (`@i`); a metric query prints series | `kubectl port-forward` to `monitoring/grafana-loki`, on local port 13100 |
| `sql` | The `spacetraders` database, as `spacetraders_ro` | psql, or psql from the `postgres:17` Docker image |

Each call starts the port-forward it needs and stops it when done, or uses one that already answers
on that port. Times are UTC. Output masks anything shaped like a JWT or a `Password=` value.

## The database login

`spacetraders_ro` is the read-only login of gembernodes' `apps/spacetraders/README.md` (slice 4.1).
Its password goes in psql's password file, which libpq reads by itself; `st.py` reads it to pass it
to the Docker image:

- Windows: `%APPDATA%\postgresql\pgpass.conf`
- elsewhere: `~/.pgpass` (mode 0600)

with this line:

```text
192.168.1.232:5432:spacetraders:spacetraders_ro:<password>
```

Every session is read-only twice: the login's default, and `PGOPTIONS` for each call. `sql` refuses
any query that names `stored_credentials`, which holds the agent token; `check` reports whether the
login can still read it (step 3 of the gembernodes README takes that right away).
