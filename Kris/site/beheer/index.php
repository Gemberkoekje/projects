<?php
// The beheer: sign in with the one password, upload pictures (each one a photo or an AI image, with a
// title), change or delete them, keep albums, and set the texts on the site. Plain forms; the upload
// form sends the files one by one with JavaScript, with a progress bar each, and also works without it.
declare(strict_types=1);

require dirname(__DIR__) . '/lib/bootstrap.php';

header('Cache-Control: no-store');
header('X-Frame-Options: DENY');
header('X-Content-Type-Options: nosniff');
header('X-Robots-Tag: noindex, nofollow');
header('Referrer-Policy: same-origin');

$config = kris_config();
$dataDir = $config['data_dir'];
$originalsDir = $dataDir . '/originals';
$mediaDir = $config['media_dir'];
$store = new Store($dataDir);
$auth = new Auth($config, $dataDir);
$auth->startSession();

$isAjax = ($_POST['ajax'] ?? '') === '1' || ($_SERVER['HTTP_X_REQUESTED_WITH'] ?? '') === 'fetch';

function flash(string $message, string $kind = 'ok'): void
{
    $_SESSION['flash'] = [$kind, $message];
}

function go(string $page = 'beelden', array $query = []): never
{
    header('Location: ./?' . http_build_query(['p' => $page] + $query), true, 303);
    exit;
}

function json_out(array $body, int $status = 200): never
{
    http_response_code($status);
    header('Content-Type: application/json; charset=utf-8');
    echo json_encode($body, JSON_UNESCAPED_UNICODE);
    exit;
}

// ---------- Requests that change something ----------

