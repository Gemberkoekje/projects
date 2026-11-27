<?php
// The site's tests, without PHPUnit, so they run wherever PHP runs:  php Kris/tests/run.php
// Each test gets a fresh temporary data and media folder.
declare(strict_types=1);

putenv('KRIS_DATA_DIR=');
putenv('KRIS_MEDIA_DIR=');
require dirname(__DIR__) . '/site/lib/bootstrap.php';

$tests = [];
$failures = 0;

function test(string $name, callable $body): void
{
    $GLOBALS['tests'][$name] = $body;
}

function check(bool $condition, string $what): void
{
    if (!$condition) {
        throw new RuntimeException("expected: $what");
    }
}

function same(mixed $expected, mixed $actual, string $what): void
{
    if ($expected !== $actual) {
        throw new RuntimeException("$what: expected " . var_export($expected, true) . ', got ' . var_export($actual, true));
    }
}

function throws(callable $body, string $class, string $what): string
{
    try {
        $body();
    } catch (Throwable $e) {
        if ($e instanceof $class) {
            return $e->getMessage();
        }
        throw new RuntimeException("$what: expected $class, got " . get_class($e) . ': ' . $e->getMessage());
    }
    throw new RuntimeException("$what: expected $class, nothing was thrown");
}

function temp_dir(): string
{
    $dir = sys_get_temp_dir() . '/kris-test-' . bin2hex(random_bytes(4));
    mkdir($dir, 0775, true);
    return $dir;
}

function photo(string $id, string $kind = 'foto', ?string $album = null): array
{
    return ['id' => $id, 'title' => "Beeld $id", 'caption' => '', 'kind' => $kind, 'album' => $album, 'year' => 2026,
        'w' => 100, 'h' => 80, 'ext' => 'jpg', 'format' => 'webp', 'bytes' => 1234, 'original_name' => "$id.jpg", 'uploaded' => '2026-11-27T22:00:00+01:00'];
}

/** The colour of a pixel as [r, g, b]. */
function rgb(\GdImage $image, int $x, int $y): array
{
    $c = imagecolorat($image, $x, $y);
    return [($c >> 16) & 0xFF, ($c >> 8) & 0xFF, $c & 0xFF];
}

function open_image(string $path): \GdImage
{
    $image = str_ends_with($path, '.webp') ? imagecreatefromwebp($path) : imagecreatefromjpeg($path);
    check($image !== false, "$path opens");
    return $image;
}

// ---------- Store ----------

test('a new store reads as empty, with the default name', function (): void {
    $store = new Store(temp_dir());
    $data = $store->read();
    same([], $data['photos'], 'photos');
    same('Kristoffer Zegers', $data['settings']['name'], 'name');
});

test('update writes site.json and the next read sees it, with no temporary file left', function (): void {
    $dir = temp_dir();
    $store = new Store($dir);
    $store->update(fn(array $d): array => Site::addPhoto($d, photo('a')));
    same('a', $store->read()['photos'][0]['id'], 'stored photo');
    same([], glob("$dir/*.tmp"), 'temporary files');
});

test('settings added in a later version get their defaults when an old site.json is read', function (): void {
    $dir = temp_dir();
    file_put_contents("$dir/site.json", json_encode(['photos' => [], 'albums' => [], 'settings' => ['name' => 'Kris']]));
    $data = (new Store($dir))->read();
    same('Kris', $data['settings']['name'], 'kept name');
    same(false, $data['settings']['indexable'], 'default indexable');
});

// ---------- Site ----------

test('the newest picture comes first', function (): void {
    $data = Site::addPhoto(Site::addPhoto(Store::empty(), photo('old')), photo('new'));
    same(['new', 'old'], array_column($data['photos'], 'id'), 'order');
});

