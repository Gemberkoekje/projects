# Zeef notes

_To understand this, start by reading [DESIGN.md](DESIGN.md), then [DEPLOY.md](DEPLOY.md), then
gembernodes `apps/zeef/README.md` and `infrastructure/pvcs/zeef.yaml`._

## Phase 1: Seerr for films and series (done 2026-11-06)

**Summary.** Films and series are now requested in Seerr; Sonarr/Radarr fetch them and Jellyfin
plays them. Maintainerr clears seasons that have been watched. Every internal UI has a name with
a real certificate: `https://<app>.lan.gemberkoekje.nl`, LAN and Tailscale only.

- **Gembernodes PRs, all merged:**
  - #122: Seerr.
  - #123: Seerr's `wait-for-postgres` with an explicit user.
  - #124: the LAN names (Caddy switchboard `lan-proxy` on `192.168.1.236`), Grafana's `domain`.
  - #125: Maintainerr.
  - #126: port 443 answers so browsers fall back to http (superseded by #128).
  - #127: a Let's Encrypt certificate for `*.lan` via acme-dns.
  - #128: every name on https, http redirects to https.
- **Addresses** (the old `192.168.1.234:<port>` ones keep working):

  | Name | What |
  |---|---|
  | `https://lan.gemberkoekje.nl` | Start page with links to all of them |
  | `https://seerr.lan.gemberkoekje.nl` | Requests (daily use) |
  | `https://jellyfin.lan.gemberkoekje.nl` | Watching; also the server URL for Kodi and Findroid |
  | `https://sonarr.…`, `radarr.…`, `prowlarr.…`, `qbittorrent.…`, `cleanuparr.…`, `maintainerr.…` | Setup and troubleshooting |
  | `https://grafana.lan.gemberkoekje.nl` | Grafana (`/` goes to `/grafana/`) |

  The how and why of the names, the certificate and its renewal are in gembernodes `NOTES.md`
  (the three 2026-11-06 "LAN names" entries).
- **Checked at the close (2026-11-06 ~17:15):**
  - All 10 names answer 200 under strict TLS, and http gives 308 to https.
  - Seerr (`settings.json`, secrets filtered): display language `nl`, Application URL
    `https://seerr.lan.gemberkoekje.nl`, Jellyfin External URL
    `https://jellyfin.lan.gemberkoekje.nl`, "Enable Scan" on for Radarr and Sonarr.
  - qBittorrent's queue: 8 downloads, 20 uploads, 30 torrents.
  - Every pod in `zeef` and `lan-proxy` Running.
  - Sonarr's queue: 37 downloading, 4 queued, 1 stalled (Cleanuparr's job); 94 monitored episodes
    still missing from the gap-fill.
- **The end-to-end test passed (2026-11-06 ~17:40).** The public-domain test film, Night of the
  Living Dead (1968, Radarr id 5, from the Internet Archive):
  - grabbed 15:02, imported 17:24 as `Movies/Night of the Living Dead (1968)/… .m4v` (3.05 GB,
    WEBRip-1080p);
  - a hardlink: the library file and the seeding copy in `@Downloads/complete/radarr/` share one
    inode, so it is stored once;
  - in Jellyfin (h264 1432×1080) and "available" in Seerr, after the path-mapping fix below and a
    manual library scan.
  - South Park (season 29 only, so "partially available") and Dragon Maid showed up in Jellyfin and
    Seerr in the same scan.

### Details

- **Decided 2026-11-06:** Seerr (formerly Jellyseerr) instead of a home-built search box (DESIGN.md
  12.3).
- **Seerr:** `seerr/seerr:v3.5.1` on the shared PostgreSQL (database `seerr`, field
  `SEERR__DB_PASS`), `/app/config` on `qnap-nfs`. `/app/config` holds only `settings.json` and logs,
  with no SQLite `db/`, so the data is in Postgres.
- **The wizard:**
  - Jellyfin `jellyfin.zeef.svc.cluster.local:8096` with the libraries Films and Series;
  - Radarr with HD Bluray + WEB, `/data/Movies`, default;
  - Sonarr with WEB-1080p (anime the same), `/data/TV Shows`, season folders, default.