if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    $action = (string) ($_POST['action'] ?? '');
    $length = (int) ($_SERVER['CONTENT_LENGTH'] ?? 0);

    // A file over post_max_size arrives as an empty request: no fields, no token.
    if ($length > 0 && $_POST === [] && $_FILES === []) {
        $message = 'Dat bestand is te groot voor deze server (hoogstens ' . megabytes(upload_limit()) . ').';
        if ($isAjax) {
            json_out(['ok' => false, 'error' => $message], 413);
        }
        flash($message, 'error');
        go();
    }

    if ($action === 'login') {
        if (!$auth->configured()) {
            go('login');
        }
        if ($auth->blocked()) {
            flash('Te vaak een verkeerd wachtwoord. Probeer het over een kwartier opnieuw.', 'error');
            go('login');
        }
        if ($auth->check((string) ($_POST['password'] ?? ''))) {
            $auth->clearFailures();
            $auth->login();
            go();
        }
        $auth->recordFailure();
        flash('Dat wachtwoord klopt niet.', 'error');
        go('login');
    }

    if (!$auth->loggedIn()) {
        if ($isAjax) {
            json_out(['ok' => false, 'error' => 'Je bent uitgelogd. Log opnieuw in.'], 401);
        }
        go('login');
    }
    if (!$auth->validCsrf($_POST['csrf'] ?? null)) {
        $message = 'Deze pagina was verlopen. Probeer het nog een keer.';
        if ($isAjax) {
            json_out(['ok' => false, 'error' => $message], 400);
        }
        flash($message, 'error');
        go();
    }

    try {
        switch ($action) {
            case 'logout':
                $auth->logout();
                header('Location: ../', true, 303);
                exit;

            case 'upload':
                // One request per file from the script, or several files at once from the plain form.
                $files = $_FILES['photos'] ?? null;
                if (!is_array($files) || !is_array($files['name'])) {
                    throw new UserError('Kies eerst een of meer beelden.');
                }
                $titles = (array) ($_POST['titles'] ?? []);
                $added = 0;
                $errors = [];
                foreach ($files['name'] as $i => $name) {
                    $error = (int) $files['error'][$i];
                    if ($error === UPLOAD_ERR_NO_FILE) {
                        continue;
                    }
                    try {
                        if ($error === UPLOAD_ERR_INI_SIZE || $error === UPLOAD_ERR_FORM_SIZE) {
                            throw new UserError('Dit bestand is groter dan deze server toestaat (hoogstens ' . megabytes(upload_limit()) . ').');
                        }
                        if ($error !== UPLOAD_ERR_OK) {
                            throw new UserError('Het uploaden ging mis (code ' . $error . '). Probeer het nog een keer.');
                        }
                        $id = Site::newId();
                        $title = trim((string) ($titles[$i] ?? '')) ?: Site::titleFromFilename((string) $name);
                        $store->update(static function (array $data) use ($id, $title, $files, $i, $originalsDir, $mediaDir): array {
                            $album = '';
                            $newAlbum = trim((string) ($_POST['album_new'] ?? ''));
                            if (($_POST['album'] ?? '') === '__new' && $newAlbum !== '') {
                                [$data, $album] = Site::albumNamed($data, $newAlbum);
                            } elseif (($_POST['album'] ?? '') !== '__new') {
                                $album = (string) ($_POST['album'] ?? '');
                            }
                            // Check the fields before the file is processed, so a bad title costs no work.
                            $fields = Site::photoFields($data, ['title' => $title, 'kind' => $_POST['kind'] ?? '', 'album' => $album, 'year' => date('Y')]);
                            $image = Images::store((string) $files['tmp_name'][$i], $id, $originalsDir, $mediaDir, true);
                            $fields['year'] = $image['year'] ?? $fields['year'];
                            return Site::addPhoto($data, ['id' => $id] + $fields + [
                                'w' => $image['w'],
                                'h' => $image['h'],
                                'ext' => $image['ext'],
                                'format' => $image['format'],
                                'bytes' => $image['bytes'],
                                'original_name' => mb_substr((string) $files['name'][$i], 0, 200),
                                'uploaded' => date('c'),
                            ]);
                        });
                        $added++;
                    } catch (UserError $e) {
                        $errors[] = $name . ': ' . $e->getMessage();
                    }
                }
                if ($isAjax) {
                    $errors ? json_out(['ok' => false, 'error' => implode(' ', $errors)], 422) : json_out(['ok' => true, 'added' => $added]);
                }
                if ($errors) {
                    flash(($added ? "$added beeld(en) toegevoegd. " : '') . 'Niet gelukt: ' . implode(' ', $errors), 'error');
                } else {
                    flash($added === 1 ? 'Het beeld staat op de site.' : "$added beelden staan op de site.");
                }
                go();

            case 'photo-save':
                $id = (string) ($_POST['id'] ?? '');
                $store->update(static function (array $data) use ($id): array {
                    $input = $_POST;
                    if (($input['album'] ?? '') === '__new') {
                        [$data, $input['album']] = Site::albumNamed($data, (string) ($input['album_new'] ?? ''));
                    }
                    return Site::updatePhoto($data, $id, $input);
                });
                flash('Opgeslagen.');
                go();

            case 'photo-delete':
                $id = (string) ($_POST['id'] ?? '');
                $photo = Site::findPhoto($store->read(), $id);
                if ($photo === null) {
                    throw new UserError('Dat beeld bestaat niet meer.');
                }
                $store->update(static fn(array $data): array => Site::deletePhoto($data, $id));
                Images::delete($photo, $originalsDir, $mediaDir);
                flash('"' . $photo['title'] . '" is verwijderd.');
                go();

            case 'album-add':
                $store->update(static fn(array $data): array => Site::albumNamed($data, (string) ($_POST['title'] ?? ''))[0]);
                flash('Het album is aangemaakt.');
                go('albums');

            case 'album-save':
                $id = (string) ($_POST['id'] ?? '');
                $store->update(static fn(array $data): array => Site::updateAlbum($data, $id, $_POST));
                flash('Het album is opgeslagen.');
                go('albums');

            case 'album-delete':
                $id = (string) ($_POST['id'] ?? '');
                $store->update(static fn(array $data): array => Site::deleteAlbum($data, $id));
                flash('Het album is verwijderd. De beelden staan nog op de site.');
                go('albums');

            case 'settings-save':
                $store->update(static fn(array $data): array => Site::updateSettings($data, $_POST));
                flash('De gegevens zijn opgeslagen.');
                go('gegevens');

            case 'password-change':
                if (($_POST['new'] ?? '') !== ($_POST['repeat'] ?? '')) {
                    throw new UserError('De twee nieuwe wachtwoorden zijn niet gelijk.');
                }
                $auth->changePassword((string) ($_POST['current'] ?? ''), (string) ($_POST['new'] ?? ''));
                flash('Je wachtwoord is veranderd.');
                go('gegevens');
        }
        throw new UserError('Onbekende actie.');
    } catch (UserError $e) {
        if ($isAjax) {
            json_out(['ok' => false, 'error' => $e->getMessage()], 422);
        }
        flash($e->getMessage(), 'error');
        $back = (string) ($_POST['back'] ?? 'beelden');
        go(in_array($back, ['beelden', 'beeld', 'albums', 'gegevens'], true) ? $back : 'beelden', $back === 'beeld' ? ['id' => (string) ($_POST['id'] ?? '')] : []);
    } catch (Throwable $e) {
        error_log('kris beheer: ' . $e);
        $message = 'Er ging iets mis op de server: ' . $e->getMessage();
        if ($isAjax) {
            json_out(['ok' => false, 'error' => $message], 500);
        }
        flash($message, 'error');
        go();
    }
}

