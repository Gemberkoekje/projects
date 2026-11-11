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

Held at 5.2.4 since 2026-11-11, because MyAnonamouse (MAM, D3) doesn't allow 5.3.0 yet. 5.3.0 keeps
the WebUI settings in another place in its config, so the move back reset the login and the API key
(gembernodes `NOTES.md`, 2026-11-11).

The first start prints a temporary password: `kubectl -n zeef logs deploy/qbittorrent | grep -i password`.
In Options (the gear icon):

- **Web UI:** your own password. Store it in `zeef-stack` as `QBITTORRENT__PASSWORD`. Leave
  "bypass authentication" off. Then make an **API key** on the same tab and Save. Sonarr, Radarr,
  Chaptarr and Cleanuparr connect with that key (B6, B7, D3), not with the password.
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
- **BitTorrent:** seeding limits ratio `2.0` or `10080` minutes (7 days), then **Stop torrent**
  (ratio `1.0` until 2026-11-11). Sonarr/Radarr remove stopped torrents and their staging copy; the
  hardlinked library copy stays.
- **Advanced → Disk IO type:** "Simple pread/pwrite" while qBittorrent is on 5.2.4 (libtorrent
  2.0). There, "Default" maps the files into memory, the mode known for high memory use and trouble
  on network storage such as the QNAP share. Back on 5.3.x (libtorrent 2.1), "Default" is pread.

Categories don't need to be made by hand. Sonarr, Radarr and Chaptarr create `sonarr`, `radarr` and
`chaptarr` (saved under `complete/<category>`) on their first grab.

**Category `chaptarr` without a ratio limit.** MAM wants each torrent seeded 72 hours within 30
days, and a popular book can reach the ratio limit sooner. Without it, the category's torrents stop
after the 7 days, or after the seed time that Chaptarr's MAM indexer sets. 5.2.4's WebUI can't edit
a category's share limits (5.3.x's can, under Edit category), so write it into the file, then
restart:

```bash
kubectl -n zeef exec deploy/qbittorrent -- s6-setuidgid abc cp /config/qBittorrent/categories.json /config/qBittorrent/categories.json.bak
kubectl -n zeef exec deploy/qbittorrent -- s6-setuidgid abc python3 -c 'import json; p="/config/qBittorrent/categories.json"; d=json.load(open(p)); d["chaptarr"]["ratio_limit"]=-1; json.dump(d, open(p, "w"), indent=4)'
kubectl -n zeef scale deploy/qbittorrent --replicas=0
kubectl -n zeef wait --for=delete pod -l app=qbittorrent --timeout=120s
kubectl -n zeef scale deploy/qbittorrent --replicas=1
```

qBittorrent only rewrites that file when a category changes, so the edit survives the restart.
Restart qBittorrent this way, not with `kubectl rollout restart`: Flux removes the restart
annotation again, and qBittorrent restarts a second time.

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
  - qBittorrent's API key (B3) instead of a user and password;
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
  - qBittorrent, host `http://qbittorrent.zeef.svc.cluster.local:8080`, with qBittorrent's API key
    (B3) instead of a user and password;
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
  - **Ignore Private on.** Private trackers such as MAM want the whole torrent seeded, and a
    skipped file leaves qBittorrent seeding only part of it.
  - Per app: Enable Blocklist, type **Blacklist**, path `https://cleanuparr.pages.dev/static/blacklist`.
- **Queue Cleaner:**
  - Enable.
  - Errored and Failed Import: Max Strikes 3.
  - Failed Import **Pattern Mode: Exclude**, with patterns `title mismatch`, `by ID` and
    `Remote Path Mapping`. Those failures need a manual fix, not deletion; everything else is
    removed and searched again.
  - A stalled-download rule, enabled, Max Strikes 3, **Privacy Type Public**.
  - Failed Import: **Ignore Private** on.
  - Private downloads get a look by hand instead: MAM counts every torrent not yet seeded 72 hours
    against a limit (D3).