- **Single seasons:** in Seerr, request only the seasons you want (for example the newest South
  Park). Sonarr then monitors just those.
- **Maintainerr's first rule, "Binnenkort weg"** (gembernodes PR #125):
  - Series library, media type Seasons, Sonarr action "Seizoen ontvolgen en wissen", 7 days in
    the collection.
  - Rules: Jellyfin "[list] Users that watched every episode" *Bevat (Gedeeltelijke lijst-match)*
    `admin`, AND "Newest episode view date" *Vóór* 7 days.
  - So a season goes about 14 days after it was last watched. It goes to Sonarr's 7-day recycle
    bin, and Sonarr doesn't refetch it.
  - Only Jellyfin watch history counts.
  - "Achtergebleven mappen opschonen" stays off: Maintainerr doesn't mount the media share.
- **The Tailscale-from-mobile check passed (2026-11-06).** With Wi-Fi off on the phone, Jellyfin
  didn't load; with Tailscale on, it did. The same holds for every UI on `.234` and `.236`.
- **QNAP indexing:** Multimedia Console no longer has a content source for the Multimedia share.
  Nothing indexes `@Downloads`, and the single disk is spared the thumbnail work. Jellyfin replaced
  QuMagie.
- **Seeker: "Search" on, "Proactive search" off** (decided 2026-11-06, corrected the same day).
  Sonarr/Radarr upgrade only through their regular feed checks, and Cleanuparr still searches
  for a replacement after it removes a download. The user sets this in Cleanuparr's UI.
  - The first attempt switched off the whole Seeker ("Search", 15:18), which also stopped the
    replacement searches; see the gotcha below.

### Gotchas

- **`pg_isready` needs `-U postgres` in a pod with a `runAsUser`** (PR #123). UID 1000 has no
  `/etc/passwd` entry in `postgres:17-alpine`, so `pg_isready` can't find a user name and answers
  "no attempt" forever.
- **Plain http is impossible for any `*.gemberkoekje.nl` name.** The apex sends HSTS with
  `includeSubDomains`, so Chrome refuses http for every subdomain, with no click-through. Internal
  names need a real certificate.
- **Vimexx, the DNS host, has no API that cert-manager supports,** and its DNSSEC can't be turned
  off self-service, which blocked moving DNS to Cloudflare. The acme-dns CNAME
  (`_acme-challenge.lan`) works with DNSSEC on.
- **New DNS names can stay cached for a day.** `*.gemberkoekje.nl` had a 24-hour TTL, and Cloudflare
  (1.1.1.1, this PC's first resolver) kept the old public-IP answer for the `.lan` names.
- **Jellyfin locks an administrator after 5 failed logins** (other users after 3). After the move to
  the new name, the browser's saved password stays tied to the old `192.168.1.234:8096` address.
  Look it up in the password manager instead of guessing.
  - On 2026-11-06 four attempts failed: a wrong password, not the switchboard.
  - "Forgot password?" writes a one-time PIN to `/config/passwordreset<hash>.json` in the pod,
    valid for 30 minutes, not by email.
- **Sonarr/Radarr's Jellyfin connection needs Map Paths `/data/` → `/media/`** (set via the API
  on 2026-11-06).
  - The pods see the share as `/data`, Jellyfin as `/media`.
  - For an item Jellyfin already has, Sonarr/Radarr ask Jellyfin for its path, so updates landed
    (new For All Mankind episodes appeared within seconds).
  - For anything new they send their own `/data/…` path, which Jellyfin ignores. The test film,
    South Park and Dragon Maid then waited for Jellyfin's 12-hourly library scan, and Seerr (which
    learns from Jellyfin) didn't see them either.
- **Jellyfin sees the switchboard's pod IP (`10.42.x.x`) as the client.** Harmless: it falls in
  Jellyfin's LAN ranges. Adding the pod network to Jellyfin's Known Proxies would show real client
  IPs in its log; not done.