// ---------- Pages ----------

$page = (string) ($_GET['p'] ?? 'beelden');
if (!$auth->loggedIn()) {
    $page = 'login';
}
$data = $store->read();
$settings = $data['settings'];
$flash = $_SESSION['flash'] ?? null;
unset($_SESSION['flash']);
$csrf = $auth->loggedIn() ? $auth->csrf() : '';
$albumsById = array_column($data['albums'], 'title', 'id');

function thumb_url(array $photo): string
{
    return '../media/' . rawurlencode($photo['id'] . '-thumb.' . $photo['format']);
}

function album_select(array $albums, ?string $current, string $id = 'album'): string
{
    $html = '<select name="album" id="' . e($id) . '" data-album-select>';
    $html .= '<option value="">Geen album</option>';
    foreach ($albums as $album) {
        $html .= '<option value="' . e($album['id']) . '"' . ($album['id'] === $current ? ' selected' : '') . '>' . e($album['title']) . '</option>';
    }
    $html .= '<option value="__new">Nieuw album…</option></select>';
    $html .= '<input type="text" name="album_new" id="' . e($id) . '-new" placeholder="Naam van het nieuwe album" maxlength="' . Site::TITLE_MAX . '" hidden>';
    return $html;
}

$title = ['login' => 'Inloggen', 'beelden' => 'Beelden', 'beeld' => 'Beeld bewerken', 'albums' => 'Albums', 'gegevens' => 'Gegevens'][$page] ?? 'Beelden';
?>
<!doctype html>
<html lang="nl">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="robots" content="noindex, nofollow">
<title><?= e($title) ?> · Beheer · <?= e($settings['name']) ?></title>
<link rel="preload" href="../assets/fonts/archivo-latin.woff2" as="font" type="font/woff2" crossorigin>
<link rel="stylesheet" href="../assets/beheer.css">
</head>
<body>
<header class="top">
  <div class="wrap top-bar">
    <a class="brand" href="./"><span><?= e(mb_strtoupper($settings['name'])) ?></span> Beheer</a>
    <?php if ($page !== 'login'): ?>
    <div class="top-acts">
      <a href="../" target="_blank" rel="noopener">Naar de site ↗</a>
      <form method="post" action="./"><input type="hidden" name="action" value="logout"><input type="hidden" name="csrf" value="<?= e($csrf) ?>"><button type="submit" class="linkish">Uitloggen</button></form>
    </div>
    <?php endif; ?>
  </div>
  <?php if ($page !== 'login'): ?>
  <nav class="wrap tabs" aria-label="Beheer">
    <a href="./?p=beelden"<?= in_array($page, ['beelden', 'beeld'], true) ? ' aria-current="page"' : '' ?>>Beelden</a>
    <a href="./?p=albums"<?= $page === 'albums' ? ' aria-current="page"' : '' ?>>Albums</a>
    <a href="./?p=gegevens"<?= $page === 'gegevens' ? ' aria-current="page"' : '' ?>>Gegevens</a>
  </nav>
  <?php endif; ?>
</header>

<main class="wrap">
<?php if ($flash): ?>
  <p class="flash flash-<?= e($flash[0]) ?>" role="<?= $flash[0] === 'error' ? 'alert' : 'status' ?>"><?= e($flash[1]) ?></p>
<?php endif; ?>

<?php if ($page === 'login'): ?>
  <section class="card narrow">
    <h1>Inloggen</h1>
    <?php if (!$auth->configured()): ?>
      <p>Het beheer heeft nog geen wachtwoord. Zet er een in <code>config.php</code> (zie <code>config.example.php</code>) en laad deze pagina opnieuw.</p>
    <?php else: ?>
      <form method="post" action="./" class="stack">
        <input type="hidden" name="action" value="login">
        <label for="password">Wachtwoord</label>
        <input type="password" id="password" name="password" autocomplete="current-password" required autofocus>
        <button type="submit" class="btn">Inloggen</button>
      </form>
    <?php endif; ?>
  </section>

