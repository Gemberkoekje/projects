# De site van Kristoffer installeren

De map [`site/`](site/) is de hele website: gewone PHP-bestanden, zonder database. Die zet je met
FTP op Kristoffers eigen hosting. Hij logt daarna in op `/beheer/` en zet er zelf zijn beelden op, op
volle kwaliteit (niet via WhatsApp, dat maakt ze veel kleiner).

## Wat de hosting moet hebben

- **PHP 8.1 of nieuwer**, met **GD** (beelden verkleinen) en **mbstring**. Bijna elk hostingpakket heeft
  dat. **EXIF** is fijn: zonder EXIF kan een staande telefoonfoto liggend uitkomen.
- **Apache** (de meeste pakketten), zodat de `.htaccess`-bestanden werken. Die houden de originelen
  en het wachtwoord privé. Bij een host met nginx: zie stap 6.
- **HTTPS** (bij bijna elke host gratis via Let's Encrypt). Zonder HTTPS gaat het wachtwoord leesbaar
  over het internet.

## Installeren

1. **Zet de inhoud van de map `site/`** in de webmap van de hosting (vaak `public_html`, `www` of
   `htdocs`). Een submap mag ook, bijvoorbeeld `public_html/fotos/`: alle links in de site zijn relatief.
   Neem de verborgen bestanden mee: `.htaccess` (ook in `data/`, `lib/` en `media/`) en `.user.ini`.
2. **Maak `config.php`**: kopieer `config.example.php` naar `config.php` in dezelfde map en vul een
   wachtwoord in. Het veiligst als hash:

   ```bash
   php -r "echo password_hash('het wachtwoord', PASSWORD_DEFAULT), PHP_EOL;"
   ```

   Zet de uitkomst bij `'password_hash'`. Simpeler mag ook: het wachtwoord zelf bij `'password'`.
   Kristoffer kan het daarna zelf veranderen in het beheer (Gegevens). Dat nieuwe wachtwoord wint dan.
3. **Schrijfrechten**: de webserver moet in de mappen `data/` en `media/` kunnen schrijven. Meestal is
   dat al zo. Zo niet, zet ze in het FTP-programma op 755 (of 775).
4. **Open `https://<domein>/beheer/`** en log in. Kijk onder **Gegevens**, blok **Server**: alles hoort een
   groen vinkje te hebben. Let vooral op **Grootste upload**: telefoonfoto's zijn 3 tot 15 MB, dus minstens
   20 MB. De site vraagt zelf 64 MB (via `.user.ini` en `.htaccess`), maar sommige hosts staan minder toe;
   dan kan het vaak in het controlepaneel van de host (instellingen van PHP).
5. **Probeer één upload** en kijk of het beeld op de site staat.
6. **Controleer dat de privé-bestanden echt privé zijn**: `https://<domein>/data/site.json` en
   `https://<domein>/config.php` moeten een foutmelding geven (403 of 404), geen inhoud. Geeft `data/`
   wel iets? Dan gebruikt de host geen `.htaccess` (nginx). Zet dan in `config.php` een `'data_dir'` buiten
   de webmap, bijvoorbeeld `'/home/<account>/kris-data'`, en verplaats de map `data` daarheen.

## Wat waar staat

| Map of bestand | Wat erin staat | Openbaar? |
|---|---|---|
| `index.php`, `assets/` | De site zelf, met het lettertype (Archivo) erbij: geen verzoeken aan Google | ja |
| `media/` | De verkleinde kopieën die de site toont (2048 en 640 pixels), zonder metagegevens | ja |
| `beheer/` | Het beheer, achter het wachtwoord | inloggen |
| `data/site.json` | Titels, albums, teksten | nee |
| `data/originals/` | De originelen, precies zoals geüpload. Niet openbaar: telefoonfoto's bevatten de GPS-plek | nee |
| `data/password.json` | Het wachtwoord als hash, als Kristoffer het heeft veranderd | nee |
| `lib/`, `config.php` | De code en het wachtwoord uit stap 2 | nee |

## Back-up

Bewaar af en toe de mappen **`data/`** en **`media/`** (via FTP, of de back-up van de host). Daarin staat
alles wat Kristoffer zelf heeft toegevoegd. De rest is gewoon deze code.

## Een nieuwe versie

Zet de nieuwe bestanden uit `site/` over de oude heen. Je overschrijft daarmee niets van Kristoffer:
`config.php`, de originelen en de kopieën zitten niet in de nieuwe versie.

## Tips voor Kristoffer

- **Kies de beelden in de galerij van de telefoon of op de computer**, niet uit WhatsApp: WhatsApp maakt
  ze klein.
- Bij elk beeld kun je de titel nog aanpassen voordat je op Uploaden drukt.
- Geeft de site "Dit is een HEIC-bestand"? Zet dan de camera op JPEG of "Meest compatibel", of exporteer
  het beeld als JPEG.
- **Groot op de voorpagina**: bewerk een beeld en vink het aan. Zonder keuze staat het nieuwste beeld
  bovenaan.
- **Zichtbaar in zoekmachines** staat eerst uit (Gegevens). Zet het aan als de site af is.
