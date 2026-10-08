<?php
declare(strict_types=1);
if (PHP_SAPI !== 'cli') { http_response_code(404); exit; }
$root = getenv('WEBSPINE_SITE_ROOT') ?: dirname(__DIR__, 3);
require_once __DIR__ . '/TrafficInsights.php';
require_once __DIR__ . '/ReportAccess.php';
try {
    $command = $argv[1] ?? 'report';
    if ($command === 'set-password') {
        require_once __DIR__ . '/PasswordConfig.php';
        $changed = \Webspine\Providers\SpineAnalytics\PasswordConfig::hashFile($root . '/config/local.php');
        echo $changed ? "Password hashed in config/local.php; plaintext cleared. Username: analytics\n" : "Password is already hashed. No changes made.\n";
    } elseif ($command === 'hash-password') {
        // Accept stdin only: keep credentials out of process arguments and history.
        $input = (string) stream_get_contents(STDIN, 75);
        if (strlen($input) >= 75) throw new InvalidArgumentException('Password input is too long.');
        echo \Webspine\Providers\SpineAnalytics\ReportAccess::hashPassword(rtrim($input, "\r\n")) . "\n";
        unset($input);
    } else {
        if (!in_array($command, ['install', 'report', 'html'], true)) throw new InvalidArgumentException('Commands: install, set-password (from local.php), hash-password (stdin), report [days], html [days]');
        $config = require $root . '/config/' . (is_file($root . '/config/local.php') ? 'local.php' : 'example.php');
        $options = $config['spine_analytics'] ?? [];
        $insights = new \Webspine\Providers\SpineAnalytics\TrafficInsights($root, $options['pages'] ?? ['/'], ($options['device_metrics'] ?? false) === true);
        if ($command === 'install') { $insights->install(); echo "Aggregate insights storage initialized.\n"; }
        elseif ($command === 'report') echo json_encode($insights->report((int) ($argv[2] ?? 30)), JSON_PRETTY_PRINT | JSON_THROW_ON_ERROR) . "\n";
        else {
            require_once dirname(__DIR__, 2) . '/bootstrap.php';
            $report = $insights->report((int) ($argv[2] ?? 30)); $demo = false;
            $reportCss = str_replace(["\r\n", "\r"], "\n", file_get_contents(__DIR__ . '/assets/report.css'));
            require __DIR__ . '/report.php';
        }
    }
} catch (Throwable $error) { fwrite(STDERR, $error->getMessage() . "\n"); exit(1); }
