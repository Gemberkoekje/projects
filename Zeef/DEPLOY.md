# Deploying Zeef

_To understand this, read [DESIGN.md](DESIGN.md) sections 5 and 6 first. Then read the gembernodes
manifests in `apps/zeef/` (start with its README.md) and `infrastructure/pvcs/zeef.yaml`.
[NOTES.md](NOTES.md) has what was decided and learned along the way._

Phase 0 runs the stack without the Zeef app: qBittorrent, Prowlarr, Sonarr, Radarr, Cleanuparr and
Recyclarr in namespace `zeef`. It is reachable only from the LAN and Tailscale. The web UIs are on
`192.168.1.234`, and the peer port is on `192.168.1.235`. Since phase 1 every UI also has a name,
`https://<app>.lan.gemberkoekje.nl` (DESIGN.md 6.3); the steps below use the IP:port addresses as
they were on the day.

This file is the runbook as it actually worked on 2026-11-06, with the corrections from that day
folded in. NOTES.md has the story behind each correction.

## Part A: before the gembernodes PR merges

Do these first. Otherwise the pods hang in `ContainerCreating` / `CreateContainerConfigError`, and
the cluster's "pod stuck" alerts start emailing after 15 minutes.

- **A1. The NFS export.** QNAP Control Panel → Privilege → Shared Folders → `Multimedia` → Edit
  Shared Folder Permission → NFS host access:
  - access on;
  - hosts `192.168.1.201`, `.202`, `.203`;
  - read/write;
  - squash root user only.

  Without it, the QNAP answers mounts with "No such file or directory".
- **A2. The databases.** Four logins and seven databases on the shared PostgreSQL. The SQL is in
  gembernodes `apps/zeef/README.md`, step 2.
  - That SQL uses psql's `\password`. **In DBeaver** (or any other GUI), use
    `ALTER ROLE <name> LOGIN PASSWORD '<hex>'` instead, because GUIs send `\password` to the server
    as SQL and fail.
  - Run it as a script, with auto-commit on: `CREATE DATABASE` can't run inside a transaction.
- **A3. The 1Password item `zeef-stack`** (vault Gembercluster). The exact field labels are in the
  same README, step 3: three API keys (`openssl rand -hex 16`) and four database passwords. Later,
  add `QBITTORRENT__PASSWORD`.
  - The operator retries a missing item with a growing back-off. To sync at once after saving it:
    `kubectl -n zeef annotate onepassworditem zeef-stack zeef/refresh="$(date +%s)" --overwrite`.
- **A4. Loose series.** Move series folders that sit outside `TV Shows/` into it, in Sonarr's layout
  (see B5). On 2026-11-06, Nichijou and Azumanga Daioh were moved by script; the renames are logged
  in `TV Shows/_rename-log-2026-11-06.csv`. Kodi needs a library update afterwards.

Then the PR merges, followed by:

```bash
flux reconcile kustomization flux-system --with-source
kubectl -n zeef get pods,svc,pvc
```

Every pod should be `1/1 Running`. The first start of Sonarr and Radarr takes over 5 minutes
(~250 database migrations), and the startup window allows 20. The PVC `multimedia` should be
`Bound` to `multimedia-nfs`. The Services should show `192.168.1.234`, and `qbittorrent-peers`
should show `192.168.1.235`.

## Part B: after the deploy

### B1. Ownership check (before any app touches the library)

Run the test as the app user (`s6-setuidgid abc`). A plain `kubectl exec` runs as root, and the
export squashes root to `guest`, so that would test the wrong user. From Git Bash, set
`MSYS_NO_PATHCONV=1` first, or it rewrites `/data` into a Windows path.

```bash
kubectl -n zeef exec deploy/qbittorrent -- ls -lnd "/data/Movies" "/data/TV Shows"
kubectl -n zeef exec deploy/qbittorrent -- s6-setuidgid abc sh -c 'mkdir -p /data/@Downloads/complete /data/@Downloads/incomplete && chmod 775 /data/@Downloads /data/@Downloads/complete /data/@Downloads/incomplete && touch /data/@Downloads/.probe && ln /data/@Downloads/.probe /data/Movies/.probe-link && stat -c "%h links %u:%g" /data/@Downloads/.probe && rm /data/Movies/.probe-link /data/@Downloads/.probe'
```

