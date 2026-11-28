<?php
// The rules for the site's data, as pure functions on the array from site.json: they take the
// data and the user's input, check it, and return the new data. Store::update() writes it.
declare(strict_types=1);

final class Site
{
    // There is one kind of picture: a "beeld". Kristoffer leaves it to the visitor whether it is a photo
    // or made with AI. Pictures uploaded before that still carry a "kind" field in site.json; nothing
    // reads it any more.
    public const TITLE_MAX = 120;
    public const CAPTION_MAX = 500;

    public static function newId(): string
    {
        return bin2hex(random_bytes(12));
    }

    /** A title from a file name: "Heaven_s-Backdoor.jpg" becomes "Heaven s Backdoor". */
    public static function titleFromFilename(string $name): string
    {
        $base = pathinfo($name, PATHINFO_FILENAME);
        $title = trim((string) preg_replace('/[\s_\-]+/u', ' ', $base));
        return $title === '' ? 'Zonder titel' : mb_substr($title, 0, self::TITLE_MAX);
    }

    public static function addPhoto(array $data, array $photo): array
    {
        array_unshift($data['photos'], $photo);
        return $data;
    }

    public static function findPhoto(array $data, string $id): ?array
    {
        foreach ($data['photos'] as $photo) {
            if ($photo['id'] === $id) {
                return $photo;
            }
        }
        return null;
    }

    public static function findAlbum(array $data, string $id): ?array
    {
        foreach ($data['albums'] as $album) {
            if ($album['id'] === $id) {
                return $album;
            }
        }
        return null;
    }

    /** The checked fields of a picture: title, caption, album, year. */
    public static function photoFields(array $data, array $input): array
    {
        $title = trim((string) ($input['title'] ?? ''));
        if ($title === '') {
            throw new UserError('Geef het beeld een titel.');
        }
        if (mb_strlen($title) > self::TITLE_MAX) {
            throw new UserError('De titel mag hoogstens ' . self::TITLE_MAX . ' tekens zijn.');
        }
        $caption = trim((string) ($input['caption'] ?? ''));
        if (mb_strlen($caption) > self::CAPTION_MAX) {
            throw new UserError('De omschrijving mag hoogstens ' . self::CAPTION_MAX . ' tekens zijn.');
        }
        $album = (string) ($input['album'] ?? '');
        if ($album !== '' && self::findAlbum($data, $album) === null) {
            throw new UserError('Dat album bestaat niet meer.');
        }
        $year = (int) ($input['year'] ?? date('Y'));
        if ($year < 1900 || $year > (int) date('Y') + 1) {
            throw new UserError('Het jaar klopt niet.');
        }
        return [
            'title' => $title,
            'caption' => $caption,
            'album' => $album === '' ? null : $album,
            'year' => $year,
        ];
    }

    public static function updatePhoto(array $data, string $id, array $input): array
    {
        $fields = self::photoFields($data, $input);
        $found = false;
        foreach ($data['photos'] as $i => $photo) {
            if ($photo['id'] === $id) {
                $data['photos'][$i] = array_merge($photo, $fields);
                $found = true;
            }
        }
        if (!$found) {
            throw new UserError('Dat beeld bestaat niet meer.');
        }
        if (!empty($input['featured'])) {
            $data['settings']['featured'] = $id;
        } elseif ($data['settings']['featured'] === $id) {
            $data['settings']['featured'] = null;
        }
        return $data;
    }

    public static function deletePhoto(array $data, string $id): array
    {
        $data['photos'] = array_values(array_filter($data['photos'], static fn(array $p): bool => $p['id'] !== $id));
        if ($data['settings']['featured'] === $id) {
            $data['settings']['featured'] = null;
        }
        return $data;
    }

