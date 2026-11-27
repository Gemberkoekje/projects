<?php
// Turns an uploaded picture into what the site shows. The original is kept byte for byte, but in
// data/originals/, which isn't public: a phone photo carries its GPS position in its metadata. The
// site shows two copies made with GD, without any metadata: "large" (2048 px on the long side) for
// the viewer and "thumb" (640 px) for the overviews, as WebP where the server's GD can write it,
// otherwise JPEG.
declare(strict_types=1);

final class Images
{
    public const LARGE = 2048;
    public const THUMB = 640;
    /** GD keeps every pixel in memory (about 5 bytes each): 60 megapixels is about 300 MB. */
    public const MAX_PIXELS = 60_000_000;

    private const TYPES = [IMAGETYPE_JPEG => 'jpg', IMAGETYPE_PNG => 'png', IMAGETYPE_WEBP => 'webp'];

    /** The format the copies are written in: WebP if this server's GD can, else JPEG. */
    public static function format(): string
    {
        return function_exists('imagewebp') && !empty(gd_info()['WebP Support']) ? 'webp' : 'jpg';
    }

    /**
     * Checks the file, writes the two copies to $mediaDir and keeps the original in $originalsDir.
     *
     * @return array{ext: string, format: string, w: int, h: int, bytes: int, year: ?int}
     */
    public static function store(string $source, string $id, string $originalsDir, string $mediaDir, bool $uploaded): array
    {
        $info = @getimagesize($source);
        if ($info === false || !isset(self::TYPES[$info[2]])) {
            throw new UserError(self::isHeic($source)
                ? 'Dit is een HEIC-bestand (de "hoge efficiëntie"-stand van iPhone of Samsung). Dat kan de site niet lezen: zet de camera op JPEG of "Meest compatibel", of exporteer het beeld als JPEG.'
                : 'Dit bestand is geen JPEG, PNG of WebP, dus de site kan het niet tonen.');
        }
        [$width, $height, $type] = $info;
        if ($width * $height > self::MAX_PIXELS) {
            throw new UserError('Dit beeld is te groot (' . round($width * $height / 1e6) . ' megapixel). Maak het kleiner dan 60 megapixel.');
        }
        self::raiseMemoryLimit();

        $orientation = 1;
        $year = null;
        if ($type === IMAGETYPE_JPEG && function_exists('exif_read_data')) {
            $exif = @exif_read_data($source);
            if (is_array($exif)) {
                $orientation = (int) ($exif['Orientation'] ?? 1);
                $year = self::exifYear($exif['DateTimeOriginal'] ?? $exif['DateTime'] ?? null);
            }
        }

        $image = match ($type) {
            IMAGETYPE_JPEG => @imagecreatefromjpeg($source),
            IMAGETYPE_PNG => @imagecreatefrompng($source),
            IMAGETYPE_WEBP => @imagecreatefromwebp($source),
        };
        if ($image === false) {
            throw new UserError('Dit beeld kon niet worden gelezen. Is het bestand misschien beschadigd?');
        }
        $image = self::orient($image, $orientation);

        Store::ensureDir($mediaDir);
        Store::ensureDir($originalsDir);
        $format = self::format();
        $written = [];
        try {
            foreach (['large' => self::LARGE, 'thumb' => self::THUMB] as $name => $max) {
                $path = "$mediaDir/$id-$name.$format";
                self::writeCopy($image, $max, $format, $path);
                $written[] = $path;
            }
            $ext = self::TYPES[$type];
            $original = "$originalsDir/$id.$ext";
            $kept = $uploaded ? move_uploaded_file($source, $original) : copy($source, $original);
            if (!$kept) {
                throw new RuntimeException('Kan het origineel niet bewaren in ' . $originalsDir);
            }
        } catch (Throwable $error) {
            array_map('unlink', array_filter($written, 'is_file'));
            throw $error;
        }
        $result = [
            'ext' => $ext,
            'format' => $format,
            'w' => imagesx($image),
            'h' => imagesy($image),
            'bytes' => (int) filesize($original),
            'year' => $year,
        ];
        imagedestroy($image);
        return $result;
    }

    /** Removes a picture's copies and its original. */
    public static function delete(array $photo, string $originalsDir, string $mediaDir): void
    {
        foreach (["$mediaDir/{$photo['id']}-large.{$photo['format']}", "$mediaDir/{$photo['id']}-thumb.{$photo['format']}", "$originalsDir/{$photo['id']}.{$photo['ext']}"] as $path) {
            if (is_file($path)) {
                unlink($path);
            }
        }
    }

    /** Turns the pixels the way the camera's EXIF Orientation tag says (1 to 8). */
    public static function orient(\GdImage $image, int $orientation): \GdImage
    {
        if ($orientation === 3) {
            $image = imagerotate($image, 180, 0);
        } elseif (in_array($orientation, [5, 6], true)) {
            $image = imagerotate($image, -90, 0);
        } elseif (in_array($orientation, [7, 8], true)) {
            $image = imagerotate($image, 90, 0);
        }
        if (in_array($orientation, [2, 5, 7], true)) {
            imageflip($image, IMG_FLIP_HORIZONTAL);
        } elseif ($orientation === 4) {
            imageflip($image, IMG_FLIP_VERTICAL);
        }
        return $image;
    }

    private static function writeCopy(\GdImage $image, int $max, string $format, string $path): void
    {
        $width = imagesx($image);
        $height = imagesy($image);
        $scale = min(1, $max / max($width, $height));
        $w = max(1, (int) round($width * $scale));
        $h = max(1, (int) round($height * $scale));
        $copy = imagecreatetruecolor($w, $h);
        if ($format === 'webp') {
            imagealphablending($copy, false);
            imagesavealpha($copy, true);
            imagefill($copy, 0, 0, imagecolorallocatealpha($copy, 0, 0, 0, 127));
        } else {
            imagefill($copy, 0, 0, imagecolorallocate($copy, 255, 255, 255));
        }
        imagecopyresampled($copy, $image, 0, 0, 0, 0, $w, $h, $width, $height);
        $temp = "$path.tmp";
        $ok = $format === 'webp' ? imagewebp($copy, $temp, $max === self::THUMB ? 78 : 84) : imagejpeg($copy, $temp, 86);
        imagedestroy($copy);
        if (!$ok || !rename($temp, $path)) {
            @unlink($temp);
            throw new RuntimeException("Kan $path niet schrijven.");
        }
    }

    private static function exifYear(mixed $value): ?int
    {
        if (!is_string($value) || !preg_match('/^(\d{4}):\d{2}:\d{2}/', $value, $m)) {
            return null;
        }
        $year = (int) $m[1];
        return $year >= 1900 && $year <= (int) date('Y') + 1 ? $year : null;
    }

    private static function isHeic(string $path): bool
    {
        $head = (string) @file_get_contents($path, false, null, 0, 16);
        return (bool) preg_match('/^.{4}ftyp(heic|heix|hevc|heim|heis|mif1|msf1)/s', $head);
    }

    private static function raiseMemoryLimit(): void
    {
        $limit = ini_bytes('memory_limit');
        if ($limit > 0 && $limit < 512 * 1024 * 1024) {
            @ini_set('memory_limit', '512M');
        }
    }
}
