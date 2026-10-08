<?php
declare(strict_types=1);
// Merge this section into your existing configuration. Append the plugin ID
// to your existing plugins list; preserve providers and other plugins.
return [
    'spine_analytics' => [
        'enabled' => false,
        'preview' => false,
        'report_enabled' => false, // Password-protected browser report over HTTPS.
        'password' => null, // Temporary plaintext in local.php; cli.php set-password clears it.
        'password_hash' => null, // Set the generated hash in private config/local.php.
        'device_metrics' => false, // Optional coarse totals from User-Agent only.
        // Public, fixed paths only. Never include customer IDs, personal data
        // or secrets. Unlisted paths are combined into [other].
        'pages' => ['/', '/docs'],
    ],
];
