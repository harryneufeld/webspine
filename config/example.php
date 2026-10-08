<?php
declare(strict_types=1);

// Copy to local.php. This file is read before any provider opens a database.
return [
    'providers' => ['storage' => 'sqlite', 'mail' => null],
    'plugins' => [],
    // Add 'spine-analytics' to plugins, then explicitly run its cli.php install.
    'spine_analytics' => [
        'enabled' => false,
        'preview' => false, // Local development only; never behind a production proxy.
        'report_enabled' => false,
        'password' => null, // Temporarily set in local.php, then run cli.php set-password.
        'password_hash' => null, // Generated hash goes in private config/local.php.
        'device_metrics' => false,
        'pages' => ['/'], // Fixed public paths only; other paths become [other].
    ],
    'sqlite' => ['path' => 'storage/site.sqlite'],
    'smtp' => [
        'host' => 'localhost', 'port' => 587, 'encryption' => 'tls',
        'username' => '', 'password' => '', 'from' => 'hello@example.com',
        'from_name' => 'Your site',
    ],
];
