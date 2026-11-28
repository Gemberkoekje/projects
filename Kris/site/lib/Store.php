<?php
// Everything the site knows, in one JSON file (data/site.json): the pictures, the albums and the
// settings. One writer at a time (flock), and every write goes to a temporary file first and is
// then renamed over the old one, so a crash halfway never leaves a broken site.json behind.
declare(strict_types=1);

final class Store
{
    private string $file;
    private string $lock;

    public function __construct(private string $dir)
    {
        self::ensureDir($dir);
        $this->file = $dir . '/site.json';
        $this->lock = $dir . '/site.lock';
    }

    public static function ensureDir(string $dir): void
    {
        if (!is_dir($dir) && !mkdir($dir, 0775, true) && !is_dir($dir)) {
            throw new RuntimeException("Kan de map $dir niet maken.");
        }
    }

    public static function empty(): array
    {
        return [
            'version' => 1,
            'photos' => [],
            'albums' => [],
            'settings' => [
                'name' => 'Kristoffer Zegers',
                'tagline' => 'Beelden',
                'about' => '',
                'email' => '',
                'instagram' => '',
                'featured' => null,
                'indexable' => false,
            ],
        ];
    }

    public function read(): array
    {
        if (!is_file($this->file)) {
            return self::empty();
        }
        $data = json_decode((string) file_get_contents($this->file), true);
        if (!is_array($data)) {
            throw new RuntimeException('data/site.json is onleesbaar.');
        }
        $empty = self::empty();
        $data['photos'] = array_values($data['photos'] ?? []);
        $data['albums'] = array_values($data['albums'] ?? []);
        $data['settings'] = array_merge($empty['settings'], $data['settings'] ?? []);
        // The first version's default line under the name; the site no longer splits photos and AI images.
        if ($data['settings']['tagline'] === 'Foto & AI') {
            $data['settings']['tagline'] = $empty['settings']['tagline'];
        }
        return $data + $empty;
    }

    /**
     * Reads, changes and writes site.json while holding the lock.
     *
     * @param callable(array): array $change
     */
    public function update(callable $change): array
    {
        $handle = fopen($this->lock, 'c');
        if ($handle === false) {
            throw new RuntimeException('Kan data/site.lock niet openen.');
        }
        try {
            flock($handle, LOCK_EX);
            $data = $change($this->read());
            $temp = $this->file . '.' . bin2hex(random_bytes(4)) . '.tmp';
            $json = json_encode($data, JSON_PRETTY_PRINT | JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR);
            if (file_put_contents($temp, $json . "\n") === false || !rename($temp, $this->file)) {
                @unlink($temp);
                throw new RuntimeException('Kan data/site.json niet opslaan.');
            }
            return $data;
        } finally {
            flock($handle, LOCK_UN);
            fclose($handle);
        }
    }
}