<?php elseif ($page === 'beelden'): ?>
  <section class="card" id="upload">
    <h1>Beelden toevoegen</h1>
    <p class="muted">Kies een of meer beelden: JPEG, PNG of WebP, hoogstens <?= e(megabytes(upload_limit())) ?> per bestand. Het origineel wordt bewaard zoals het is; de site toont een verkleinde kopie.</p>
    <form method="post" action="./" enctype="multipart/form-data" class="stack" id="upload-form">
      <input type="hidden" name="action" value="upload">
      <input type="hidden" name="csrf" value="<?= e($csrf) ?>">
      <label for="photos">Beelden</label>
      <input type="file" id="photos" name="photos[]" accept="image/jpeg,image/png,image/webp" multiple required>

      <div id="file-list" class="file-list" hidden></div>

      <fieldset class="kind-batch" id="kind-batch">
        <legend>Wat is het?</legend>
        <p class="muted js-only" hidden>Kies het bij elk beeld hierboven, of zet ze allemaal tegelijk:</p>
        <div class="choice">
          <label><input type="radio" name="kind" value="foto" required> Foto</label>
          <label><input type="radio" name="kind" value="ai" required> AI-beeld</label>
        </div>
      </fieldset>

      <label for="album">Album <span class="muted">(niet verplicht)</span></label>
      <?= album_select($data['albums'], null) ?>

      <p class="hint" id="upload-hint" hidden></p>
      <button type="submit" class="btn" id="upload-btn">Uploaden</button>
    </form>
  </section>

  <section class="card">
    <h2>Op de site <span class="muted">(<?= count($data['photos']) ?>)</span></h2>
    <?php if (!$data['photos']): ?>
      <p class="muted">Er staan nog geen beelden op de site.</p>
    <?php else: ?>
      <ul class="photo-list">
        <?php foreach ($data['photos'] as $photo): ?>
        <li>
          <a class="photo-row" href="./?p=beeld&amp;id=<?= e($photo['id']) ?>">
            <img src="<?= e(thumb_url($photo)) ?>" alt="" loading="lazy" width="96" height="96">
            <span class="photo-meta">
              <strong><?= e($photo['title']) ?></strong>
              <span>
                <span class="badge badge-<?= e($photo['kind']) ?>"><?= e(Site::KINDS[$photo['kind']]) ?></span>
                <?= e((string) $photo['year']) ?>
                <?php if ($photo['album'] && isset($albumsById[$photo['album']])): ?> · <?= e($albumsById[$photo['album']]) ?><?php endif; ?>
                <?php if ($settings['featured'] === $photo['id']): ?> · op de voorpagina<?php endif; ?>
              </span>
            </span>
            <span class="go">Bewerken →</span>
          </a>
        </li>
        <?php endforeach; ?>
      </ul>
    <?php endif; ?>
  </section>

