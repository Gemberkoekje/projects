# Kris: a site for all of Kristoffer Zegers' pictures

A clickable design mock of a site for Kristoffer Zegers' pictures: his photos and his AI images,
together. Online at <https://kris.gemberkoekje.nl> (also `gemberkoekje.nl/kris`), served by
gembernodes from `apps/kris/` there, which holds copies of `mock/`.

What he asked for: "Ik wil graag nog eens een website met al mijn (AI en gewone) fotos". A place
for all his pictures, not a gallery: no prints, editions or exhibitions.

## Direction

- **The Guggenheim site's grammar:** a two-line wordmark (KRISTOFFER ZEGERS / FOTO & AI), a row of
  capital menu items, full-width hairlines between bands, big pictures, a carousel with a
  "← 1 / 3 →" counter, a dark footer.
- **What Kristoffer liked in his own AI draft:** the picture in a white border with a caption line
  ("01 / Titel, jaar — AI-beeld") and the black filmstrip under it.
- **Photos and AI images side by side, never mixed up:** every picture is either a photo or an AI
  image (its album decides), and every AI image carries the label AI, on its thumbnail and in its
  caption. The menu is the filter: Alles, Foto's, AI, then Albums and Over.
- Dutch site text. Archivo (Google Fonts) for all type, one light theme on purpose.

## Screens

Home (the newest album, the albums, the newest pictures, photos and AI images), Alles / Foto's / AI
(every picture, filtered), Albums, an album (the picture, caption, filmstrip, arrow keys, full
screen), Over & contact (a short text, contact details, a message form). The black bar on top is for
reviewers, not part of the site. Screens switch in script and by `#hash`; nothing is saved or sent.

## Rules for this mock

- **No real pictures in Git or online** until Kristoffer agrees: both repos are public. Every picture
  is a stand-in made by `tools/stand_ins.py`: grey for photos, coloured for AI images.
  `.gitignore` keeps anything else in `mock/img/` out.
- **Titles and texts are invented examples** under his real name, so the reviewer bar says so and the
  page is `noindex` (also as a header on gembernodes).
- **Pictures are WebP, never JPEG or PNG:** Flux's source-controller leaves `*.jpg`, `*.jpeg`, `*.gif` and
  `*.png` out of what it fetches from Git (its default exclusions), so gembernodes would not see them and
  its build fails.
- **Size:** the stand-ins are about 0.5 MB in total, because gembernodes serves them from one ConfigMap of
  at most 1 MiB. His real pictures, all of them, need another way in (an image of their own, or an NFS
  volume), and probably a way for him to add pictures himself.

## Changing it

Edit `mock/index.html`, then copy it (and any changed `mock/img/` file) to `apps/kris/` in gembernodes;
the ConfigMap hash rolls the pod. A new picture also needs a line in that folder's `kustomization.yaml`.

## Open questions for Kristoffer

1. Which pictures go on the site, and how many: "al mijn foto's" may be hundreds. Full resolution files?
2. Albums: how does he group them now (by subject, by year, by trip)? And does an album hold only photos
   or only AI images, as in the mock, or can it mix?
3. For AI images: show which tool or prompt made them, or only the label AI?
4. Does he want to add pictures himself later (an upload page), or have them added for him?
5. Contact details to show: email, Instagram. A message form, or only an address?
