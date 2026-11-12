# FrontPage — phase notes

Newest entry last. Each phase ends with an entry: what was built, the decisions, the gotchas, the
file map, and where to start reading.

---

## Phase 1 — the page and the live cluster view (2026-11-12)

_To understand this, start by reading [DESIGN.md](DESIGN.md), then
`FrontPage.WebApp/wwwroot/index.html` (all the content), `FrontPage.core/SnapshotBuilder.cs` (what
the live view shows) and `FrontPage.WebApp/Program.cs` (how it's wired)._

### What was built

- The page, ported from the mock agreed over four rounds: introduction, live cluster box, and the
  Tools (with filters and a separate Private section), Cluster and About tabs. Light and dark,
  phone width without sideways scrolling, and readable without JavaScript (all panels show).
- `GET /api/cluster`: the summary of the cluster (`ClusterSnapshot`), refreshed every 30 seconds in
  the background. The page polls it every 30 seconds and greys the live parts out when it fails or
  is more than three minutes old.
- `KubernetesClusterFactsReader`: nodes, running pods, Deployments, StatefulSets, node metrics and
  the Flux GitRepository's revision. Metrics and the revision are optional.
- CI (`.github/workflows/frontpage-build-and-push.yml`) and a runtime-only Dockerfile. The
  gembernodes manifests are on gembernodes branch `claude/frontpage` (namespace, ServiceAccount and
  read-only ClusterRole, two replicas, PDB, the route on `gemberkoekje.nl/`).

### Decisions

- **Static page, not Razor.** All content is fixed, so `index.html` plus a small script is the least
  machinery; the CSP can then be `'self'` with no `unsafe-inline`.
- **Fonts self-hosted** (Fraunces 600, Inter 400/500/600, latin subsets, OFL, from Fontsource 5.3.0)
  so a visit sends nothing to Google. Monospace uses the system's.
- **Uptime left out:** the Ready condition's timestamp doesn't move on a quick reboot.
- **"Apps" = namespaces with a running pod, minus `Cluster:SystemNamespaces`,** so Zeef's twelve
  Deployments count as one app.
- **A tool is up when every configured workload has a ready replica;** a scaled-down or missing
  workload is down.
- **Analyzer tuning (`.globalconfig`):** SA1206 off (StyleCop 1.1.118 predates `required` and asks
  for `required public`); QW0018 off (System.TimeProvider is injected, so it's testable already).

### Gotchas

- **StyleCop 1.1.118 flags target-typed `new(...)` (SA1000).** Write `new T(...)`.
- **Qowaiv's QW0016 wants `required` init properties, not positional records,** and QW0021 wants
  two-digit hours, minutes and seconds in `new DateTimeOffset(...)`.
- **NUnit reuses one fixture instance for all tests in a class.** Fixtures with state are
  `[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]`.
- **The Kubernetes client asks for node metrics at `/apis/metrics.k8s.io/v1beta1/nodes/`** (trailing
  slash: a custom object with an empty name). The API server reads it as a list; the test stub
  trims the slash.
- **A stub `DelegatingHandler` must set `response.RequestMessage`,** or the client throws an
  `ArgumentNullException` for `httpRequest`.
- **`BuildConfigFromConfigFile()` ignores `$KUBECONFIG`;** `BuildDefaultConfig()` honours it, then
  `~/.kube/config`, then the in-cluster ServiceAccount.

### How it was tested

- 58 tests: the summary rules, the refresh (keeps the previous summary on failure), the
  configuration, the Kubernetes adapter against canned API answers (including each optional and
  required call failing), and the real host (security headers, no inline script or style, every
  `data-app` dot configured and vice versa, caching, `/api/cluster` as camelCase JSON, no cluster
  call per request).
- Locally against the live cluster, read-only through the kubeconfig: all 12 tools up, three nodes
  with metrics, the Flux commit. The page in headless Edge at 1366 px and 390 px, light and dark:
  no console errors or CSP violations, fonts loaded, no sideways scroll; with `/api/cluster`
  failing or ten minutes stale it shows "status unavailable" and grey dots.
- The image: published in Release, built, run as uid 1654 with a read-only root filesystem and no
  capabilities, reading the live cluster: about 77 MiB.

### Next

- Land it: merge this branch, take the image tag CI pushes, and put it in gembernodes
  `apps/frontpage/deployment.yaml` (branch `claude/frontpage`, placeholder `IMAGE_TAG`), then open
  that pull request. After the merge, the checks at the end of gembernodes' NOTES.md entry.
- Maybe later: `www.gemberkoekje.nl` (no listener or route yet).

### File map

- `FrontPage.core/`: `ClusterFacts.cs`, `ClusterSnapshot.cs`, `SnapshotBuilder.cs`, `GitVersion.cs`,
  `AppDefinition.cs`, `AppState.cs`.
- `FrontPage.application/`: `Ports/IClusterFactsReader.cs`, `ClusterStatus.cs`,
  `ClusterStatusSettings.cs`.
- `FrontPage.infrastructure/KubernetesClusterFactsReader.cs`.
- `FrontPage.WebApp/`: `Program.cs`, `ClusterSettings.cs`, `ClusterStatusRefresher.cs`,
  `SecurityHeaders.cs`, `ApiJson.cs`, `appsettings*.json`, `wwwroot/` (`index.html`,
  `css/site.css`, `js/site.js`, `favicon.svg`, `robots.txt`, `fonts/`).
- `FrontPage.test/`: one `*_specs.cs` per subject, helpers in `TestHelpers/`.
- Build: `FrontPage.slnx`, `Directory.Build.props`, `Directory.Packages.props`, `.globalconfig`,
  `NuGet.config`, `Dockerfile`; CI in `/.github/workflows/frontpage-build-and-push.yml`.
