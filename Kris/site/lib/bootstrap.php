<?php
// Shared setup for the site and the beheer: configuration, the classes, small helpers.
// Plain PHP 8.1+, no Composer, no database: it has to run on ordinary shared hosting.
declare(strict_types=1);

define('KRIS_ROOT', dirname(__DIR__));

require __DIR__ . '/Store.php';
require __DIR__ . '/Site.php';
require __DIR__ . '/Images.php';
require __DIR__ . '/Auth.php';

/**
 * The configuration: config.php next to index.php (copied from config.example.php), with the
 * environment variables KRIS_DATA_DIR, KRIS_MEDIA_DIR and BEHEER_PASSWORD on top (used by the tests
 * and by a local run in Docker).
 */
function kris_config(): array
{
    static $config = null;
    if ($config !== null) {
        return $config;
    }
    $file = [];
    if (is_file(KRIS_ROOT . '/config.php')) {
        $loaded = require KRIS_ROOT . '/config.php';
        $file = is_array($loaded) ? $loaded : [];
    }
    $env = static function (string $name): ?string {
        $value = getenv($name);
        return $value === false || $value === '' ? null : $value;
    };
    $config = [
        'data_dir' => rtrim($env('KRIS_DATA_DIR') ?? (string) ($file['data_dir'] ?? KRIS_ROOT . '/data'), '/'),
        'media_dir' => rtrim($env('KRIS_MEDIA_DIR') ?? KRIS_ROOT . '/media', '/'),
        'password' => $env('BEHEER_PASSWORD') ?? (string) ($file['password'] ?? ''),
        'password_hash' => (string) ($file['password_hash'] ?? ''),
    ];
    return $config;
}

function e(?string $text): string
{
    return htmlspecialchars((string) $text, ENT_QUOTES | ENT_SUBSTITUTE, 'UTF-8');
}

/** JSON that is safe inside a <script> element. */
function json_for_script(mixed $value): string
{
    return json_encode(
        $value,
        JSON_HEX_TAG | JSON_HEX_AMP | JSON_HEX_APOS | JSON_HEX_QUOT | JSON_UNESCAPED_UNICODE | JSON_THROW_ON_ERROR
    );
}

/** A size from php.ini ("64M") in bytes. */
function ini_bytes(string $key): int
{
    $value = trim((string) ini_get($key));
    if ($value === '') {
        return 0;
    }
    $number = (int) $value;
    return match (strtolower(substr($value, -1))) {
        'g' => $number * 1024 ** 3,
        'm' => $number * 1024 ** 2,
        'k' => $number * 1024,
        default => $number,
    };
}

/** The largest file the server takes in one upload, in bytes. */
function upload_limit(): int
{
    $limits = array_filter([ini_bytes('upload_max_filesize'), ini_bytes('post_max_size')]);
    return $limits ? min($limits) : 0;
}

function megabytes(int $bytes): string
{
    return number_format($bytes / 1024 / 1024, $bytes < 10 * 1024 * 1024 ? 1 : 0, ',', '.') . ' MB';
}

/** A message the user should see as is (a bad file, a missing field), not a crash. */
final class UserError extends RuntimeException
{
}