<?php elseif ($page === 'beeld'):
    $photo = Site::findPhoto($data, (string) ($_GET['id'] ?? ''));
    if ($photo === null): ?>
  <section class="card"><p>Dat beeld bestaat niet (meer). <a href="./">Terug naar de beelden</a></p></section>
    <?php else: ?>
  <p><a href="./">← Alle beelden</a></p>
  <section class="card edit">
    <img class="edit-img" src="<?= e(thumb_url($photo)) ?>" alt="">
    <form method="post" action="./" class="stack">
      <h1>Beeld bewerken</h1>
      <input type="hidden" name="action" value="photo-save">
      <input type="hidden" name="back" value="beeld">
      <input type="hidden" name="csrf" value="<?= e($csrf) ?>">
      <input type="hidden" name="id" value="<?= e($photo['id']) ?>">
      <label for="title">Titel</label>
      <input type="text" id="title" name="title" value="<?= e($photo['title']) ?>" maxlength="<?= Site::TITLE_MAX ?>" required>
      <label for="caption">Omschrijving <span class="muted">(niet verplicht, bijvoorbeeld de plaats)</span></label>
      <textarea id="caption" name="caption" maxlength="<?= Site::CAPTION_MAX ?>" rows="3"><?= e($photo['caption']) ?></textarea>
      <fieldset>
        <legend>Wat is het?</legend>
        <div class="choice">
          <?php foreach (Site::KINDS as $value => $label): ?>
          <label><input type="radio" name="kind" value="<?= e($value) ?>"<?= $photo['kind'] === $value ? ' checked' : '' ?> required> <?= e($label) ?></label>
          <?php endforeach; ?>
        </div>
      </fieldset>
      <label for="album">Album</label>
      <?= album_select($data['albums'], $photo['album']) ?>
      <label for="year">Jaar</label>
      <input type="number" id="year" name="year" value="<?= e((string) $photo['year']) ?>" min="1900" max="<?= (int) date('Y') + 1 ?>" inputmode="numeric" required>
      <label class="check"><input type="checkbox" name="featured" value="1"<?= $settings['featured'] === $photo['id'] ? ' checked' : '' ?>> Groot op de voorpagina</label>
      <button type="submit" class="btn">Opslaan</button>
      <p class="muted small">Origineel: <?= e($photo['original_name'] ?? '') ?>, <?= e((string) $photo['w']) ?> × <?= e((string) $photo['h']) ?> pixels, <?= e(megabytes((int) $photo['bytes'])) ?>.</p>
    </form>
  </section>
  <section class="card danger">
    <h2>Verwijderen</h2>
    <p class="muted">Het beeld verdwijnt van de site, en het origineel ook. Dat kan niet ongedaan worden gemaakt.</p>
    <form method="post" action="./" onsubmit="return confirm('&quot;<?= e(addslashes($photo['title'])) ?>&quot; echt verwijderen?');">
      <input type="hidden" name="action" value="photo-delete">
      <input type="hidden" name="csrf" value="<?= e($csrf) ?>">
      <input type="hidden" name="id" value="<?= e($photo['id']) ?>">
      <button type="submit" class="btn btn-danger">Verwijder dit beeld</button>
    </form>
  </section>
    <?php endif; ?>

<?php elseif ($page === 'albums'): ?>
  <section class="card">
    <h1>Albums</h1>
    <p class="muted">Een album is niet verplicht: elk beeld staat ook gewoon bij Alles, Foto's of AI.</p>
    <form method="post" action="./" class="inline">
      <input type="hidden" name="action" value="album-add">
      <input type="hidden" name="back" value="albums">
      <input type="hidden" name="csrf" value="<?= e($csrf) ?>">
      <label for="new-album" class="sr">Naam van het nieuwe album</label>
      <input type="text" id="new-album" name="title" placeholder="Naam van het nieuwe album" maxlength="<?= Site::TITLE_MAX ?>" required>
      <button type="submit" class="btn">Album maken</button>
    </form>
  </section>
  <?php foreach ($data['albums'] as $album):
      $count = count(array_filter($data['photos'], static fn(array $p): bool => $p['album'] === $album['id'])); ?>
  <section class="card">
    <form method="post" action="./" class="stack">
      <input type="hidden" name="action" value="album-save">
      <input type="hidden" name="back" value="albums">
      <input type="hidden" name="csrf" value="<?= e($csrf) ?>">
      <input type="hidden" name="id" value="<?= e($album['id']) ?>">
      <label for="a-<?= e($album['id']) ?>">Naam <span class="muted">(<?= $count ?> <?= $count === 1 ? 'beeld' : 'beelden' ?>)</span></label>
      <input type="text" id="a-<?= e($album['id']) ?>" name="title" value="<?= e($album['title']) ?>" maxlength="<?= Site::TITLE_MAX ?>" required>
      <label for="d-<?= e($album['id']) ?>">Omschrijving <span class="muted">(niet verplicht)</span></label>
      <textarea id="d-<?= e($album['id']) ?>" name="description" maxlength="<?= Site::CAPTION_MAX ?>" rows="2"><?= e($album['description']) ?></textarea>
      <button type="submit" class="btn">Opslaan</button>
    </form>
    <form method="post" action="./" onsubmit="return confirm('Het album &quot;<?= e(addslashes($album['title'])) ?>&quot; verwijderen? De beelden blijven op de site.');">
      <input type="hidden" name="action" value="album-delete">
      <input type="hidden" name="csrf" value="<?= e($csrf) ?>">
      <input type="hidden" name="id" value="<?= e($album['id']) ?>">
      <button type="submit" class="linkish danger-text">Album verwijderen</button>
    </form>
  </section>
  <?php endforeach; ?>

