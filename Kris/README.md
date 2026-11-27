# Kris: photography site for Kristoffer Zegers

A clickable design mock of a portfolio site for Kristoffer Zegers' photographs. Online at
<https://kris.gemberkoekje.nl> (also `gemberkoekje.nl/kris`), served by gembernodes from
`apps/kris/` there, which holds copies of `mock/`.

## Direction

- **The Guggenheim site's grammar:** a two-line wordmark (KRISTOFFER ZEGERS / FOTOGRAFIE), a row of
  capital menu items, full-width hairlines between bands, big pictures, carousels with a
  "← 1 / 3 →" counter, cards in a row, a dark footer.
- **What Kristoffer liked in his own AI draft:** the print in a white mat with a gallery caption
  line ("01 / Titel, jaar — techniek, maat — oplage"), the exhibition specs (locatie, data, status)
  and the black filmstrip under the photo.
- **One subject detail:** the home page opens on a dark gallery wall with the print and a wall label.
- Dutch site text; the NL / EN switch in the header is a placeholder. Archivo (Google Fonts) for all
  type, one light theme on purpose.

## Screens

Home, Werk (series), Index (every photo, filter per series), Serie (the photo viewer: matted print,
caption, filmstrip, arrow keys, full screen), Tentoonstellingen, Over & contact (statement, biography,
contact form). The black bar on top is for reviewers, not part of the site. Screens switch in script
and by `#hash`; nothing is saved or sent.

## Rules for this mock

- **No real photos in Git or online** until Kristoffer agrees: both repos are public. Every picture is a
  grey stand-in made by `tools/stand_ins.py`; `.gitignore` keeps anything else in `mock/img/` out.
- **All titles, texts and exhibitions are invented examples** under his real name, so the reviewer bar
  says so and the page is `noindex` (also as a header on gembernodes).
- **Pictures are WebP, never JPEG or PNG:** Flux's source-controller leaves `*.jpg`, `*.jpeg`, `*.gif` and
  `*.png` out of what it fetches from Git (its default exclusions), so gembernodes would not see them and
  its build fails.
- **Size:** the stand-ins are about 0.6 MB in total, because gembernodes serves them from one ConfigMap of
  at most 1 MiB. Real full-size photos would need another way in (an image or an NFS volume).

## Changing it

Edit `mock/index.html`, then copy it (and any changed `mock/img/` file) to `apps/kris/` in gembernodes;
the ConfigMap hash rolls the pod. A new picture also needs a line in that folder's `kustomization.yaml`.

## Open questions for Kristoffer

1. Which photos may go on the site, and at what size (the real site needs his full-resolution files)?
2. His own texts: statement, biography, series descriptions, titles, exhibitions.
3. Dutch only, or Dutch and English?
4. Prints: sizes, editions, prices or "op aanvraag"?
5. Contact details to show: email, Instagram.