test('a picture must be a photo or an AI image, and needs a title', function (): void {
    $data = Store::empty();
    throws(fn() => Site::photoFields($data, ['title' => 'x', 'kind' => '']), UserError::class, 'no kind');
    throws(fn() => Site::photoFields($data, ['title' => 'x', 'kind' => 'video']), UserError::class, 'other kind');
    throws(fn() => Site::photoFields($data, ['title' => '   ', 'kind' => 'ai']), UserError::class, 'empty title');
    $fields = Site::photoFields($data, ['title' => '  Heaven\'s Backdoor ', 'kind' => 'ai', 'year' => '2025']);
    same("Heaven's Backdoor", $fields['title'], 'trimmed title');
    same(2025, $fields['year'], 'year');
    same(null, $fields['album'], 'no album');
});

test('a picture cannot point at an album that does not exist', function (): void {
    throws(fn() => Site::photoFields(Store::empty(), ['title' => 'x', 'kind' => 'foto', 'album' => 'nope']), UserError::class, 'unknown album');
});

test('titles from file names lose the extension and the underscores', function (): void {
    same('Gravity s Mercy', Site::titleFromFilename('Gravity_s-Mercy.JPG'), 'title');
    same('Zonder titel', Site::titleFromFilename('.jpg'), 'empty name');
});

test('updating a picture changes its fields and can put it on the front page, and take it off', function (): void {
    $data = Site::addPhoto(Store::empty(), photo('a'));
    $data = Site::updatePhoto($data, 'a', ['title' => 'Zevenbergen', 'caption' => 'Muur', 'kind' => 'foto', 'year' => 2024, 'featured' => '1']);
    same('Zevenbergen', $data['photos'][0]['title'], 'title');
    same('a', $data['settings']['featured'], 'featured');
    same('webp', $data['photos'][0]['format'], 'file fields kept');
    $data = Site::updatePhoto($data, 'a', ['title' => 'Zevenbergen', 'kind' => 'foto', 'year' => 2024]);
    same(null, $data['settings']['featured'], 'no longer featured');
});

test('deleting the front-page picture clears the front page', function (): void {
    $data = Site::addPhoto(Store::empty(), photo('a'));
    $data['settings']['featured'] = 'a';
    $data = Site::deletePhoto($data, 'a');
    same([], $data['photos'], 'photos');
    same(null, $data['settings']['featured'], 'featured');
});

test('an album with the same name, in any case, is reused instead of made twice', function (): void {
    [$data, $first] = Site::albumNamed(Store::empty(), 'Industrie');
    [$data, $second] = Site::albumNamed($data, ' industrie ');
    same($first, $second, 'same album');
    same(1, count($data['albums']), 'one album');
});

test('renaming an album to the name of another one is refused', function (): void {
    [$data, $a] = Site::albumNamed(Store::empty(), 'A');
    [$data] = Site::albumNamed($data, 'B');
    throws(fn() => Site::updateAlbum($data, $a, ['title' => 'b']), UserError::class, 'duplicate name');
});

test('deleting an album keeps its pictures, without an album', function (): void {
    [$data, $album] = Site::albumNamed(Store::empty(), 'A');
    $data = Site::addPhoto($data, photo('a', 'ai', $album));
    $data = Site::deleteAlbum($data, $album);
    same([], $data['albums'], 'albums');
    same(null, $data['photos'][0]['album'], 'picture without album');
});

test('settings check the e-mail address and the Instagram name, and drop the @', function (): void {
    $data = Store::empty();
    throws(fn() => Site::updateSettings($data, ['name' => 'K', 'email' => 'geen-adres']), UserError::class, 'bad email');
    throws(fn() => Site::updateSettings($data, ['name' => 'K', 'instagram' => 'met spatie']), UserError::class, 'bad instagram');
    throws(fn() => Site::updateSettings($data, ['name' => ' ']), UserError::class, 'empty name');
    $data = Site::updateSettings($data, ['name' => 'Kristoffer Zegers', 'instagram' => '@kris.zegers', 'email' => 'kris@example.nl', 'indexable' => '1']);
    same('kris.zegers', $data['settings']['instagram'], 'instagram');
    same(true, $data['settings']['indexable'], 'indexable');
});

test('the public view has the media URLs and none of the original file details', function (): void {
    $data = Site::addPhoto(Store::empty(), photo('a', 'ai'));
    $view = Site::publicView($data, 'media');
    same('media/a-thumb.webp', $view['photos'][0]['thumb'], 'thumb url');
    same('media/a-large.webp', $view['photos'][0]['large'], 'large url');
    foreach (['original_name', 'bytes', 'uploaded', 'ext'] as $private) {
        check(!array_key_exists($private, $view['photos'][0]), "$private left out");
    }
});