<?php elseif ($page === 'gegevens'): ?>
  <section class="card">
    <h1>Gegevens</h1>
    <form method="post" action="./" class="stack">
      <input type="hidden" name="action" value="settings-save">
      <input type="hidden" name="back" value="gegevens">
      <input type="hidden" name="csrf" value="<?= e($csrf) ?>">
      <label for="name">Naam bovenaan de site</label>
      <input type="text" id="name" name="name" value="<?= e($settings['name']) ?>" maxlength="60" required>
      <label for="tagline">Regel onder de naam</label>
      <input type="text" id="tagline" name="tagline" value="<?= e($settings['tagline']) ?>" maxlength="60">
      <label for="about">Tekst op de pagina Over <span class="muted">(een witregel begint een nieuwe alinea)</span></label>
      <textarea id="about" name="about" rows="6" maxlength="4000"><?= e($settings['about']) ?></textarea>
      <label for="email">E-mailadres op de site <span class="muted">(niet verplicht)</span></label>
      <input type="email" id="email" name="email" value="<?= e($settings['email']) ?>" autocomplete="email">
      <label for="instagram">Instagram-naam <span class="muted">(niet verplicht, zonder @)</span></label>
      <input type="text" id="instagram" name="instagram" value="<?= e($settings['instagram']) ?>" maxlength="30">
      <label class="check"><input type="checkbox" name="indexable" value="1"<?= $settings['indexable'] ? ' checked' : '' ?>> Zichtbaar in zoekmachines zoals Google</label>
      <button type="submit" class="btn">Opslaan</button>
    </form>
  </section>
  <section class="card">
    <h2>Wachtwoord veranderen</h2>
    <form method="post" action="./" class="stack">
      <input type="hidden" name="action" value="password-change">
      <input type="hidden" name="back" value="gegevens">
      <input type="hidden" name="csrf" value="<?= e($csrf) ?>">
      <label for="current">Huidig wachtwoord</label>
      <input type="password" id="current" name="current" autocomplete="current-password" required>
      <label for="new">Nieuw wachtwoord <span class="muted">(minstens 8 tekens)</span></label>
      <input type="password" id="new" name="new" autocomplete="new-password" minlength="8" required>
      <label for="repeat">Nieuw wachtwoord, nog een keer</label>
      <input type="password" id="repeat" name="repeat" autocomplete="new-password" minlength="8" required>
      <button type="submit" class="btn">Wachtwoord veranderen</button>
    </form>
  </section>
  <section class="card">
    <h2>Server</h2>
    <?php
    $checks = [
        ['PHP-versie', PHP_VERSION, version_compare(PHP_VERSION, '8.1', '>=')],
        ['GD (beelden verkleinen)', extension_loaded('gd') ? 'aanwezig' : 'ontbreekt', extension_loaded('gd')],
        ['Kopieën als', Images::format() === 'webp' ? 'WebP' : 'JPEG (geen WebP in GD)', true],
        ['EXIF (draaien, jaar)', function_exists('exif_read_data') ? 'aanwezig' : 'ontbreekt: staande foto\'s kunnen liggend uitkomen', function_exists('exif_read_data')],
        ['mbstring', extension_loaded('mbstring') ? 'aanwezig' : 'ontbreekt', extension_loaded('mbstring')],
        ['Grootste upload', megabytes(upload_limit()), upload_limit() >= 20 * 1024 * 1024],
        ['Geheugen voor PHP', (string) ini_get('memory_limit'), ini_bytes('memory_limit') < 0 || ini_bytes('memory_limit') >= 256 * 1024 * 1024],
        ['Map data beschrijfbaar', is_writable($dataDir) ? 'ja' : 'nee', is_writable($dataDir)],
        ['Map media beschrijfbaar', is_dir($mediaDir) && is_writable($mediaDir) ? 'ja' : 'nee', is_dir($mediaDir) && is_writable($mediaDir)],
    ];
    ?>
    <table class="checks">
      <?php foreach ($checks as [$label, $value, $ok]): ?>
      <tr><th scope="row"><?= e($label) ?></th><td class="<?= $ok ? 'ok' : 'bad' ?>"><?= $ok ? '✓' : '✗' ?> <?= e($value) ?></td></tr>
      <?php endforeach; ?>
    </table>
  </section>
<?php endif; ?>
</main>
<script src="../assets/beheer.js"></script>
</body>
</html>
