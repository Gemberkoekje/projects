<?php
// The public site: one page, its screens switched by assets/site.js (Alles, Foto's, AI, Albums, an
// album or a picture, Over), filled from data/site.json. Works in the web root or in a subfolder:
// every link is relative.
declare(strict_types=1);

require __DIR__ . '/lib/bootstrap.php';

$config = kris_config();
$data = (new Store($config['data_dir']))->read();
$settings = $data['settings'];
$view = Site::publicView($data, 'media');

header('X-Content-Type-Options: nosniff');
header('Referrer-Policy: strict-origin-when-cross-origin');
if (!$settings['indexable']) {
    header('X-Robots-Tag: noindex');
}

$name = $settings['name'];
$tagline = $settings['tagline'];
$hasAlbums = (bool) array_filter($data['albums'], static function (array $album) use ($data): bool {
    foreach ($data['photos'] as $photo) {
        if ($photo['album'] === $album['id']) {
            return true;
        }
    }
    return false;
});

// For the preview a link gets in WhatsApp and elsewhere: the picture on the front page.
$feature = Site::findPhoto($data, (string) $settings['featured']) ?? ($data['photos'][0] ?? null);
$here = (Auth::isHttps() ? 'https' : 'http') . '://' . ($_SERVER['HTTP_HOST'] ?? 'localhost')
    . rtrim(str_replace('\\', '/', dirname((string) ($_SERVER['SCRIPT_NAME'] ?? '/index.php'))), '/') . '/';
$description = $settings['about'] !== '' ? mb_substr(preg_replace('/\s+/u', ' ', $settings['about']), 0, 180)
    : "Foto's en AI-beelden van $name.";

$about = $settings['about'] !== '' ? $settings['about']
    : "$name maakt foto's en beelden met AI. Op deze site staan ze allemaal bij elkaar.\n\nElk AI-beeld heeft het label AI, zodat altijd duidelijk is wat een foto is en wat niet.";
$paragraphs = array_filter(array_map('trim', preg_split('/\R\s*\R/u', $about) ?: []));
$instagram = $settings['instagram'] !== '' ? 'https://www.instagram.com/' . rawurlencode($settings['instagram']) . '/' : null;
?>
<!doctype html>
<html lang="nl">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<title><?= e($name) ?><?= $tagline !== '' ? ' · ' . e($tagline) : '' ?></title>
<meta name="description" content="<?= e($description) ?>">
<?php if (!$settings['indexable']): ?><meta name="robots" content="noindex"><?php endif; ?>
<meta property="og:title" content="<?= e($name) ?>">
<meta property="og:description" content="<?= e($description) ?>">
<meta property="og:type" content="website">
<?php if ($feature): ?><meta property="og:image" content="<?= e($here . 'media/' . $feature['id'] . '-large.' . $feature['format']) ?>"><?php endif; ?>
<link rel="preload" href="assets/fonts/archivo-latin.woff2" as="font" type="font/woff2" crossorigin>
<link rel="stylesheet" href="assets/site.css">
</head>
<body>

<header class="site" id="site">
  <div class="bar wrap">
    <a class="wordmark" href="#home" data-go="home" aria-label="<?= e($name) ?>, home">
      <span class="wm-name"><?= e(mb_strtoupper($name)) ?></span>
      <?php if ($tagline !== ''): ?><span class="wm-sub"><?= e(mb_strtoupper($tagline)) ?></span><?php endif; ?>
    </a>
    <nav class="util" aria-label="Snelkoppelingen">
      <?php if ($instagram): ?><a href="<?= e($instagram) ?>" rel="noopener" target="_blank">Instagram</a><?php endif; ?>
      <a href="#contact" data-go="contact">Contact</a>
    </nav>
    <button class="menu-btn" type="button" id="menu-open" aria-expanded="false" aria-controls="menu"><i></i>Menu</button>
  </div>
  <nav class="primary wrap" aria-label="Hoofdmenu">
    <a href="#alles" data-go="alles">Alles</a>
    <a href="#fotos" data-go="fotos">Foto's</a>
    <a href="#ai" data-go="ai">AI</a>
    <?php if ($hasAlbums): ?><a href="#albums" data-go="albums">Albums</a><?php endif; ?>
    <a href="#over" data-go="over">Over</a>
  </nav>
</header>