- The pods run as UID 1000, GID 100 (QNAP's `everyone`). They need write access to the existing
  show and film folders, because new episodes go into existing `Season NN/` folders.
- If `touch` or `ln` fails, change `PUID`/`PGID` in the manifests to match the owner that `ls -ln`
  shows, then redeploy.
- Then rename and delete a pod-written file from Windows over SMB.
- Creating `complete/` and `incomplete/` up front also stops Sonarr/Radarr's red "directory does
  not appear to exist" health error. qBittorrent only creates them on its first download.

Result on 2026-11-06: **passed.** The library was already owned by `1000:100`, because your QNAP
user is UID 1000.

### B2. Router

Forward **TCP and UDP 49160 → 192.168.1.235**, and nothing else. Downloads work without the forward,
just with fewer peers. UPnP can't do this from inside the cluster: its discovery never leaves the
pod network, and the router couldn't reach a pod address anyway.

### B3. qBittorrent (`http://192.168.1.234:8080`)

The first start prints a temporary password: `kubectl -n zeef logs deploy/qbittorrent | grep -i password`.
In Options (the gear icon):

- **Web UI:** your own password. Store it in `zeef-stack` as `QBITTORRENT__PASSWORD`. Leave
  "bypass authentication" off.
- **Downloads:**
  - Default Torrent Management Mode **Automatic**;
  - default save path `/data/@Downloads/complete`;
  - "Keep incomplete torrents in" `/data/@Downloads/incomplete`;
  - "Append .!qB extension to incomplete files" on.
  - **"Excluded file names" stays empty.** With Sonarr/Radarr it causes stuck downloads; Cleanuparr
    does this job instead.
- **Connection:** listening port `49160` (preset by the container); UPnP off (already off).
- **Speed:**
  - The line measured ~414 Mbit/s down and ~455 Mbit/s up from inside the pod on 2026-11-06.
  - Global limits: download `36000` and upload `30000` KiB/s (~300 and ~250 Mbit/s). That leaves
    room for the house, the cluster's public sites and the QNAP.
  - "Apply rate limit to transport overhead" on.
  - Optional: alternative limits `18000` / `6000` scheduled 18:00–23:00.
- **BitTorrent → Torrent Queueing:** raise the defaults. Out of the box: 3 downloads and **5 active
  torrents including seeding ones**. With a gap-fill running, seeding episodes fill those slots and
  new downloads wait for hours (on 2026-11-06, 49 torrents with only ~5 moving). Use maximum active
  downloads `8`, uploads `20` and torrents `30`, and keep "Do not count slow torrents" on.
- **BitTorrent:** seeding limits ratio `1.0` or `10080` minutes (7 days), then **Stop torrent**.
  Sonarr/Radarr remove stopped torrents and their staging copy; the hardlinked library copy stays.

Categories don't need to be made by hand. Sonarr and Radarr create `sonarr` and `radarr` (saved
under `complete/<category>`) on their first grab.

### B4. Prowlarr (`:9696`)