// ---------- Images ----------

foreach (range(1, 8) as $n) {
    test("EXIF orientation $n comes out upright: red top left, green top right, blue bottom left", function () use ($n): void {
        $data = temp_dir();
        $media = temp_dir();
        $result = Images::store(__DIR__ . "/fixtures/orient-$n.jpg", "o$n", $data, $media, false);
        same([120, 80], [$result['w'], $result['h']], 'upright size');
        $format = Images::format();
        $image = open_image("$media/o$n-large.$format");
        same([120, 80], [imagesx($image), imagesy($image)], 'copy size');
        [$r] = rgb($image, 12, 12);
        [, $g] = rgb($image, 108, 12);
        [, , $b] = rgb($image, 12, 68);
        $white = rgb($image, 108, 68);
        check($r > 170, "red top left (got r=$r)");
        check($g > 150, "green top right (got g=$g)");
        check($b > 170, "blue bottom left (got b=$b)");
        check(min($white) > 200, 'white bottom right');
        same(file_get_contents(__DIR__ . "/fixtures/orient-$n.jpg"), file_get_contents("$data/o$n.jpg"), 'original kept byte for byte');
    });
}

test('the year comes from the camera, when it says', function (): void {
    $result = Images::store(__DIR__ . '/fixtures/orient-6.jpg', 'y', temp_dir(), temp_dir(), false);
    same(2019, $result['year'], 'year from DateTimeOriginal');
    $none = Images::store(__DIR__ . '/fixtures/orient-1.jpg', 'n', temp_dir(), temp_dir(), false);
    same(null, $none['year'], 'no date');
});

test('a big picture gets copies of 2048 and 640 pixels on the long side, without EXIF', function (): void {
    $source = temp_dir() . '/big.png';
    $image = imagecreatetruecolor(3000, 2000);
    imagefill($image, 0, 0, imagecolorallocate($image, 30, 60, 90));
    imagepng($image, $source);
    $media = temp_dir();
    $result = Images::store($source, 'big', temp_dir(), $media, false);
    same('png', $result['ext'], 'original type');
    $format = Images::format();
    $large = getimagesize("$media/big-large.$format");
    $thumb = getimagesize("$media/big-thumb.$format");
    same([2048, 1365], [$large[0], $large[1]], 'large size');
    same([640, 427], [$thumb[0], $thumb[1]], 'thumb size');
    $copy = (string) file_get_contents("$media/big-large.$format");
    check(!str_contains($copy, 'Exif'), 'no EXIF in the copy');
});

test('a small picture is not blown up', function (): void {
    $media = temp_dir();
    Images::store(__DIR__ . '/fixtures/orient-1.jpg', 's', temp_dir(), $media, false);
    $size = getimagesize("$media/s-large." . Images::format());
    same([120, 80], [$size[0], $size[1]], 'kept size');
});

test('a text file is refused, and nothing is written', function (): void {
    $source = temp_dir() . '/note.jpg';
    file_put_contents($source, 'not a picture');
    $media = temp_dir();
    $message = throws(fn() => Images::store($source, 'x', temp_dir(), $media, false), UserError::class, 'text file');
    check(str_contains($message, 'JPEG, PNG of WebP'), 'says which formats work');
    same([], glob("$media/*"), 'no copies');
});

test('a HEIC file gets its own explanation', function (): void {
    $source = temp_dir() . '/phone.heic';
    file_put_contents($source, "\x00\x00\x00\x18ftypheic\x00\x00\x00\x00mif1heic");
    $message = throws(fn() => Images::store($source, 'h', temp_dir(), temp_dir(), false), UserError::class, 'heic');
    check(str_contains($message, 'HEIC'), 'mentions HEIC');
});