    /**
     * The album with this title (any case), made if there isn't one yet.
     *
     * @return array{0: array, 1: string} the data and the album's id
     */
    public static function albumNamed(array $data, string $title): array
    {
        $title = trim($title);
        if ($title === '') {
            throw new UserError('Geef het album een naam.');
        }
        if (mb_strlen($title) > self::TITLE_MAX) {
            throw new UserError('De naam van het album mag hoogstens ' . self::TITLE_MAX . ' tekens zijn.');
        }
        foreach ($data['albums'] as $album) {
            if (mb_strtolower($album['title']) === mb_strtolower($title)) {
                return [$data, $album['id']];
            }
        }
        $id = self::newId();
        $data['albums'][] = ['id' => $id, 'title' => $title, 'description' => ''];
        return [$data, $id];
    }

    public static function updateAlbum(array $data, string $id, array $input): array
    {
        $title = trim((string) ($input['title'] ?? ''));
        if ($title === '') {
            throw new UserError('Geef het album een naam.');
        }
        $description = trim((string) ($input['description'] ?? ''));
        if (mb_strlen($title) > self::TITLE_MAX || mb_strlen($description) > self::CAPTION_MAX) {
            throw new UserError('De naam of de omschrijving van het album is te lang.');
        }
        foreach ($data['albums'] as $album) {
            if ($album['id'] !== $id && mb_strtolower($album['title']) === mb_strtolower($title)) {
                throw new UserError('Er is al een album met die naam.');
            }
        }
        foreach ($data['albums'] as $i => $album) {
            if ($album['id'] === $id) {
                $data['albums'][$i]['title'] = $title;
                $data['albums'][$i]['description'] = $description;
                return $data;
            }
        }
        throw new UserError('Dat album bestaat niet meer.');
    }

    /** Removes the album; its pictures stay on the site, without an album. */
    public static function deleteAlbum(array $data, string $id): array
    {
        $data['albums'] = array_values(array_filter($data['albums'], static fn(array $a): bool => $a['id'] !== $id));
        foreach ($data['photos'] as $i => $photo) {
            if ($photo['album'] === $id) {
                $data['photos'][$i]['album'] = null;
            }
        }
        return $data;
    }

    public static function updateSettings(array $data, array $input): array
    {
        $name = trim((string) ($input['name'] ?? ''));
        if ($name === '') {
            throw new UserError('De naam op de site mag niet leeg zijn.');
        }
        $email = trim((string) ($input['email'] ?? ''));
        if ($email !== '' && filter_var($email, FILTER_VALIDATE_EMAIL) === false) {
            throw new UserError('Dat e-mailadres klopt niet.');
        }
        $instagram = ltrim(trim((string) ($input['instagram'] ?? '')), '@');
        if ($instagram !== '' && !preg_match('/^[A-Za-z0-9._]{1,30}$/', $instagram)) {
            throw new UserError('Een Instagram-naam bestaat alleen uit letters, cijfers, punten en lage streepjes.');
        }
        $data['settings'] = array_merge($data['settings'], [
            'name' => mb_substr($name, 0, 60),
            'tagline' => mb_substr(trim((string) ($input['tagline'] ?? '')), 0, 60),
            'about' => mb_substr(trim((string) ($input['about'] ?? '')), 0, 4000),
            'email' => $email,
            'instagram' => $instagram,
            'indexable' => !empty($input['indexable']),
        ]);
        return $data;
    }

    /** What the public site gets: no original file names, sizes or upload times. */
    public static function publicView(array $data, string $mediaUrl): array
    {
        $photos = array_map(static fn(array $p): array => [
            'id' => $p['id'],
            'title' => $p['title'],
            'caption' => $p['caption'],
            'album' => $p['album'],
            'year' => $p['year'],
            'w' => $p['w'],
            'h' => $p['h'],
            'thumb' => "$mediaUrl/{$p['id']}-thumb.{$p['format']}",
            'large' => "$mediaUrl/{$p['id']}-large.{$p['format']}",
        ], $data['photos']);
        $settings = $data['settings'];
        return [
            'photos' => $photos,
            'albums' => array_map(static fn(array $a): array => [
                'id' => $a['id'], 'title' => $a['title'], 'description' => $a['description'],
            ], $data['albums']),
            'featured' => $settings['featured'],
            'name' => $settings['name'],
            'tagline' => $settings['tagline'],
        ];
    }
}
