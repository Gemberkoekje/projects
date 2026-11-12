# FrontPage — design

The front page of `gemberkoekje.nl`: who I am, the tools I've made (public and private), and a live
view of the gembernodes cluster.

_To understand this, read this file first, then [NOTES.md](NOTES.md) for what was built and what is
next. The page itself is `FrontPage.WebApp/wwwroot/index.html`; the cluster status starts at
`FrontPage.core/SnapshotBuilder.cs`._

## What the page is

Agreed with a clickable mock (four rounds, 2026-11-12). The decisions behind it:

- **Short, with tabs.** An introduction and a small live cluster box above three tabs: Tools,
  Cluster and About. Tabs have their own links (`#tools`, `#cluster`, `#about`). The Tools tab
  filters with chips (all, LARP and tabletop, games, other).
- **Candid about Claude Code, without plastering it everywhere.** It's named once in the
  introduction ("I come up with them, test them and steer them; Claude Code does the coding"),
  once in the About tab's "How I work", and in the footer. No credit line per card.
- **Plain wording.** No explanation of how the cluster data is fetched; the page just shows it.
- **Public tools, then a separate "Private" section** for the tools behind a sign-in (DungeonTable,
  Kluskat, Eos Bureau Investigations). Tools reachable only at home (Zeef, Grafana, SpaceTraders,
  File Browser) are not on the page at all.
- **GitHub links** wherever the code is public; tools in the projects monorepo link to their folder.
- **The Vortex tools are one card** with a text explanation and a link to the-vortex.nl, not the
  individual tools, which are for Vortex members only.
- **Ownership is stated honestly:** Bot on the Clocktower is marked as a fork of Mark Haines' bot;
  the COV card says I built the site but am not part of the association.
- **Look:** warm "ginger" palette, Fraunces for headings and Inter for text (both self-hosted, OFL,
  so no request leaves for Google Fonts), a dark terminal-style panel for the cluster. Light and
  dark follow the system. Works at phone width without sideways scrolling.

## The live cluster view

The browser asks `GET /api/cluster` on the same site. That answer comes from memory: a background
service in the app reads the cluster every 30 seconds and keeps the latest summary. A visitor never
causes a request to the Kubernetes API, however often they reload.

What the summary holds, and nothing more:

| Field | From |
|---|---|
| Per node: name, Ready, CPU and memory in use (% of allocatable), k3s version | Nodes; `metrics.k8s.io` node metrics (metrics-server ships with k3s) |
| Pods running | Pods with `status.phase=Running`, all namespaces |
| Apps running | Namespaces with a running pod, minus a configured list of system namespaces |
| Running version: the gembernodes commit and when Flux fetched it | The `flux-system` GitRepository's artifact |
| Per tool on the page: up or down | The Deployments and StatefulSets configured for it under `Apps` |

No IPs, image names, namespaces of private things, logs or events leave the app. A tool is "up" when
every workload configured for it has at least one ready replica.

**Failure:** if a read fails, the previous summary stays, with its own timestamp, so the page shows
how old it is. The page treats a summary older than three minutes, or none at all, as unavailable
and greys the live parts out. Metrics or the Flux revision failing alone only blanks those fields.

**Uptime is deliberately not shown:** the node's Ready condition doesn't change on a quick reboot,
so its timestamp is not an uptime, and the real boot time lives only in Prometheus.

## Access to the cluster

The app runs as its own ServiceAccount with one ClusterRole that can only `get`/`list` nodes, pods,
Deployments, StatefulSets and node metrics, and `get` the GitRepository named `flux-system`. Never
`edit` or `admin`: on gembernodes those let you create a Flux Kustomization that reconciles as
cluster-admin.

## Code layout

The usual four layers, kept small:

- `FrontPage.core`: the facts as read (`ClusterFacts`), the summary (`ClusterSnapshot`) and the pure
  function between them (`SnapshotBuilder`).
- `FrontPage.application`: the port `IClusterFactsReader` and `ClusterStatus`, which refreshes and
  holds the latest summary.
- `FrontPage.infrastructure`: `KubernetesClusterFactsReader`, the only class that touches the
  Kubernetes client.
- `FrontPage.WebApp`: the host. Static page in `wwwroot`, `/api/cluster`, `/healthz`, the refresher,
  and strict security headers (CSP `'self'` only, so no inline script or style).

## Hosting

- Image `ghcr.io/gemberkoekje/frontpage`, built by `.github/workflows/frontpage-build-and-push.yml`
  on every push to `main` that touches `FrontPage/`, tagged with the commit.
- gembernodes: namespace `frontpage`, two replicas, and an HTTPRoute for `gemberkoekje.nl` with
  `PathPrefix: /` on the shared `https-gemberkoekje-nl` listener. The apps that already answer on a
  path of the bare domain (`/adventureengine`, `/dnd`, `/plotboek`...) keep working: the Gateway
  API picks the longest matching path.
