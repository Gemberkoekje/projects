<?php
// The beheer's one password. It comes from config.php ("password" or "password_hash"), or, once
// Kristoffer has changed it in the beheer, from data/password.json (a password_hash), which then wins.
// After 10 wrong tries from one address in 15 minutes, that address waits. A sign-in lasts 30 days
// and ends when the password changes. Every form carries a CSRF token.
declare(strict_types=1);

final class Auth
{
    public const MAX_FAILS = 10;
    public const WINDOW = 900;
    public const LIFETIME = 30 * 24 * 3600;

    public function __construct(private array $config, private string $dataDir)
    {
    }

    private function storedHash(): ?string
    {
        $file = $this->dataDir . '/password.json';
        if (!is_file($file)) {
            return null;
        }
        $data = json_decode((string) file_get_contents($file), true);
        return is_array($data) && is_string($data['hash'] ?? null) ? $data['hash'] : null;
    }

    public function configured(): bool
    {
        return $this->storedHash() !== null || $this->config['password_hash'] !== '' || $this->config['password'] !== '';
    }

    public function check(string $given): bool
    {
        $hash = $this->storedHash() ?? ($this->config['password_hash'] !== '' ? $this->config['password_hash'] : null);
        if ($hash !== null) {
            return password_verify($given, $hash);
        }
        return $this->config['password'] !== '' && hash_equals(hash('sha256', $this->config['password']), hash('sha256', $given));
    }

    /** Stores a new password (as a hash) in data/password.json. */
    public function changePassword(string $current, string $new): void
    {
        if (!$this->check($current)) {
            throw new UserError('Je huidige wachtwoord klopt niet.');
        }
        if (mb_strlen($new) < 8) {
            throw new UserError('Kies een wachtwoord van minstens 8 tekens.');
        }
        $json = json_encode(['hash' => password_hash($new, PASSWORD_DEFAULT)], JSON_THROW_ON_ERROR);
        if (file_put_contents($this->dataDir . '/password.json', $json, LOCK_EX) === false) {
            throw new RuntimeException('Kan het nieuwe wachtwoord niet opslaan.');
        }
        $_SESSION['who'] = $this->fingerprint();
    }

    /** Changes when the password changes, so old sign-ins end. */
    private function fingerprint(): string
    {
        $secret = $this->storedHash() ?? ($this->config['password_hash'] !== '' ? $this->config['password_hash'] : $this->config['password']);
        return hash('sha256', 'kris-beheer|' . $secret);
    }

    // ---------- Rate limit ----------

    private function attemptsFile(): string
    {
        $dir = $this->dataDir . '/attempts';
        Store::ensureDir($dir);
        return $dir . '/' . hash('sha256', (string) ($_SERVER['REMOTE_ADDR'] ?? 'unknown')) . '.json';
    }

    /** @return list<int> the times of recent wrong tries */
    private function recentFails(): array
    {
        $file = $this->attemptsFile();
        $times = is_file($file) ? json_decode((string) file_get_contents($file), true) : [];
        $since = time() - self::WINDOW;
        return array_values(array_filter(is_array($times) ? $times : [], static fn($t): bool => is_int($t) && $t > $since));
    }

    public function blocked(): bool
    {
        return count($this->recentFails()) >= self::MAX_FAILS;
    }

    public function recordFailure(): void
    {
        $times = $this->recentFails();
        $times[] = time();
        file_put_contents($this->attemptsFile(), json_encode($times), LOCK_EX);
    }

    public function clearFailures(): void
    {
        @unlink($this->attemptsFile());
    }

    // ---------- Session ----------

    public static function isHttps(): bool
    {
        return (!empty($_SERVER['HTTPS']) && $_SERVER['HTTPS'] !== 'off') || (int) ($_SERVER['SERVER_PORT'] ?? 0) === 443;
    }

    public function startSession(): void
    {
        if (session_status() === PHP_SESSION_ACTIVE) {
            return;
        }
        ini_set('session.use_strict_mode', '1');
        ini_set('session.gc_maxlifetime', (string) self::LIFETIME);
        session_name('kris_beheer');
        session_set_cookie_params([
            'lifetime' => self::LIFETIME,
            'path' => self::cookiePath(),
            'secure' => self::isHttps(),
            'httponly' => true,
            'samesite' => 'Lax',
        ]);
        session_start();
    }

    /** The folder the beheer lives in, so the cookie stays with it (the site may sit in a subfolder). */
    private static function cookiePath(): string
    {
        $path = dirname((string) ($_SERVER['SCRIPT_NAME'] ?? '/beheer/index.php'));
        return rtrim(str_replace('\\', '/', $path), '/') . '/';
    }

    public function loggedIn(): bool
    {
        return ($_SESSION['who'] ?? null) === $this->fingerprint();
    }

    public function login(): void
    {
        session_regenerate_id(true);
        $_SESSION['who'] = $this->fingerprint();
        $_SESSION['csrf'] = bin2hex(random_bytes(16));
    }

    public function logout(): void
    {
        $_SESSION = [];
        session_destroy();
        setcookie('kris_beheer', '', ['expires' => time() - 3600, 'path' => self::cookiePath(), 'secure' => self::isHttps(), 'httponly' => true, 'samesite' => 'Lax']);
    }

    public function csrf(): string
    {
        if (empty($_SESSION['csrf'])) {
            $_SESSION['csrf'] = bin2hex(random_bytes(16));
        }
        return $_SESSION['csrf'];
    }

    public function validCsrf(?string $token): bool
    {
        return is_string($token) && !empty($_SESSION['csrf']) && hash_equals($_SESSION['csrf'], $token);
    }
}
