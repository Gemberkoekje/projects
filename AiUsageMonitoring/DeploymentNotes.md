# Deployment Notes — AI Usage Monitor

## 1. Plan Compliance Summary

All 11 steps from Plan.md have been implemented:

| Step | Description | Status |
|------|-------------|--------|
| 1 | Project scaffold (.NET hosted service, DI, exits after one cycle) | ✅ Done |
| 2 | Marten event schema (UsageSnapshotRecorded, AlertFired, DailyReportSent, UsageSummaryProjection) | ✅ Done |
| 3 | CoPilot usage API client | ✅ Done — **but see bug below** |
| 4 | Claude usage API client | ~~Removed~~ — claude.ai subscription usage is not available via API |
| 5 | Burndown calculation (ideal / actual / projected / rolling average delta) | ✅ Done |
| 6 | Spike detection (delta > avg × 2.5, projection > budget × 1.1, deduplication) | ✅ Done |
| 7 | Daily report gate (query Marten, skip if already sent today) | ✅ Done |
| 8 | Email rendering (HTML alert email + daily burndown report, traffic-light status) | ✅ Done |
| 9 | Orchestrator / Worker.cs wiring everything together | ✅ Done |
| 10 | Dockerfile + docker-compose + env-var secrets | ✅ Done — **CI/CD and K8s manifest missing** |
| 11 | Unit tests (74 burndown + 35 spike detector tests) | ✅ Done |

---

## 2. Code Changes Needed

### Bug: CoPilot API endpoint is wrong

**File:** `src/AiUsageMonitor/Clients/CopilotUsageClient.cs` line 40  
**File:** `src/AiUsageMonitor/appsettings.json` line 7

The hardcoded fallback URL `https://api.github.com/copilot/usage/v1/organizations/self/consumption` does not exist. The real GitHub Copilot usage API requires the organization name in the path:

```
GET https://api.github.com/orgs/{org}/copilot/usage
```

**Fix required:**
1. Add `CoPilot:Organization` to `appsettings.json`
2. Update `CopilotUsageClient` to read the org name from config and build the URL as `https://api.github.com/orgs/{org}/copilot/usage`
3. Update `appsettings.json` endpoint to `https://api.github.com/orgs/{ORG_PLACEHOLDER}/copilot/usage` (or remove it and let the client build it)
4. Add `COPILOT_ORG` to docker-compose env vars

The response shape from this endpoint is an array of daily usage objects (not a single `data` wrapper), so the JSON parsing logic in `CopilotUsageClient.cs` lines 70-86 will also need updating to sum across the daily entries.

**Real response shape (abbreviated):**
```json
[
  {
    "date": "2024-03-01",
    "total_suggestions_count": 2345,
    "total_acceptances_count": 1234,
    "total_lines_suggested": 5678,
    "total_lines_accepted": 2345,
    "total_active_users": 12,
    "breakdown": [...]
  }
]
```
Note: The API returns suggestion/acceptance counts and active users — **not raw token counts or cost**. You will need to decide whether to track active users as the `SeatsUsed` metric and map suggestion counts to `TokensUsed`, or pivot to a different metric entirely. The GitHub Copilot API does not expose token consumption or billing cost directly.

---

### Remove: Claude client

Delete `src/AiUsageMonitor/Clients/IClaudeUsageClient.cs` and `ClaudeUsageClient.cs` entirely. Remove the Claude HTTP client registration from `Program.cs`, the Claude fetch/persist/burndown/spike calls from `Worker.cs`, the `Claude:*` config section from `appsettings.json`, and the `Claude` entry from `docker-compose.yml`. The daily report email in `EmailService.cs` currently takes two `BurndownReport` parameters (one per provider) — reduce it to one.

---

### Missing: .gitignore

There is no `.gitignore` in the repo root. At minimum it should exclude:
```
obj/
bin/
.vs/
*.user
```
This matters because `obj/` and `.vs/` are currently unguarded and will pollute commits.