- **Seeker:** keep **"Search" on** and turn **"Proactive search" off**. "Proactive search" hunts for
  better copies of what you already have. "Search" is the main switch and also runs the
  replacement search after Cleanuparr removes a download. Cleanuparr tells Sonarr/Radarr to skip
  their own redownload, so with "Search" off a removed fake is never replaced.

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
- [x] The first gap-fill episodes download, import as hardlinks, and update the library
      (through Jellyfin, which replaced the direct Kodi connection; 2026-11-06).
- [x] **End to end, free content:** in Radarr, add *Night of the Living Dead* (1968, public domain)
      or *Big Buck Bunny* and search. It grabs (Internet Archive if available), downloads into
      `@Downloads/complete/radarr`, appears in `Movies/` as a hardlink, and the library updates.
      Passed 2026-11-06 with *Night of the Living Dead*, shown in Jellyfin and Seerr; the film was
      deleted again afterwards.
- [x] `Zeef/NOTES.md` gets the phase 0 wrap-up.

## Part C: Bazarr, English subtitles (added 2026-11-06)

Bazarr fetches English subtitles for anything without English audio or English subtitles, as
`.en.srt` next to the video. Manifest: gembernodes `apps/zeef/bazarr.yaml` (PR #129).

### C1. Before the merge

- **Database**, in DBeaver on the shared PostgreSQL (`192.168.1.232`, user `postgres`), with
  auto-commit on:

  ```sql
  CREATE ROLE bazarr LOGIN PASSWORD '<password>';
  CREATE DATABASE bazarr OWNER bazarr;
  REVOKE CONNECT, TEMPORARY ON DATABASE bazarr FROM PUBLIC;
  ```

  Make the password with `openssl rand -hex 24`, or 1Password's generator with letters and digits
  only (a quote in it would break the SQL).
- **1Password:** add the field `BAZARR__POSTGRES__PASSWORD` (exact label) to `zeef-stack`, with
  the same password.
- **OpenSubtitles.com:** create a free account. The free tier allows a limited number of
  downloads a day, which is plenty once the backlog is done.

### C2. First visit (`https://bazarr.lan.gemberkoekje.nl`)

- **Settings → General → Security:** Authentication **Forms**, with a username and password
  (1Password). Bazarr starts without a login.

C3 to C6 were done on 2026-11-06 through Bazarr's own API, from inside the pod (`POST
/api/system/settings`, `X-API-KEY` from `/config/config/config.yaml`). The Sonarr/Radarr keys were
piped from their pods, never printed. Only the login and the OpenSubtitles account are the user's.

### C3. Sonarr and Radarr

- **Settings → Sonarr:** address `sonarr.zeef.svc.cluster.local`, port `8989`, the Sonarr API key
  from `zeef-stack`.
- **Settings → Radarr:** `radarr.zeef.svc.cluster.local`, `7878`, the Radarr key.
- No path mappings: Bazarr sees the library at `/data`, like Sonarr and Radarr.

### C4. Languages

- **Settings → Languages:** language filter **English**.
- A languages profile **English** with English only, set as the default for series and for films.
- **"Exclude if matching audio" on** for English (`audio_exclude`). An episode or film with an
  English audio track needs no subtitles, which keeps the providers' daily quotas for the
  non-English ones.
- **Treat embedded subtitles as present:** on (the default), so a video with an English subtitle
  track isn't given a second one. A track flagged *forced* (signs only) doesn't count: Dragon Ball
  DAIMA's files have only a forced English track, so Bazarr fetches a full one.
- **Undefined-language embedded subtitles count as English** (`default_und_embedded_subtitles_lang
  = en`).
  - Nichijou's fansub files have a subtitle track with no language set (Jellyfin shows "?").
  - Without this setting Bazarr fetched 7 needless English subtitles for them, out of the
    OpenSubtitles daily quota.

### C5. Providers

- **OpenSubtitles.com**, with the account from C1 (entered by the user).
- **TsukiHime**, for anime. It serves the subtitle tracks of fansub releases and needs no account;
  it finds a show through the public Anime-Lists mapping.
- **Podnapisi**, general, no account.
- **Subdl**, with the user's free API key, AI translation off (added 2026-11-06).
- **AnimeTosho.xyz** (provider `animetosho_xyz`), for anime: the subtitle tracks of every release
  Anime Tosho mirrors, matched to the exact episode through AniDB.
  - It needs **AniDB** (Settings → Providers → AniDB): a free AniDB account, then a registered
    project at https://anidb.net/software/add, with an HTTP API client (name in lowercase letters,
    version 1).
  - Not the older **"Anime Tosho"** provider (`animetosho`). Its `feed.animetosho.org` no longer
    exists in DNS. Anime Tosho lives at animetosho.net now, and `feed.animetosho.xyz` redirects
    there.
- Not used: the **Embedded Subtitles** provider. It only copies tracks that are already in the
  video out to `.srt`, which Jellyfin doesn't need.
- **Minimum score for series: 65** (the default is 90; films stay at 70).
  - Anime subtitles are almost never made for the exact release on disk. Anime Tosho's
    right-episode matches score 68–74; OpenSubtitles' one for Apocalypse Hotel S01E12 (the Judas
    release) scored 86.
  - Audio sync fixes the timing. Apocalypse Hotel E01 and E12 were synced with offsets of only
    0.05–0.07 seconds.

### C6. Subtitles: sync and Jellyfin

- **Settings → Subtitles → Audio synchronization:** on. Bazarr lines a downloaded subtitle up with
  the audio (ffsubsync), so one made for a different release of the same episode still fits.
- **Post-processing:** on, with the command `/scripts/jellyfin-notify.sh "{{episode}}"`.
  - The script comes with the manifest (ConfigMap `bazarr-scripts`).
  - It tells Jellyfin about the new subtitle at once, instead of at Jellyfin's 12-hourly scan.
  - It needs `JELLYFIN__APIKEY` in `zeef-stack`, which is already there.

### C7. Checks

- [x] Bazarr runs on PostgreSQL: 17 tables in `bazarr`, no SQLite file (2026-11-06).
- [x] The first sync found 49 episodes without English subtitles, all non-English audio.
- [x] The first search (2026-11-06 ~20:57) downloaded 20 subtitles from OpenSubtitles (13 Dragon
      Ball DAIMA, 7 Nichijou) and synced each to the audio.
- [x] The post-processing script got a 204 from Jellyfin each time, and Jellyfin listed the new
      `eng (external)` tracks straight away.
- [ ] Then OpenSubtitles' free tier stopped: "Daily download limit reached", throttled for 6 hours.
      10 were left (Apocalypse Hotel 3, Dragon Ball DAIMA 6, an Azumanga extra without dialogue).
      Bazarr's own 6-hourly search picks them up after the reset.
- [x] Apocalypse Hotel S01E01 (a ToonsHub file without subtitle tracks) gets an `.en.srt`, and
      Jellyfin offers English subtitles for it (2026-11-06, from AnimeTosho.xyz). All six of its
      episodes on disk now have English subtitles; the remaining five DAIMA episodes wait for
      OpenSubtitles' reset.

The free OpenSubtitles tier (about 20 downloads a day) is the bottleneck for a backlog; new
episodes trickle in well below it. VIP, or a second provider with an API key (Subdl), would raise it.

## Part D: Audiobooks: Audiobookshelf, AudioBookRequest and Chaptarr (added 2026-11-08, Chaptarr 2026-11-09)

The same chain as for series: **AudioBookRequest** (ABR, requests, like Seerr) → **Chaptarr** (search,
grab, import, like Sonarr) → **qBittorrent** → `Audiobooks/<Author>/<Title>/` → **Audiobookshelf**
(ABS, playback, like Jellyfin).
- Prowlarr feeds Chaptarr its indexers, except MyAnonamouse (MAM), which Chaptarr has built in (D3).
- Cleanuparr checks Chaptarr's downloads, with an audiobook list of its own.

Manifests: gembernodes `apps/zeef/chaptarr.yaml`, `audiobookrequest.yaml`, `audiobookshelf.yaml` and
`cleanuparr.yaml` (PR #143). The why and the local tests are in gembernodes `NOTES.md` (2026-11-09).

**If you followed the first version of this part** (merged 2026-11-09, before Chaptarr): remove
qBittorrent's `audiobooks` category and Prowlarr's own qBittorrent download client. Neither is used
any more. Also turn Audiobookshelf's watcher back **on** (D6).

### D1. Before the merge

- **Databases**, in DBeaver on the shared PostgreSQL (`192.168.1.232`, user `postgres`, database
  `postgres`), with auto-commit on. Run it as a script (Alt+X):

  ```sql
  -- AudioBookRequest. Hex password only: it goes into a connection URL as-is.
  CREATE ROLE audiobookrequest LOGIN PASSWORD '<hex-password-1>';
  CREATE DATABASE audiobookrequest OWNER audiobookrequest;
  REVOKE CONNECT, TEMPORARY ON DATABASE audiobookrequest FROM PUBLIC;

  -- Chaptarr: three databases, which it doesn't create itself.
  CREATE ROLE chaptarr LOGIN PASSWORD '<hex-password-2>';
  CREATE DATABASE "chaptarr-main" OWNER chaptarr;
  CREATE DATABASE "chaptarr-log" OWNER chaptarr;
  CREATE DATABASE "chaptarr-cache" OWNER chaptarr;
  REVOKE CONNECT, TEMPORARY ON DATABASE "chaptarr-main", "chaptarr-log", "chaptarr-cache" FROM PUBLIC;
  ```

  Leave out the first block if `audiobookrequest` already exists. Make each password with
  `openssl rand -hex 24`, or in DBeaver with
  `SELECT replace(gen_random_uuid()::text || gen_random_uuid()::text, '-', '');`.
- **1Password**, item `zeef-stack`, exact labels:
  - `AUDIOBOOKREQUEST__POSTGRES__PASSWORD`: password 1;
  - `CHAPTARR__POSTGRES__PASSWORD`: password 2;
  - `CHAPTARR__AUTH__APIKEY`: `openssl rand -hex 16`.

  A missing field keeps that pod in `CreateContainerConfigError`.

### D2. The library folder

Made by qBittorrent's user, so Chaptarr (same user) can write in it and ABS can read it:

```bash
kubectl -n zeef exec deploy/qbittorrent -- s6-setuidgid abc sh -c 'mkdir -p /data/Audiobooks && chmod 775 /data/Audiobooks'
```

### D3. Chaptarr, first visit (`https://chaptarr.lan.gemberkoekje.nl`)

- **Login:** the "Authentication Required" screen. Choose **Forms** and create the user
  (1Password), as in Sonarr.
- **Settings → Media Management:**
  - **Add Root Folder:** path `/data/Audiobooks`, type Audiobook, Audiobook Quality Profile
    "Audiobook", Audiobook Metadata Profile "Audiobook Default", and use it as the default
    audiobook root folder.
  - "Use Hardlinks instead of Copy" on (the default).
  - **Recycling Bin** `/data/@Downloads/recycle/chaptarr/`, cleanup 7 days, as for Sonarr.
- **Settings → Metadata:** leave "Tag Audio Files with Metadata" **off** (the default). Imports are
  hardlinks of the files qBittorrent is seeding, and writing tags would change those too.
- **Settings → Download Clients → qBittorrent:**
  - host `qbittorrent.zeef.svc.cluster.local`, port `8080`;
  - qBittorrent's API key (B3). User and password stay empty: Chaptarr refuses them next to a key;
  - Audiobook Category `chaptarr` (the default);
  - **Remove Completed** and **Remove Failed** on.
- **Settings → Indexers → + → MyAnonaMouse** (MAM, a private tracker that needs an account). Use
  Chaptarr's own MAM indexer, not Prowlarr's (D4). It knows MAM's rules: it skips VIP-only torrents
  unless the account is VIP, and it stops MAM grabs before the limit on torrents not yet seeded 72
  hours. Going over that limit means a 24-hour download suspension.
  - `mam_id` from MAM → Preferences → Security. It's tied to the home IP: if the ISP changes that,
    searches fail until you make a new one.
  - Test. It detects the user class and VIP status.
  - "Protect Unsatisfied Slots" on (the default), Safety Reserve `5`.
  - Seed Time at least `72` hours (set to 256). The force-started torrents below ignore it; it
    only counts for a MAM torrent that isn't force-started.
  - Priority (Show Advanced) `10`, below the public indexers' 25. Chaptarr ranks title match,
    category, format, profile and protocol first, then indexer priority, then size and seeders.
    Without it, a busy public torrent wins over the same book on MAM.
- **A second qBittorrent client, for MAM.** A torrent waiting in qBittorrent's queue doesn't
  announce, so it earns no seeding time on MAM. Settings → Download Clients → + → qBittorrent:
  - name "qbittorrent MAM", the same host and API key, Audiobook Category `chaptarr mam`;
  - **Initial State: Force Start**, Remove Completed and Remove Failed on;
  - then, in the MyAnonaMouse indexer: Download Client (Show Advanced) "qbittorrent MAM".

  Forced torrents skip the queue, and qBittorrent applies no seeding limits to them: they seed
  until you stop them, and Chaptarr never removes them. The library copy is a hardlink, so that
  costs no extra space. Stop old ones by hand (category `chaptarr mam`) once MAM counts them as
  seeded, or leave them running for MAM's bonus points.

  **A MAM torrent added to qBittorrent by hand** gets no Force Start. Give it category
  `chaptarr mam` (Chaptarr then imports it, as it did *Elantris*), then right-click → Force Start.
  Otherwise the global limits apply (ratio 2.0 or 7 days), and it can wait in the queue.
- **Searching again:** Chaptarr has no scheduled search for missing books, and RSS only sees new
  uploads. After adding an indexer, or to retry: Wanted → Missing → **Search All**, or tick books
  and Search Selected. Interactive Search (the person icon) lists every release, with the reason
  for any rejection.
- **Settings → Connect → AudioBookShelf** (after D6, which makes the API key):
  - host `audiobookshelf.zeef.svc.cluster.local`, port `13378`, API key "chaptarr" from D6;
  - triggers On Release Import, On Upgrade, On Rename and On Book File Delete;
  - in the library mapping, map root folder `/data/Audiobooks` to library "Luisterboeken" (folder
    `/media/Audiobooks`). Chaptarr translates the paths, so the different mount points are fine.

### D4. Prowlarr: Chaptarr as a "Readarr" app (`https://prowlarr.lan.gemberkoekje.nl`)

Prowlarr has no Chaptarr app type. Chaptarr speaks Readarr's API, so it's added as Readarr.

**Settings → Apps → + → Readarr:**
- name "Chaptarr", sync level **Full Sync**;
- Prowlarr server `http://prowlarr.zeef.svc.cluster.local:9696`;
- Readarr server `http://chaptarr.zeef.svc.cluster.local:8789`;
- API key `CHAPTARR__AUTH__APIKEY`;
- sync categories as given (Audio/Audiobook 3030 and the Books ones).

Test, Save. The indexers then appear in Chaptarr with "(Prowlarr)" after the name.

**Don't add MyAnonamouse in Prowlarr;** Chaptarr has its own (D3). Prowlarr's copy offered VIP-only
torrents, which MAM refuses ("Download blocked: VIP torrent and you are not VIP or higher"), and
after such a failure Prowlarr pauses the indexer for a while.

### D5. Cleanuparr (`https://cleanuparr.lan.gemberkoekje.nl`, 2.11.0 after the merge)

- **Media Managers → Chaptarr:** `http://chaptarr.zeef.svc.cluster.local:8789`, key
  `CHAPTARR__AUTH__APIKEY`. Test.
- **Malware Blocker → Chaptarr:** Enable Blocklist, type **Whitelist**, path
  `/blocklists/audiobooks.txt`.
  - **Not** the shared blacklist that Sonarr and Radarr use. That one blocks `*.mp3`, `*.m4a`,
    `*.flac`, covers and `.cue`, so it would throw away most audiobooks.
  - The whitelist comes with the manifest (ConfigMap `cleanuparr-blocklists`). It holds audio,
    cover and companion-file extensions; any other file (`.exe`, `.lnk`, archives, scripts) is
    skipped.
  - A release with nothing on the list is removed and blocklisted in Chaptarr, which then
    searches again.
  - To add an extension, edit that ConfigMap in gembernodes. Cleanuparr picks the change up
    within about 5 minutes.
- The Malware Blocker leaves private torrents (MAM) alone (Ignore Private, B7), so they keep every
  file.
- **Queue Cleaner:** nothing to change. It covers every Media Manager, for public torrents only
  (B7).

### D6. Audiobookshelf, first visit (`https://audiobookshelf.lan.gemberkoekje.nl`)

- **Root user:** the first visit asks for one. Store it in 1Password.
- **Settings:**
  - Leave "Automatically watch libraries for changes" **on** (the default). Chaptarr reports each
    import through this watcher. With it off, ABS answers but drops the update (tested on
    2026-11-09).
  - Leave "Store covers with item" and "Store metadata with item" off (the defaults). The folder
    is read-only, and the files have to stay as they are for seeding.
- **Libraries → Add:**
  - name "Luisterboeken", media type Books;
  - folder `/media/Audiobooks` (the share is mounted read-only at `/media`);
  - metadata provider Audible (`.com` or `.co.uk`);
  - tab **Schedule:** "Schedule Automatic Library Scans" on, daily, as a fallback.
- **Settings → Backups:** "Automatic backups" on, daily, "Number of backups to keep" 7. They land
  in `/metadata/backups`, on the QNAP.
- **Settings → API Keys:** two keys for the root user, "chaptarr" (D3) and "audiobookrequest" (D7),
  so each can be revoked on its own. Keep them in 1Password.
- **Settings → Users:** an account for each listener.

### D7. AudioBookRequest, first visit (`https://audiobookrequest.lan.gemberkoekje.nl`)

- **First admin:** the first visit goes to `/init`. Login type **Forms**, with a username and
  password (1Password).
- **Settings → Prowlarr / Chaptarr:**
  - download backend **Chaptarr**;
  - Chaptarr URL `http://chaptarr.zeef.svc.cluster.local:8789`, API key `CHAPTARR__AUTH__APIKEY`.
    It should show as connected.
  - The Prowlarr fields stay empty. With Chaptarr as the backend, ABR doesn't download through
    Prowlarr itself.
- **Settings → Audiobookshelf:**
  - URL `http://audiobookshelf.zeef.svc.cluster.local:13378`, API key "audiobookrequest" from D6;
  - library "Luisterboeken", and "Use ABS to mark existing books as downloaded" on.
- **Settings → Download:** Auto Download on. Requests from "trusted" users and admins then go to
  Chaptarr at once; others wait for an admin.
- **Users:** an account for each person. Group "trusted" for those whose requests may download
  without approval.

### D8. Listening

- **Phone:** the Audiobookshelf app (Android, iOS), server address
  `https://audiobookshelf.lan.gemberkoekje.nl`. It works at home and over Tailscale, and can keep
  books on the phone for offline listening.
- **Browser:** the same address.

### D9. Checks

- [ ] Chaptarr runs on PostgreSQL: tables in `chaptarr-main`. `/config` holds only `config.xml`,
      logs, covers and `staging.db`.
- [ ] ABR runs on PostgreSQL: tables in `audiobookrequest`.
- [ ] Prowlarr's indexers appear in Chaptarr as "(Prowlarr)"; MyAnonaMouse appears without it.
- [ ] A test request in ABR appears in Chaptarr (author and book, monitored) and gets searched.
- [ ] The grab is in qBittorrent with category `chaptarr`. Cleanuparr's log shows the Malware
      Blocker checked it, with no files skipped for an ordinary book.
- [ ] The finished book is in `/data/Audiobooks/<Author>/<Title>/` as a hardlink (`stat` shows 2
      links).
- [ ] ABS shows it within a minute or so (Chaptarr's push), without waiting for the daily scan.
- [ ] ABR shows the request as in the library.
- [ ] The phone app plays it, and progress matches in the browser.
- [ ] A public torrent: after 7 days (ratio doesn't count in category `chaptarr`), Chaptarr
      removes it and its staging copy, and the book stays.
- [ ] A MAM torrent is in category `chaptarr mam`, force-started, with every file wanted, and keeps
      seeding.
- [ ] The first ABS backup appears in `/metadata/backups`.
