<?php
declare(strict_types=1);

// Copy to local.php. This file is read before any provider opens a database.
return [
    'providers' => ['storage' => 'sqlite', 'mail' => null],
    'plugins' => [],
    'sqlite' => ['path' => 'storage/site.sqlite'],
    'smtp' => [
        'host' => 'localhost', 'port' => 587, 'encryption' => 'tls',
        'username' => '', 'password' => '', 'from' => 'hello@example.com',
        'from_name' => 'Your site',
    ],
];