<div class="menu" id="menu" hidden>
  <div class="menu-head">
    <span class="wordmark"><span class="wm-name"><?= e(mb_strtoupper($name)) ?></span></span>
    <button class="menu-btn" type="button" id="menu-close" style="display:inline-flex">Sluiten</button>
  </div>
  <a class="big" href="#home" data-go="home">Home</a>
  <a class="big" href="#alles" data-go="alles">Alles</a>
  <a class="big" href="#fotos" data-go="fotos">Foto's</a>
  <a class="big" href="#ai" data-go="ai">AI</a>
  <?php if ($hasAlbums): ?><a class="big" href="#albums" data-go="albums">Albums</a><?php endif; ?>
  <a class="big" href="#over" data-go="over">Over</a>
  <a class="big" href="#contact" data-go="contact">Contact</a>
</div>

<main>
  <!-- Home: the front-page picture, the newest pictures, the albums, photos and AI images -->
  <section data-screen="home">
    <div class="hero" id="hero">
      <div class="wrap hero-grid">
        <div class="hero-text">
          <span class="label" id="hero-label"></span>
          <h1 class="display" id="hero-title"><?= e($name) ?></h1>
          <p id="hero-text">Hier komen de foto's en AI-beelden van <?= e($name) ?>.</p>
          <div class="links" id="hero-links"></div>
        </div>
        <figure class="print" id="hero-print" hidden>
          <a class="mat" href="#alles" id="hero-open"><img id="hero-img" alt=""></a>
          <figcaption id="hero-cap"></figcaption>
        </figure>
      </div>
    </div>

    <section class="band" id="band-latest" hidden aria-labelledby="h-latest">
      <div class="wrap">
        <div class="band-head">
          <h2 id="h-latest">Nieuwste beelden</h2>
          <a class="more" href="#alles" data-go="alles">Alle beelden →</a>
        </div>
        <div class="latest" id="latest"></div>
      </div>
    </section>

    <section class="band" id="band-albums" hidden aria-labelledby="h-albums">
      <div class="wrap">
        <div class="band-head">
          <h2 id="h-albums">Albums</h2>
          <a class="more" href="#albums" data-go="albums">Alle albums →</a>
        </div>
        <div class="carousel" id="car-albums">
          <div class="car-view"><div class="car-track"></div></div>
          <div class="car-ctrl">
            <button class="arrow" type="button" data-prev aria-label="Vorig album">←</button>
            <span data-count></span>
            <button class="arrow" type="button" data-next aria-label="Volgend album">→</button>
          </div>
        </div>
      </div>
    </section>

    <section class="band" id="band-pair" hidden>
      <div class="wrap pair">
        <a href="#fotos" data-go="fotos">
          <h2>Foto's</h2>
          <img id="pair-foto" alt="">
          <p>Gewone foto's, in albums of allemaal bij elkaar.</p>
          <span class="more">Bekijk de foto's →</span>
        </a>
        <a href="#ai" data-go="ai">
          <h2>AI-beelden</h2>
          <img id="pair-ai" alt="">
          <p>Beelden gemaakt met AI. Ze staan ook tussen de foto's, altijd met het label AI.</p>
          <span class="more">Bekijk de AI-beelden →</span>
        </a>
      </div>
    </section>
  </section>

  <!-- Alles, Foto's, AI: every picture, filtered -->
  <section data-screen="index" hidden>
    <div class="page-head">
      <div class="wrap">
        <h1 id="ix-title">Alle beelden</h1>
        <p class="lead" id="ix-lead"></p>
      </div>
    </div>
    <div class="wrap">
      <div class="chips" id="chips" role="group" aria-label="Toon"></div>
      <div class="masonry" id="masonry"></div>
      <p class="empty" id="ix-empty" hidden>Hier staan nog geen beelden.</p>
    </div>
  </section>

  <!-- Albums -->
  <section data-screen="albums" hidden>
    <div class="page-head">
      <div class="wrap">
        <h1>Albums</h1>
        <p class="lead">Foto's en AI-beelden per onderwerp, de nieuwste eerst.</p>
      </div>
    </div>
    <div class="wrap" id="album-list"></div>
  </section>

  <!-- One album, or the pictures of Alles / Foto's / AI one by one -->
  <section data-screen="viewer" hidden>
    <div class="wrap viewer">
      <aside class="v-side">
        <a class="back" href="#alles" id="v-back">← Terug</a>
        <div>
          <span class="label muted" id="v-label"></span>
          <h1 id="v-title"></h1>
        </div>
        <div class="essay" id="v-essay"></div>
        <hr>
        <dl class="specs" id="v-specs"></dl>
      </aside>
      <div class="v-main">
        <div class="stage">
          <button class="stage-nav prev" type="button" id="v-prev" aria-label="Vorig beeld">←</button>
          <button class="mat" type="button" id="v-open" aria-label="Bekijk op volledig scherm" style="border:0;cursor:zoom-in">
            <img id="v-img" alt="">
          </button>
          <button class="stage-nav next" type="button" id="v-next" aria-label="Volgend beeld">→</button>
        </div>
        <div class="caption">
          <p class="what" id="v-cap"></p>
          <div class="acts">
            <span id="v-count"></span>
            <button class="linkbtn" type="button" id="v-full">Volledig scherm</button>
          </div>
        </div>
        <div class="strip" id="v-strip" aria-label="Beelden"></div>
      </div>
    </div>
  </section>

  <!-- Over & contact -->
  <section data-screen="over" hidden>
    <div class="page-head">
      <div class="wrap">
        <h1>Over</h1>
      </div>
    </div>
    <div class="wrap about no-portrait">
      <div class="prose">
        <h2 style="margin-top:0"><?= e($name) ?></h2>
        <?php foreach (array_values($paragraphs) as $i => $paragraph): ?>
        <p<?= $i === 0 ? ' class="big"' : '' ?>><?= nl2br(e($paragraph)) ?></p>
        <?php endforeach; ?>

        <section id="contact" aria-labelledby="h-contact" style="display:grid;gap:1rem">
          <h2 id="h-contact">Contact</h2>
          <?php if ($settings['email'] === '' && !$instagram): ?>
          <p class="muted">Contactgegevens volgen.</p>
          <?php else: ?>
          <dl class="contact-lines">
            <?php if ($settings['email'] !== ''): ?><dt>E-mail</dt><dd><a href="mailto:<?= e($settings['email']) ?>"><?= e($settings['email']) ?></a></dd><?php endif; ?>
            <?php if ($instagram): ?><dt>Instagram</dt><dd><a href="<?= e($instagram) ?>" rel="noopener" target="_blank">@<?= e($settings['instagram']) ?></a></dd><?php endif; ?>
          </dl>
          <?php endif; ?>
        </section>
      </div>
    </div>
  </section>
