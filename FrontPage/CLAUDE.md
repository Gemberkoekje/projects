# FrontPage — repo conventions

The front page of `gemberkoekje.nl`: a static page plus a read-only summary of the gembernodes
cluster. Read [DESIGN.md](DESIGN.md) first (the agreed design and its reasons), then
[NOTES.md](NOTES.md) for where the last phase stopped.

## Layout

`FrontPage.core` ← `.application` ← `.infrastructure` ← `.WebApp` (+ `FrontPage.test`), the same
layering as Kluskat, kept small. Only `KubernetesClusterFactsReader` touches the Kubernetes client.

## Rules

- **Warnings are errors** (projects/ house style). Tune a rule only in `.globalconfig`, with a
  `[Justification: …]`.
- **The page is static** (`FrontPage.WebApp/wwwroot`). The CSP is `'self'` only: no inline
  `<script>`, `<style>`, `style="…"` or `on…=` handlers, and nothing from another site (fonts are
  self-hosted). A test fails on inline script or style. Build DOM in `site.js` with
  `createElement`/`textContent`; setting `element.style.width` from script is fine.
- **Every status dot is configured, and every configured app has a dot.** A dot is
  `<span class="dot" data-app="<id>">`; its workloads are `Apps:<id>` in `appsettings.json` as
  `namespace/name`. A test compares the two.
- **Nothing private leaves the app.** `/api/cluster` returns `ClusterSnapshot` only: node names,
  readiness and usage, counts, the gembernodes commit, and up/down per configured page id. Add a
  field there only if it's safe on a public page.
- **Requests never reach the cluster.** `ClusterStatusRefresher` reads it every
  `Cluster:RefreshSeconds`; requests read `ClusterStatus.Current`.
- Copy on the page is plain and short (agreed in the mock rounds): Claude Code is named in the
  introduction, in About → How I work and in the footer, nowhere else.

## Working here

```bash
dotnet build FrontPage.slnx
```

```bash
dotnet test FrontPage.slnx
```

```bash
dotnet run --project FrontPage.WebApp
```

The last one serves http://localhost:8085 and reads the **live** cluster through your kubeconfig
(read-only calls only). Without a kubeconfig the app stops at start-up: that's a misconfiguration.

- Adding a tool to the page: a card in `index.html`, and if it runs on gembernodes a
  `data-app` dot plus its workloads under `Apps` in `appsettings.json`.
- Deploying: CI pushes `ghcr.io/gemberkoekje/frontpage:<commit>` on every merge to `main`; bump the
  tag in gembernodes `apps/frontpage/deployment.yaml`.
- Every phase ends with a written summary appended to `NOTES.md`.