---

## 3. GitHub Actions CI/CD

Create `.github/workflows/ci.yml`:

```yaml
name: CI

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

jobs:
  build-and-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Restore
        run: dotnet restore src/AiUsageMonitor/AiUsageMonitor.csproj

      - name: Build
        run: dotnet build src/AiUsageMonitor/AiUsageMonitor.csproj --no-restore --configuration Release

      - name: Test
        run: dotnet test tests/AiUsageMonitor.Tests/AiUsageMonitor.Tests.csproj --no-restore --configuration Release

  docker:
    runs-on: ubuntu-latest
    needs: build-and-test
    if: github.ref == 'refs/heads/main'
    steps:
      - uses: actions/checkout@v4

      - name: Log in to GitHub Container Registry
        uses: docker/login-action@v3
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

      - name: Build and push Docker image
        uses: docker/build-push-action@v5
        with:
          context: ./src/AiUsageMonitor
          push: true
          tags: |
            ghcr.io/${{ github.repository_owner }}/aiusagemonitor:latest
            ghcr.io/${{ github.repository_owner }}/aiusagemonitor:${{ github.sha }}
```

**GitHub repository settings needed:**
- Go to your repo → Settings → Actions → General → Workflow permissions → set to "Read and write permissions" (so `GITHUB_TOKEN` can push to GHCR)
- No additional secrets needed for GHCR — `GITHUB_TOKEN` is automatic

---

## 4. Hosting on Gembernodes

The app is a single-run process (exits after one cycle), making it a **Kubernetes CronJob**. The pattern below matches how other apps on Gembernodes are deployed (based on `cov-website/deployment.yaml`):
- Image from `ghcr.io/gemberkoekje/...`
- `ghcr-login` imagePullSecret for GHCR access
- SMTP values as plain env vars; only `SMTP_PASSWORD` from a K8s secret named `mail-secret`
- Namespace matches the app name

The env var name in the manifest is independent of the secret key name — you map the secret key to whatever name the app reads. The app uses `Email:From` / `Email:To` (mapping to `Email__From` / `Email__To` in .NET env var notation), so those are what the manifest sets. No code changes needed.

### Step 1 — Push your image

GitHub Actions (see section 3) builds and pushes to:
```
ghcr.io/gemberkoekje/aiusagemonitor:latest
```

### Step 2 — Create the `mail-secret` (reuse existing if already present)

The `mail-secret` secret may already exist in the cluster from other apps. If not, create it:

```bash
kubectl create secret generic mail-secret \
  --from-literal=SMTP_PASSWORD='your-gmail-app-password' \
  -n aiusagemonitor
```

> If the secret already exists in a shared namespace, reference it from there or copy it into the `aiusagemonitor` namespace.

### Step 3 — Create the app secrets (API tokens + DB)

```bash
kubectl create secret generic aiusagemonitor-secrets \
  --from-literal=COPILOT_API_TOKEN='ghp_...' \
  --from-literal=COPILOT_ORG='gemberkoekje' \
  --from-literal=MARTEN_CONNECTION='Host=yourdb;Port=5432;Database=aiusagemonitor;Username=user;Password=pass' \
  --from-literal=Email__To='you@yourdomain.com' \
  -n aiusagemonitor
```

### Step 4 — Create the CronJob manifest

Save as `k8s/cronjob.yml`:

```yaml
apiVersion: batch/v1
kind: CronJob
metadata:
  name: aiusagemonitor
  namespace: aiusagemonitor
spec:
  schedule: "0 * * * *"   # every hour — adjust as needed
  concurrencyPolicy: Forbid
  successfulJobsHistoryLimit: 3
  failedJobsHistoryLimit: 3
  jobTemplate:
    spec:
      template:
        spec:
          restartPolicy: OnFailure
          imagePullSecrets:
            - name: ghcr-login
          containers:
            - name: aiusagemonitor
              image: ghcr.io/gemberkoekje/aiusagemonitor:latest
              imagePullPolicy: Always
              securityContext:
                allowPrivilegeEscalation: false
                capabilities:
                  drop: ["ALL"]
              resources:
                requests:
                  cpu: 50m
                  memory: 64Mi
                limits:
                  cpu: 250m
                  memory: 256Mi
              envFrom:
                - secretRef:
                    name: aiusagemonitor-secrets
              env:
                - name: ASPNETCORE_ENVIRONMENT
                  value: Production
                - name: Smtp__Host
                  value: "smtp.gmail.com"
                - name: Smtp__Port
                  value: "587"
                - name: Smtp__User
                  value: "gemberkoekje@gmail.com"
                - name: Smtp__Password
                  valueFrom:
                    secretKeyRef:
                      name: mail-secret
                      key: SMTP_PASSWORD
                - name: Email__From
                  value: "gemberkoekje@gmail.com"
```

### Step 5 — Apply to cluster

```bash
kubectl create namespace aiusagemonitor   # if it doesn't exist yet
kubectl apply -f k8s/cronjob.yml
```

### Step 6 — Verify

```bash
# Manually trigger a job to test
kubectl create job --from=cronjob/aiusagemonitor aiusagemonitor-manual-test -n aiusagemonitor

# Watch logs
kubectl logs job/aiusagemonitor-manual-test -n aiusagemonitor --follow
```

### PostgreSQL

You need a PostgreSQL database accessible from within the cluster. Options:
- Deploy a PostgreSQL pod in the same namespace (e.g., using the Bitnami Helm chart: `helm install postgres bitnami/postgresql -n aiusagemonitor`)
- Use a managed database and put the connection string in `aiusagemonitor-secrets`

Marten will auto-create its schema tables on first run — no manual migration needed.

---

## 5. Secrets for 1Password

### Kubernetes secrets (sensitive — store in 1Password)

| Secret Name | K8s Secret | Where to Get It | Notes |
|-------------|-----------|-----------------|-------|
| `SMTP_PASSWORD` | `mail-secret` | [myaccount.google.com/apppasswords](https://myaccount.google.com/apppasswords) → create an App Password for "Mail" | Requires Gmail 2FA to be enabled first |
| `COPILOT_API_TOKEN` | `aiusagemonitor-secrets` | [github.com/settings/tokens](https://github.com/settings/tokens) → Generate new token (classic) → scope: `manage_billing:copilot` | Must be an org owner or billing admin |
| `MARTEN_CONNECTION` | `aiusagemonitor-secrets` | From your PostgreSQL provider dashboard | Format: `Host=...;Port=5432;Database=aiusagemonitor;Username=...;Password=...` |
| `Email__To` | `aiusagemonitor-secrets` | Your recipient email address | The address that receives daily reports and alerts |

### Plain env vars (not secrets — already in the manifest above)

| Env Var | Value |
|---------|-------|
| `Smtp__Host` | `smtp.gmail.com` |
| `Smtp__Port` | `587` |
| `Smtp__User` | `gemberkoekje@gmail.com` |
| `Email__From` | `gemberkoekje@gmail.com` |
| `COPILOT_ORG` | `gemberkoekje` (or your org name) |

### How to store in 1Password

1. Open 1Password → create a new item of type **"Secure Note"**
2. Name it `AiUsageMonitor — Production`
3. Add the four sensitive values from the table above as custom fields
4. When deploying, use the 1Password CLI (`op`) to inject secrets:

```bash
op run -- kubectl create secret generic mail-secret \
  --from-literal=SMTP_PASSWORD='{{op://AiUsageMonitor/SMTP_PASSWORD}}' \
  -n aiusagemonitor
```

Or use the [1Password Kubernetes Operator](https://developer.1password.com/docs/k8s/k8s-operator/) to sync secrets directly into your cluster as Kubernetes Secrets.