</main>

<footer class="foot">
  <div class="wrap foot-top">
    <a class="wordmark" href="#home" data-go="home"><span class="wm-name"><?= e(mb_strtoupper($name)) ?></span><?php if ($tagline !== ''): ?><span class="wm-sub"><?= e(mb_strtoupper($tagline)) ?></span><?php endif; ?></a>
  </div>
  <div class="wrap foot-cols">
    <div>
      <h4>Bekijken</h4>
      <ul>
        <li><a href="#alles" data-go="alles">Alle beelden</a></li>
        <li><a href="#fotos" data-go="fotos">Foto's</a></li>
        <li><a href="#ai" data-go="ai">AI-beelden</a></li>
        <?php if ($hasAlbums): ?><li><a href="#albums" data-go="albums">Albums</a></li><?php endif; ?>
      </ul>
    </div>
    <div>
      <h4>Contact</h4>
      <?php if ($settings['email'] !== ''): ?><p><a href="mailto:<?= e($settings['email']) ?>"><?= e($settings['email']) ?></a></p><?php else: ?><p>Contactgegevens volgen.</p><?php endif; ?>
    </div>
    <?php if ($instagram): ?>
    <div>
      <h4>Volgen</h4>
      <p>Nieuw werk staat ook op Instagram.</p>
      <a class="more" href="<?= e($instagram) ?>" rel="noopener" target="_blank">@<?= e($settings['instagram']) ?> →</a>
    </div>
    <?php endif; ?>
  </div>
  <div class="wrap foot-bottom">
    <span>© <?= date('Y') ?> <?= e($name) ?>. Alle beelden © <?= e($name) ?>.</span>
  </div>
</footer>

<div class="lightbox" id="lightbox" hidden role="dialog" aria-modal="true" aria-label="Beeld op volledig scherm">
  <div class="lb-top"><span id="lb-count"></span><button type="button" id="lb-close">Sluiten</button></div>
  <div class="lb-img">
    <button class="arrow prev" type="button" id="lb-prev" aria-label="Vorig beeld">←</button>
    <img id="lb-img" alt="">
    <button class="arrow next" type="button" id="lb-next" aria-label="Volgend beeld">→</button>
  </div>
  <p class="lb-cap" id="lb-cap"></p>
</div>

<script type="application/json" id="site-data"><?= json_for_script($view) ?></script>
<script src="assets/site.js"></script>
</body>
</html>