test('deleting a picture removes its copies and its original', function (): void {
    $data = temp_dir();
    $media = temp_dir();
    $result = Images::store(__DIR__ . '/fixtures/orient-1.jpg', 'd', $data, $media, false);
    Images::delete(['id' => 'd'] + $result, $data, $media);
    same([], glob("$media/*"), 'copies gone');
    same([], glob("$data/*"), 'original gone');
});

// ---------- Auth ----------

test('a plain password from the config works, and a wrong one does not', function (): void {
    $auth = new Auth(['password' => 'geheim123', 'password_hash' => ''], temp_dir());
    check($auth->configured(), 'configured');
    check($auth->check('geheim123'), 'right password');
    check(!$auth->check('geheim12'), 'wrong password');
    check(!$auth->check(''), 'empty password');
});

test('a password hash from the config works', function (): void {
    $auth = new Auth(['password' => '', 'password_hash' => password_hash('zegers', PASSWORD_DEFAULT)], temp_dir());
    check($auth->check('zegers'), 'right password');
    check(!$auth->check('Zegers'), 'wrong case');
});

test('without any password the beheer counts as not set up, and nothing gets in', function (): void {
    $auth = new Auth(['password' => '', 'password_hash' => ''], temp_dir());
    check(!$auth->configured(), 'not configured');
    check(!$auth->check(''), 'empty password refused');
});

test('a changed password wins over the config, and needs the current one', function (): void {
    $_SESSION = [];
    $dir = temp_dir();
    $auth = new Auth(['password' => 'eerste-wachtwoord', 'password_hash' => ''], $dir);
    throws(fn() => $auth->changePassword('fout', 'tweede-wachtwoord'), UserError::class, 'wrong current');
    throws(fn() => $auth->changePassword('eerste-wachtwoord', 'kort'), UserError::class, 'too short');
    $auth->changePassword('eerste-wachtwoord', 'tweede-wachtwoord');
    check($auth->check('tweede-wachtwoord'), 'new password');
    check(!$auth->check('eerste-wachtwoord'), 'old password no longer');
    check(!str_contains((string) file_get_contents("$dir/password.json"), 'tweede'), 'stored as a hash');
});

test('ten wrong tries block the address for a while, and a good sign-in clears them', function (): void {
    $_SERVER['REMOTE_ADDR'] = '203.0.113.7';
    $auth = new Auth(['password' => 'x', 'password_hash' => ''], temp_dir());
    for ($i = 0; $i < Auth::MAX_FAILS - 1; $i++) {
        $auth->recordFailure();
    }
    check(!$auth->blocked(), 'not blocked after 9');
    $auth->recordFailure();
    check($auth->blocked(), 'blocked after 10');
    $_SERVER['REMOTE_ADDR'] = '203.0.113.8';
    check(!$auth->blocked(), 'another address is not blocked');
    $_SERVER['REMOTE_ADDR'] = '203.0.113.7';
    $auth->clearFailures();
    check(!$auth->blocked(), 'cleared');
});

test('the CSRF token must match', function (): void {
    $_SESSION = [];
    $auth = new Auth(['password' => 'x', 'password_hash' => ''], temp_dir());
    $token = $auth->csrf();
    check($auth->validCsrf($token), 'own token');
    check(!$auth->validCsrf('x' . $token), 'other token');
    check(!$auth->validCsrf(null), 'no token');
});

// ---------- Helpers ----------

test('php.ini sizes are read in bytes', function (): void {
    ini_set('memory_limit', '256M');
    same(256 * 1024 * 1024, ini_bytes('memory_limit'), '256M');
});

test('JSON for a script element cannot close the script', function (): void {
    $json = json_for_script(['t' => '</script><b>']);
    check(!str_contains($json, '</script>'), 'no closing tag');
    same(['t' => '</script><b>'], json_decode($json, true), 'same data');
});

// ---------- Run ----------

foreach ($tests as $name => $body) {
    try {
        $body();
        echo "  ok  $name\n";
    } catch (Throwable $e) {
        $failures++;
        echo "FAIL  $name\n      " . $e->getMessage() . "\n";
    }
}
echo "\n" . count($tests) . ' tests, ' . $failures . " failed (PHP " . PHP_VERSION . ', copies as ' . Images::format() . ")\n";
exit($failures === 0 ? 0 : 1);