1. **Login.** On the first visit, the "Authentication Required" screen asks for a method. Choose
   **Forms (Login Page)** and create your username and password; Sonarr and Radarr ask the same.
   Leave "Allowed Hosts" and "Trusted Networks" empty. If you get a bare login page with no way to
   register, the method was preset through the environment (see NOTES.md, fixed in PR #117).
2. **Settings → Apps → +:** add **Sonarr** and **Radarr** with sync level Full Sync.
   - Prowlarr server `http://prowlarr.zeef.svc.cluster.local:9696`.
   - Sonarr server `http://sonarr.zeef.svc.cluster.local:8989`, Radarr server
     `http://radarr.zeef.svc.cluster.local:7878`.
   - Their API keys from `zeef-stack`.
3. **Settings → Apps → Sync Profiles:**
   - Standard: **Minimum Seeders 3**.
   - Add a second profile, **Search only**: Enable RSS off, Minimum Seeders `0`. It's for the
     Internet Archive, whose search is too slow for the 15-minute feed checks and whose torrents
     mostly download from its web servers (0 seeders).
4. **Indexers → Add Indexer.** This is the main Indexers page, not Settings → Indexers, which holds
   proxies and FlareSolverr; we use neither.
   - Click a result, then **Test**, then **Save**. Skip any indexer whose test mentions Cloudflare.
   - **Indexer Priority** only appears after clicking **Show Advanced** in the indexer's window.
     Several indexers can be set at once with Select Indexers → Edit.
   - **Internet Archive:** Sync Profile "Search only", priority 50.
   - **LinuxTracker:** priority 50. It has no TV or film categories, so it isn't synced to
     Sonarr/Radarr. That's correct; it's for the software path.
   - **Anime Tosho** (the public one, with a space): no API key needed. Tick "Improve Sonarr
     compatibility…" and "Remove first season keywords…".
   - Open "anyone can upload" sites get a worse priority (e.g. 40) than curated ones.

Prowlarr then pushes every indexer into Sonarr and Radarr, with "(Prowlarr)" after the name.

### B5. Recyclarr, before importing the library

```bash
kubectl -n zeef create job --from=cronjob/recyclarr recyclarr-first
kubectl -n zeef logs -f job/recyclarr-first
```

This creates the quality profiles **WEB-1080p** (Sonarr) and **HD Bluray + WEB** (Radarr) with the
TRaSH custom formats (37 and 41). Run it before B6, so the library import can use those profiles
right away. On "Any", the junk filter doesn't apply at all. The CronJob then re-syncs every night at
05:30.

### B6. Sonarr (`:8989`) and Radarr (`:7878`)

Both:

- **Login:** Forms, as in Prowlarr.
- **Download Clients → qBittorrent:**
  - host `qbittorrent.zeef.svc.cluster.local`, port `8080`;
  - user `admin` plus your password;
  - category `sonarr` / `radarr`;
  - **Remove Completed** and **Remove Failed** on.
- **Media Management** (Show Advanced):
  - Use Hardlinks instead of Copy on (the default).
  - **Import Extra Files on, with extensions `srt`.** Radarr has it off by default.
  - Unmonitor Deleted Episodes/Movies on.
  - **Minimum Free Space `25600`** MB, the brake that stops grabbing before the share is full.
  - **Recycling Bin** `/data/@Downloads/recycle/sonarr/` (`…/radarr/`), cleanup 7 days. Without it
    an upgrade deletes the old file permanently: NFS deletes bypass the QNAP's own recycle bin.
    Create the folders first, as `abc` with mode 775.
  - Leave permissions alone (UMASK handles them).

Sonarr:

- Root folder `/data/TV Shows`.
- Episode naming, matching the existing files:
  - Rename Episodes on;
  - Standard `{Series TitleYear} - S{season:00}E{episode:00}`;
  - Daily `{Series TitleYear} - {Air-Date}`;
  - Anime `{Series TitleYear} - S{season:00}E{episode:00}`;
  - Series folder `{Series TitleYear}`;
  - Season folder `Season {season:00}`;
  - Colon Replacement "Smart" or "Replace with Space Dash Space". They give the same result for
    "Title: Subtitle".
- Series → Library Import from `/data/TV Shows`:
  - Quality Profile **WEB-1080p**;
  - Series Type **Anime** for anime (Nichijou, Azumanga Daioh);
  - Monitor as you like ("All" fills the gaps; "Future" only follows);
  - "Start search for missing" off for the first import.

Radarr:

- Root folder `/data/Movies`.
- Movie naming:
  - Rename Movies on;
  - Standard and Folder `{Movie Title} ({Release Year})`.
- Movies → Library Import from `/data/Movies`, with profile **HD Bluray + WEB** and no search.

Library import never renames existing files. Only new downloads use these formats. If the import
was done with the wrong profile: Series (Movies) → Select → select all → Edit → Quality Profile.

### B7. Cleanuparr (`:11011`)

The menu calls the *arr apps **Media Managers**; the docs say "Arr".

- **First visit:** create a local account. Skip OIDC.
- **Download Clients:**
  - qBittorrent, host `http://qbittorrent.zeef.svc.cluster.local:8080`, `admin` plus your password;
  - "Download Directory Source and Target" empty, because every app sees the same paths.
- **Media Managers:**
  - Sonarr `http://sonarr.zeef.svc.cluster.local:8989` and Radarr
    `http://radarr.zeef.svc.cluster.local:7878`, with their keys.
  - Check the port numbers: a missing digit shows up only as "No route to host" in the log.
- **Malware Blocker:**
  - Enable.
  - **"Delete if any file is blocked" off** (the default). Blocked files are skipped, and a release
    is removed only when every file is blocked. With it on, harmless `.nfo`, ad `.txt` and cover
    `.png` files throw away good releases.
  - Ignore Private on.
  - Per app: Enable Blocklist, type **Blacklist**, path `https://cleanuparr.pages.dev/static/blacklist`.
- **Queue Cleaner:**
  - Enable.
  - Errored and Failed Import: Max Strikes 3.
  - Failed Import **Pattern Mode: Exclude**, with patterns `title mismatch`, `by ID` and
    `Remote Path Mapping`. Those failures need a manual fix, not deletion; everything else is
    removed and searched again.
  - A stalled-download rule, enabled, Max Strikes 3.
- **Seeker:** it searches for better copies of what you already have. Turn it off if you don't want
  existing files replaced.

### B8. Kodi

- **In Kodi:** Settings → Services → Control → "Allow remote control via HTTP", port `8080`, with a
  username and password.
- **In Sonarr and Radarr:** Settings → Connect → Kodi, with the Kodi device's IP, port `8080` and
  those credentials. Turn on "Update Library", and "Clean Library" too, which also drops moved
  paths.

Kodi's sources stay `Movies/` and `TV Shows/`.

**Superseded by Jellyfin (phase 0b, DESIGN.md 12.4).** Kodi now gets its library through the
Jellyfin for Kodi add-on. Sonarr and Radarr use Connect → **Emby / Jellyfin** instead (host
`jellyfin.zeef.svc.cluster.local`, port `8096`, "Update Library" on), with **Map Paths From `/data/`
To `/media/`**. Without that mapping, new series and films only appear at Jellyfin's 12-hourly
scan.

### B9. QNAP media apps

In Multimedia Console (content sources for QuMagie/Video Station), make sure
`Multimedia/@Downloads` is not indexed. Exclude it explicitly if needed.

### B10. Checks (phase 0 is done when all pass)

- [x] B1 passed: writes, hardlinks, and rename/delete over SMB (2026-11-06).
- [x] `http://192.168.1.234:8989` works at home.
- [x] On mobile data it does **not** answer without Tailscale, and does with Tailscale (tested with
      Jellyfin on the phone, 2026-11-06).
- [x] The QNAP apps don't show anything from `@Downloads`: Multimedia Console has no content source
      for the share any more (checked by the user, 2026-11-06). Jellyfin has replaced QuMagie for
      watching.
- [x] Everything comes back after a PostgreSQL outage and restart (2026-11-06, see NOTES.md).
- [ ] The first gap-fill episodes download, import as hardlinks, and update Kodi.
- [ ] **End to end, free content:** in Radarr, add *Night of the Living Dead* (1968, public domain)
      or *Big Buck Bunny* and search. It grabs (Internet Archive if available), downloads into
      `@Downloads/complete/radarr`, appears in `Movies/` as a hardlink, Kodi's library updates, and
      after 7 days or ratio 1.0 the staging copy is removed while the film stays.
- [ ] `Zeef/NOTES.md` gets the phase 0 wrap-up.
