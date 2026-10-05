<?php
declare(strict_types=1);
return [
    'home' => ['template' => 'home', 'title' => 'Your website starts here'],
    'about' => ['template' => 'page', 'title' => 'About', 'data' => ['body' => 'Introduce yourself, your business, or your project. Tell visitors what you do and why it matters.']],
    'contact' => ['template' => 'page', 'title' => 'Contact', 'data' => ['body' => 'Give visitors a way to reach you. Replace this example with your contact details.']],
    'not-found' => ['template' => 'page', 'title' => 'Page not found', 'status' => 404, 'data' => ['body' => 'This address does not exist. You can return to the homepage using the link below.']],
];
