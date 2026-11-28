<?php
// Copy this file to config.php (in the same folder) and give the beheer a password.
// Kristoffer can change it later himself, under Beheer, Gegevens; that new one then wins.
return [
    // The password for beheer/. Safest as a hash: make one with
    //   php -r "echo password_hash('your password', PASSWORD_DEFAULT), PHP_EOL;"
    // and paste it here:
    'password_hash' => '',

    // Or, simpler, the password itself (leave password_hash empty then):
    'password' => '',

    // Where the private data lives: site.json, the originals, sign-in attempts. By default the
    // folder data next to this file, which .htaccess keeps private. If the host allows a folder
    // outside the web root, that is safer still:
    // 'data_dir' => '/home/account/kris-data',
];
