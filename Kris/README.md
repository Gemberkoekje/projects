# Kris: a site for all of Kristoffer Zegers' pictures

A site for Kristoffer Zegers' pictures, his photos and his AI images together. What he asked for:
"Ik wil graag nog eens een website met al mijn (AI en gewone) fotos". A place for all his pictures,
not a gallery: no prints, editions or exhibitions.

Two parts:

- **[`site/`](site/): the real site**, in plain PHP, for Kristoffer's own hosting (he wants it on his own
  site). He uploads his pictures himself in a small beheer, at full quality instead of through WhatsApp.
  How to put it online: [INSTALLEREN.md](INSTALLEREN.md) (Dutch, for whoever installs it).
- **[`mock/`](mock/): the approved design**, a clickable mock with stand-in pictures. Online as a preview at
  <https://kris.gemberkoekje.nl> (also `gemberkoekje.nl/kris`), served by gembernodes from copies in
  `apps/kris/` there. The site is built from it.

## Direction (both)

- **The Guggenheim site's grammar:** a two-line wordmark (KRISTOFFER ZEGERS / FOTO & AI), a row of
  capital menu items, full-width hairlines between bands, big pictures, a carousel with a
  "← 1 / 3 →" counter, a dark footer.
- **What Kristoffer liked in his own AI draft:** the picture in a white border with a caption line
  ("01 / Titel, jaar — AI-beeld") and the black filmstrip under it.
- **From the Louvre's exhibition pages** (his second example): the names of works (pictures and albums)
  in thin serif capitals (Marcellus), the AI label as a white pill on the picture (AI-BEELD), section
  headings in spaced capitals with a hairline after them, cards with the name under the picture for the
  newest pictures, and album cards with the name over the picture. Interface text stays in Archivo.
- **Photos and AI images side by side, never mixed up:** every picture is either a photo or an AI
  image, chosen per picture, and every AI image carries the label AI, on its thumbnail and in its
  caption. The menu is the filter: Alles, Foto's, AI, then Albums (only when there are albums) and Over.
- Dutch site text. Archivo for the interface, Marcellus for the names of works (in the site both served
  from `site/assets/fonts/`, not from Google), one light theme on purpose.

## The site (`site/`)

Plain PHP 8.1+ with GD, no Composer, no database, so it runs on ordinary shared hosting:

- `index.php` is the public site: one page, its screens switched by `assets/site.js` (from the mock),
  filled with the JSON `lib/Site.php` makes from `data/site.json`. Every link is relative, so it works in
  the web root or a subfolder. `#p-<id>` links to one picture.
- `beheer/` is the beheer: sign in with one password (`config.php`, or the one Kristoffer set in the
  beheer, in `data/password.json`), upload pictures (each with a title and Foto or AI-beeld; the script
  sends them one at a time with a progress bar, and the plain form works without it), edit or delete
  them, put one on the front page, keep albums, and set the texts. The Gegevens page shows whether the
  host has what the site needs.
- **Pictures:** `lib/Images.php` keeps the original byte for byte in `data/originals/`, which isn't public
  (phone photos carry their GPS position), and writes two copies without metadata to `media/`: 2048 and
  640 px on the long side, WebP where GD can, else JPEG, turned upright from the EXIF orientation. JPEG,
  PNG and WebP only; HEIC gets its own explanation.
- **Data:** `lib/Store.php` keeps everything in `data/site.json`, written under a lock, through a
  temporary file. `lib/Site.php` holds the rules as pure functions.
- **Safety:** `.htaccess` keeps `data/`, `lib/`, `config.php` and dotfiles private (Apache); `.user.ini` and
  `.htaccess` ask for 64 MB uploads. Ten wrong passwords from one address in 15 minutes block it for a
  while; every form carries a CSRF token; the beheer sends `no-store`, `noindex` and `X-Frame-Options`.
- Neither the password nor Kristoffer's pictures go into Git: `.gitignore` keeps `site/config.php`,
  `site/data/*` and `site/media/*` out.

### Tests

```bash
php Kris/tests/run.php
```

No PHPUnit: a small runner, so it runs wherever PHP does. It covers the store, the rules, the image
handling (all eight EXIF orientations, from fixtures made with Pillow, the copy sizes, no EXIF in the
copies, the original kept byte for byte, refused files) and the password, rate limit and CSRF token.
The workflow `kris-site.yml` runs it on PHP 8.1 and 8.3, and on `main` keeps `site/` as a download
(artifact `kris-site`).

To run the site locally (PHP's built-in server ignores `.htaccess` and `.user.ini`, hence the `-d`s):

```bash
KRIS_DATA_DIR=/tmp/kris-data KRIS_MEDIA_DIR=/tmp/kris-data/media BEHEER_PASSWORD=test \
  php -d upload_max_filesize=64M -d post_max_size=72M -d memory_limit=512M -S 127.0.0.1:8000 -t Kris/site
```

The images then come from `/tmp/kris-data/media`, which the built-in server doesn't serve: for a full
check, use Apache with `AllowOverride All` (as most hosts do) and a `config.php`.

## The mock (`mock/`)

- **Stand-in pictures only:** every picture is made by `tools/stand_ins.py` (grey for photos, coloured
  for AI images); `.gitignore` keeps anything else in `mock/img/` out. Titles and texts are invented
  examples, so the reviewer bar says so and the page is `noindex`.
- **Pictures are WebP, never JPEG or PNG:** Flux's source-controller leaves `*.jpg`, `*.jpeg`, `*.gif` and
  `*.png` out of what it fetches from Git, so gembernodes would not see them. They stay about 0.5 MB in
  total: gembernodes serves them from one ConfigMap of at most 1 MiB.
- To change it, edit `mock/index.html`, then copy it (and any changed `mock/img/` file) to `apps/kris/` in
  gembernodes; the ConfigMap hash rolls the pod.

## Open questions for Kristoffer

1. Which host, and does it meet [INSTALLEREN.md](INSTALLEREN.md)'s list (PHP 8.1+, GD, Apache, HTTPS)?
2. For AI images: show which tool or prompt made them, or only the label AI?
3. Order: newest first, as now, or does he want to arrange them himself?
