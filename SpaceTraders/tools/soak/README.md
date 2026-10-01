# Soak test tools

The tools behind the soak test of `PLAN.md` slice 1.14: run the bot against the live API for a few
hours, from an empty local database, and record every 15 minutes what it writes and logs.

Two rules:
- **One instance per account.** The SpaceTraders rate limit is per account: never soak while
  another instance (the cluster's, say) uses the same account.
- **Never the cluster's database.** The API project's user secrets point at the cluster's shared
  Postgres. `launch.py` always replaces that connection string with the local one.

## Run

From the `SpaceTraders` folder:

1. An empty Postgres, on loopback only:

   ```powershell
   docker run -d --name spacetraders-soak-pg -e POSTGRES_DB=spacetraders -e POSTGRES_USER=postgres -e POSTGRES_PASSWORD=soak -p 127.0.0.1:55432:5432 postgres:16-alpine
   ```

2. A Release build outside the source tree, so the running bot doesn't lock what you build:

   ```powershell
   dotnet publish SpaceTraders.API -c Release -o ..\soak\app
   ```

3. The bot, configured like the cluster (Production, JSON logs), with the account token, agent
   name and faction from the API project's user secrets:

   ```powershell
   python tools\soak\launch.py --app ..\soak\app --out ..\soak\out
   ```

4. The sampler, every 15 minutes for 4 hours:

   ```powershell
   python tools\soak\sampler.py --out ..\soak\out --hours 4
   ```

5. The report, at any time:

   ```powershell
   python tools\soak\report.py --out ..\soak\out
   ```

To restart the bot on another build, stop it (`taskkill /F /PID <pid in out\logs\app.pid>`) and
start `launch.py` again with the new `--app`. The log and the samples carry on; the report splits
the log into runs.

## What gets recorded

Per sample, in `samples.jsonl`, `tables.csv`, `summary.log`, `metrics/NN.txt` and `context/NN.json`:
- every table: rows, bytes, and its inserts, updates and deletes, so a small table that is
  rewritten in a loop shows up too; and the schemas, where a `wolverine` schema must not appear;
- the database size and its transaction counts;
- `/metrics` (from the metrics port, 9090, since slice 2.1), the health endpoints, and the status
  endpoints (credits, ships, contracts);
- the log: lines by level and by message template, plus the lines slice 1.14 is about (circuit
  breaker trips, 429s, reset detection, errors).

`logview.py` prints the JSON log readably and masks anything shaped like a token.

## On Windows

- The console's code page (850) writes characters such as "→" as the byte 0x1A, which isn't
  valid in JSON; the scripts read it as "?". On the cluster the log is UTF-8.
- `taskkill` needs `/F`: like a killed pod, the process gets no chance to shut down.
