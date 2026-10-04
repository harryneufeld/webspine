<?php
declare(strict_types=1);

// Page titles, template choices, and plain-text page content are site-owned.
return [
    'home' => ['template' => 'home', 'title' => 'Your own AI-driven website.'],
    'docs' => ['template' => 'docs', 'title' => 'Documentation'],
    'not-found' => [
        'template' => 'page', 'title' => 'A path yet to be built.', 'status' => 404,
        'data' => [
            'body' => 'This page does not exist. Start again from the homepage or explore the documentation.',
            'notFound' => true,
        ],
    ],
    'download-unavailable' => [
        'template' => 'page', 'title' => 'Build your download', 'status' => 503,
        'data' => ['body' => 'Run php core/bin/console.php package --full to create this bootstrap ZIP.'],
    ],
    'field-notes' => [
        'template' => 'page', 'title' => 'Small pieces. Real possibilities.',
        'data' => ['body' => 'This route is registered by the field-notes feature plugin through the app.ready action hook. It reads the active theme through the framework and uses the shared layout. Explore site/plugins/field-notes/plugin.php to see the whole thing.'],
    ],
];