- **Sonarr runs only 3 tasks at once, and a big anime search can take all three.**
  - Anime is searched episode by episode, under every alternative title, across all indexers:
    about a minute per episode with 18 indexers.
  - On 2026-11-06 ~10 anime series were requested within 20 minutes. Dr. STONE (94 episodes),
    SPY x FAMILY (50) and Evil Lord (12) held all three slots for up to an hour.
  - Everything else waited behind them: the download check and imports (`RefreshMonitoredDownloads`),
    RSS, and the episode lists of newly added series (Clannad and Strawberry Marshmallow showed 0
    episodes).
  - Harmless, it clears itself; but Sonarr's queue page is stale meanwhile. Request big anime a few
    at a time.
- **Anime named "Title Part N - 01" lands in the wrong season.** Erai-raws restarts the numbering
  for each part ("Dr. Stone: Science Future Part 3 - 01").
  - Sonarr's season alias ("Dr. Stone: Science Future" = season 4) doesn't match with "Part 3"
    added, so it read "01" as absolute episode 1. Twelve season 4 episodes were grabbed as Dr.
    STONE S01E01–E13.
  - Fixed 2026-11-06:
    - Sonarr release profile "Geen 'Part N - 01'-namen" (all series) rejects
      `/\bPart\s?\d+\s*-?\s+\d{2,3}\b/i`. It was tested against the day's real names: `S01E11 …
      Part 1 1080p`-style titles still pass.
    - The 12 grabs were removed from the queue with blocklist, which also starts Sonarr's own
      redownload search.
    - Widened the same evening to `/\bPart\s?\d+\s*(?:-?\s+\d{2,3}\b|\[)/i`. An Erai-raws
      *batch*, "Dr Stone - Science Future Part 3 [1080P …][Unofficial Batch]", has no episode
      number after "Part 3". Sonarr took "Part 3" for episode 3 and grabbed it as S01E03. It was
      removed with blocklist as well.
- **Anime releases don't always carry English subtitles.**
  - ToonsHub's Amazon rips (Apocalypse Hotel S01E01/E11/E12, plus E03/E04/E07 queued) had no
    subtitle tracks at all; Erai-raws' copies of the same show had English and 12 more.
  - Also seen: RawsPlease (raws), and French-only VOSTFR or Tsundere-Raws releases (SPY x FAMILY
    E11, the CITY THE ANIMATION season pack).
  - Sonarr can't see subtitles before the download. Release profile 2, "Geen ToonsHub zonder
    ondertitels", rejects ToonsHub names that don't mention subs.
  - The real fix is Bazarr (gembernodes PR #129, DEPLOY.md part C): it fetches an English `.en.srt`
    for anything without English audio or subtitles.
  - The auto-mode safety check refused deleting the subtitle-less files without the user's
    explicit OK. With Bazarr they don't need replacing.
- **Space (2026-11-06 evening).**
  - The share has 682 GB used of 3.4 TB (21%): `TV Shows` 543 GB, `Movies` 39 GB.
  - Reclaimable:
    - `temp/` 49 GB, a Download Station leftover;
    - the QNAP's `@Recycle` 26 GB (Ted Lasso, deleted 2026-10-10);
    - Radarr's recycle bin 21 GB, emptied automatically within 7 days.
  - Everything requested so far was estimated at ~400–500 GB more, mostly anime backlog at ~1 GB
    per episode.

### Carried over

- **Phase 2** (the Zeef app for software & games, the import verifier, "Plak een link"): best
  started after the stack has run a week or so without help.

### File map (gembernodes)

| File | What |
|---|---|
| `apps/zeef/seerr.yaml` | Seerr on PostgreSQL, Service `.234:5055` |
| `apps/zeef/maintainerr.yaml` | Maintainerr (SQLite on `local-path`), Service `.234:6246` |
| `infrastructure/pvcs/zeef.yaml` | Plus `seerr-config` (`qnap-nfs`) and `maintainerr-data` (`local-path`) |
| `infrastructure/lan-proxy/Caddyfile`, `index.html` | The names and the start page |
| `infrastructure/lan-proxy/caddy.yaml` | Caddy, 2 replicas, LoadBalancer `.236:80/443` |
| `infrastructure/lan-proxy/certificate.yaml` | Certificate `lan-wildcard` (secret `lan-wildcard-tls`) |
| `infrastructure/lan-proxy/restart.yaml` | Weekly rolling restart, so Caddy picks up a renewed certificate |
| `configuration/certissuer-lan.yaml` | ClusterIssuer `letsencrypt-lan` (acme-dns) and OnePasswordItem `acme-dns-lan` |
| `namespaces/lan-proxy-namespace.yaml` | Namespace `lan-proxy` |
| `infrastructure/monitoring/grafana-release.yaml` | Grafana `domain` and `root_url` on the LAN name |
| `apps/zeef/jellyfin.yaml` | `JELLYFIN_PublishedServerUrl` on the LAN name |

## Phase 0: the Servarr stack on gembernodes (done 2026-11-06)

**Summary.** Download Station is replaced: films and series are found, filtered, downloaded and
imported automatically.
- **The stack:** qBittorrent, Prowlarr (9 indexers), Sonarr, Radarr, Cleanuparr and Recyclarr
  (TRaSH profiles), in namespace `zeef`, LAN/Tailscale only on `.234`.
- **The library:** adopted, with hardlinked imports.
- **Phase 0b:** Jellyfin serves it to Kodi and the phone.
- **Rules:**
  - Recycle bin of 7 days.
  - Audio-description and sign-language releases rejected.
  - Servarr log level info.
- **Gembernodes PRs, all merged:**
  - #115: the stack itself.
  - #116: Servarr startup window of 20 minutes.
  - #117: let the apps create their first user.
  - #118: PostgreSQL startup probe and a slower liveness probe, after the restart loop.
  - #119: Jellyfin.
  - #120: Servarr log level info.
  - #121: the TRaSH accessibility formats.
- **Runbook:** Part A and Part B of DEPLOY.md are done, with the corrections folded in. The checks
  left over are carried into phase 1 (above).
- **First results:**
  - 15+ For All Mankind episodes imported as hardlinks (seasons 3–5 in progress);
  - Kodi and Findroid play directly through Jellyfin.

Status at ~14:00, for the record:

- **Deployed and running.** The whole stack is up in namespace `zeef`.
- **Part A is done** (NFS export, databases via DBeaver, 1Password `zeef-stack`).
- **Part B is done:** B1–B7.
  - qBittorrent: paths, seeding limits, speed limits.
  - Prowlarr: 8 public indexers plus 1 private one, synced.
  - Recyclarr profiles.
  - Sonarr/Radarr: library adopted (11 series, 4 films), on the TRaSH profiles.
  - Cleanuparr, corrected.
- **The gap-fill is running:** Sonarr Search All for ~175 monitored missing episodes. The share was
  grown to 3.1 TB, with 2.8 TB free.

### Decisions

- **Answers to the last open points (2026-11-06):**
  - Kodi plays the files, so Sonarr/Radarr refresh Kodi's library after each import.
  - No preference on renaming existing films, so they keep their names (library import never
    renames).
  - `Games/` gets its own folder next to `Software/`.
- **Pinned versions:**

  | Image | Tag |
  |---|---|
  | qBittorrent | `lscr.io/linuxserver/qbittorrent:5.3.0_v2.1.2-ls486` |
  | Sonarr | `lscr.io/linuxserver/sonarr:4.0.20.3014-ls2` |
  | Radarr | `lscr.io/linuxserver/radarr:6.4.4.10685-ls325` |
  | Prowlarr | `lscr.io/linuxserver/prowlarr:2.7.0.5723-ls167` |
  | Cleanuparr | `ghcr.io/cleanuparr/cleanuparr:2.10.12` |
  | Recyclarr | `ghcr.io/recyclarr/recyclarr:8.8.1` |

  These are each image's `latest` on 2026-11-06. Sonarr 5 and Radarr 7 are still develop/nightly
  builds.
- **No SQLite on NFS anywhere.** Sonarr, Radarr and Prowlarr use `<APP>__POSTGRES__*`. Cleanuparr
  2.10.12 also supports PostgreSQL (`DATABASE_PROVIDER=postgres`, `POSTGRES_*`). All are in the
  shared PostgreSQL and covered by the nightly `pg_dumpall`.
- **API keys come from 1Password** (`<APP>__AUTH__APIKEY`), so Prowlarr, Recyclarr, Cleanuparr and
  Zeef share them without copying. The names are verified against the Servarr wiki
  (`sonarr/environment-variables.md`).
- **One LoadBalancer Service per app, sharing `192.168.1.234`** (`metallb.io/allow-shared-ip:
  zeef-lan`). This replaces the design's separate `lan-services.yaml`, because one Service then
  gives both the cluster DNS name and the LAN address.
  - `allocateLoadBalancerNodePorts: false` keeps the UIs off the nodes' own IPs.
  - The router forwards nothing to `.234`.
- **The peer port is `49160` on `192.168.1.235`**, with `externalTrafficPolicy: Local` so qBittorrent
  sees the peers' real addresses.
- **Removal after seeding:** qBittorrent stops a torrent at ratio 1.0 or 7 days, and Sonarr/Radarr
  ("Remove Completed") delete it together with its staging copy. This is TRaSH's recommended split,
  instead of qBittorrent deleting on its own.
- **Recyclarr profiles:**
  - WEB-1080p for Sonarr and HD Bluray + WEB for Radarr, copied from the guide's templates.
  - There is no separate anime profile. Recyclarr allows one `quality_definition` type per Sonarr
    instance, and Nichijou is complete anyway.
  - Language stays the original language. Dutch dubs for kids' films could come later through the
    Language Profiles group.
- **Naming is set in Sonarr/Radarr, not by Recyclarr.** Recyclarr's `media_naming` only takes the
  guide's preset keys. The library's own `Title (Year)` formats are in DEPLOY.md B5.
- **PUID 1000, PGID 100 (QNAP `everyone`), UMASK 002** for every pod that writes `/data`. DEPLOY.md B1
  checks this against the real owners before anything touches the library.
- **Speed limits come from a measurement, not a guess.** From inside the qBittorrent pod the line
  measured ~414 Mbit/s down (Hetzner test file, 4 streams) and ~455 Mbit/s up (Cloudflare).
  - Limits: 36000/30000 KiB/s.
  - Cloudflare's download endpoint answers 403 to non-browser clients; use a public test file.
- **Recyclarr runs before the library import.** The import then puts everything straight on the
  TRaSH profiles. On "Any" there is no junk filter.
- **"Fill the gaps" was chosen** for the series (monitor all). The 25 GB Minimum Free Space in
  Sonarr/Radarr is the brake against a full share.
- **Audio-description and sign-language releases are rejected** (gembernodes PR #121, 2026-11-06).
  TRaSH's [Optional] Accessibility group (WiTH AD/ASL/BASL/BSL) scores them −10000. With the
  profiles' minimum format score of 0, such a release is never grabbed. Files already in the
  library that have one are below cutoff, so Sonarr/Radarr replace them when a normal release
  appears.
- **Indexers are the user's choice.** Claude gave criteria and pushed none. The user picked 6
  public, 1 private and 2 legal ones (Internet Archive, LinuxTracker), and added Anime Tosho.

### Gotchas found so far

- **`enableServiceLinks: false` on every pod.** Otherwise Kubernetes injects `SONARR_PORT=tcp://…`
  style variables, which is clutter next to the apps' own `SONARR__*` settings.
- **The cluster's error-log alert only watches a fixed namespace list,** and `zeef` isn't on it on
  purpose: indexer errors are chatty. The Pending/CrashLoop alerts do cover `zeef`, so part A must
  be done before the merge.
- **No `fsGroup`, and the PV is `Retain` with Flux pruning disabled.** The comments in the manifests
  say why. Breaking either rule touches the whole library.
- **`kubeconform` isn't installed on this machine.** The manifests were checked with
  `kubectl kustomize`, a server-side `--dry-run=server --validate=strict` (namespaced objects
  re-homed to an existing namespace), a pod-security baseline dry-run, and `flux build`. The
  gembernodes CI runs the full `scripts/validate.py` on the PR.
- **Recyclarr 8 no longer publishes `latest`.** Pin a version (or a major tag such as `8`).
- **The PR was merged before part A was done.** The QNAP answers "No such file or directory" for an
  NFS path whose share has no NFS host access yet; the 1Password operator reports "no items found"
  for a missing item. The pods recovered by themselves once the export was applied: the kubelet
  retries the mount about every 2 minutes. Flux stayed Ready (no `wait`/health checks on `apps`), so
  only the pod-stuck alerts fire.
- **B1 passed (2026-11-06).**
  - The library was already owned by `1000:100`: the QNAP user is UID 1000 and `everyone` is GID
    100. PUID/PGID needed no change.
  - Writes into existing folders, hardlinks over NFS, and SMB rename/delete of pod-written files
    all work.
  - Test as `s6-setuidgid abc`: `kubectl exec` runs as root, which the export squashes to `guest`.
- **Don't preset `<APP>__AUTH__METHOD` through the environment.** The Servarr apps then skip their
  first-run "Authentication Required" screen, where the user is created, and show a login page
  nobody can pass. `config.xml` held no method and the Users table was empty.
  - Fixed in gembernodes PR #117: only `<APP>__AUTH__REQUIRED=Enabled` stays.
  - On the first visit, choose Forms and create the user.
- **The first start runs ~250 migrations and takes over 5 minutes** (PostgreSQL's data is on NFS).
  The startupProbe window is 20 minutes (PR #116).
- **Cleanuparr: keep "Delete if any file is blocked" off (its default).** Its blacklist also
  matches harmless junk that almost every public release carries: `.nfo` files, ad `.txt` files
  ("Downloaded from torrentgalaxy.to"), box-cover `.png` images.
  - With the option on, 6 good releases were thrown away and blocklisted within minutes on
    2026-11-06.
  - With it off, blocked files are skipped (never downloaded), so the ads are filtered out.
  - A release is only removed when *every* file is blocked, which is the classic malware-only
    fake.
- **A slow cluster volume makes Sonarr crawl.** On 2026-11-06, after the user resized the QNAP
  volume holding `gembernodes-pvcs`:
  - Synced writes there measured 457 kB/s (`dd … oflag=dsync`), against 37.5 MB/s on Multimedia.
  - Sonarr weighed ~1 release per second: an RSS sync took over 30 minutes, and the download
    queue lagged behind qBittorrent by 10+ minutes.
  - The quick check: the same `dd` on `/config` (cluster volume) and `/data/@Downloads`
    (Multimedia) in the qBittorrent pod, as `abc`.
  - **Not a background task.** The NAS has a single HDD. Multimedia's NFS export has "sync" off,
    and `gembernodes-pvcs` most likely has it on: correct for Postgres, slow for small writes.
  - **The real brake was logging.** Sonarr, Radarr and Prowlarr start at log level "debug" (about
    11 lines a second while weighing releases), all written to `/config` on that volume.
    `<APP>__LOG__LEVEL=info` fixes it (gembernodes PR #120).
  - **Don't untick "sync" on `gembernodes-pvcs`.** Postgres relies on it.
- **Cleanuparr's Seeker has two switches, and the main one also does replacement searches.**
  - "Proactive search" hunts for missing items and better copies of what you already have.
  - "Search" is the main switch. It also runs the replacement search after Cleanuparr removes a
    download, one queued item per run.
  - Cleanuparr removes queue items with `blocklist=true&skipRedownload=true` (2.10.12
    `ArrClient.cs`), so Sonarr/Radarr's own "Redownload Failed" never runs, and the replacement
    is left to Seeker.
  - With "Search" off (from 2026-11-06 15:18), 7 removals had gone unreplaced by ~18:45. South
    Park S29E01, E02 and E04 had been grabbed from Knaben as fakes: a single `.exe` named after a
    real release group (FLUX, NTb). The malware filter removed them correctly, then nothing
    searched again.
  - A manual Sonarr EpisodeSearch for the 6 still-missing episodes grabbed all 6. S29E01's pick
    ("CC WEB H264 KRAFT" on TorrentDownload, 733 seeders) was another lone `.exe`, removed within
    seconds. E01 was then grabbed by hand: a LimeTorrents release carrying the episode title
    ("South American Biker Gangs … AMZN WEB-DL … Kitsune", 0.92 GB).
- **Seeder counts don't prove a release is real.** The South Park fakes showed hundreds of
  seeders. The real copies of new episodes carried the episode title in the name and were
  ~0.8–0.9 GB; the fakes had only `S29E0x 1080p WEB H264-<group>`. The malware filter is what
  actually stops them, at metadata time, before any payload arrives.
- **Git Bash rewrites `/data/...` arguments to `kubectl exec`** into `C:/Program Files/Git/data/...`.
  Set `MSYS_NO_PATHCONV=1`.
- **Docker Hub's API through Python's `urllib` fails here** (an expired CA in the store). `curl`
  works. Use it to look up image tags.

### Phase 0b: Jellyfin (2026-11-06)

- Deployed by gembernodes PR #119 (DESIGN.md section 12). The wizard is done (server
  "jellyfin-Gembernodes").
- Libraries: "Films" → `/media/Movies` and "Series" → `/media/TV Shows`, with real-time monitoring
  off.
- Sonarr and Radarr → Connect → Emby / Jellyfin, Update Library on download and on upgrade.
- `JELLYFIN__APIKEY` is in `zeef-stack`. A manual run of the backup CronJob
  (`kubectl -n zeef create job --from=cronjob/jellyfin-backup …`) wrote
  `jellyfin-backup-20261106144854.zip` (780 KB) to the QNAP volume.
- The node disk holding `/config` (local-path) was at 46% (25 of 57 GB).
- **Gotcha:** Jellyfin's backup path is fixed at `<data>/backups`, and it has no scheduled backup of
  its own, hence the mount at `/config/data/backups` plus the CronJob.
- Clients verified from Jellyfin's log at ~14:40:
  - Findroid 1.2.0 (Android) and the Jellyfin for Kodi add-on 2.2.0 each played an episode.
  - No ffmpeg transcode was started (the only ffmpeg lines are startup detection) and
    `/cache/transcodes` stayed empty, so both played directly.
- Still the user's: the Tailscale-from-mobile check.

### Library tidy-up (2026-11-06)

`Nichijou/` and `azumanga daioh/` were moved from the share's root into `TV Shows/`, in Sonarr's
layout. The 64 renames are logged in `TV Shows/_rename-log-2026-11-06.csv`, in the same format as the
earlier 2026-10-10 log.

- **Names:** taken from Sonarr's own lookup, which is TVDB: "Nichijou: My Ordinary Life" (2011,
  tvdb 240291) and "Azumanga Daioh" (2002, tvdb 79077). Both have 26 episodes in season 1,
  numbered the same as the files' 01–26.
- **Azumanga DVD extras:**
  - "The Very Short Movie" became `Specials/… S00E01`.
  - Production Sketches 1–6 became `Specials/… S00E03–E08` (TVDB's "Character Sketches 1–6").
  - The opening/closing sequences and the art gallery have no TVDB entry, so they went to
    `extras/`, a folder Sonarr skips.
  - The release `.nfo` (info text, not Kodi metadata) stays in the show folder.
- **Library import:** pick Series Type **Anime** for both.

### File map (gembernodes, branch `claude/zeef-phase0`)

| File | What |
|---|---|
| `namespaces/zeef-namespace.yaml` | Namespace `zeef` (pod-security baseline) |
| `infrastructure/pvcs/zeef.yaml` | Static PV `multimedia-nfs` (Retain, no prune) and PVC `multimedia`; config PVCs on `qnap-nfs` |
| `apps/zeef/qbittorrent.yaml` | Deployment, WebUI Service (`.234:8080`), peer Service (`.235:49160`) |
| `apps/zeef/sonarr.yaml`, `radarr.yaml`, `prowlarr.yaml` | Deployments on PostgreSQL, Services on `.234` |
| `apps/zeef/cleanuparr.yaml` | Deployment on PostgreSQL, Service `.234:11011` |
| `apps/zeef/recyclarr.yaml` | ConfigMap with `recyclarr.yml` and a nightly CronJob |
| `apps/zeef/zeef-stack-secrets.yaml` | `OnePasswordItem` `zeef-stack` |
| `apps/zeef/README.md` | Reachability, rules, and part A's SQL and 1Password fields |
| `apps/zeef/kustomization.yaml` | Plus entries in `apps/`, `namespaces/` and `infrastructure/pvcs/` kustomizations |
