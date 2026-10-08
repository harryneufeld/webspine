<?php
declare(strict_types=1);
if (PHP_SAPI !== 'cli') { http_response_code(404); exit; }
require dirname(__DIR__).'/bootstrap.php';
try {
    $command = $argv[1] ?? '';
    if ($command === 'verify' && count($argv) === 3) {
        $result = \Webspine\ReleaseVerification::archive($argv[2], $argv[2].'.sha256.json');
        $result = array_intersect_key($result,array_flip(['version','api','php','type'])) + ['verified'=>true];
    } elseif ($command === 'verify-set' && in_array(count($argv), [4,5], true)) {
        $result = \Webspine\ReleaseVerification::assets($argv[2],$argv[3],$argv[4] ?? null);
    } elseif ($command === 'build' && count($argv) === 4) {
        $root = realpath($argv[2]) ?: throw new RuntimeException('Missing trusted source directory.');
        // The source is operator-owned executable PHP, never an untrusted downloaded ZIP.
        $version = (require $root.'/core/version.php')['version'];
        foreach ([false,true] as $full) \Webspine\Release::package($root,$full,rtrim($argv[3],'/\\').'/webspine-'.($full?'':'core-').$version.'.zip');
        $result = \Webspine\ReleaseVerification::assets($argv[3],$version);
    } else throw new InvalidArgumentException('Usage: release.php build <trusted-source> <output-directory> | verify <archive> | verify-set <directory> <version> [trusted-source]');
    echo json_encode($result,JSON_PRETTY_PRINT|JSON_UNESCAPED_SLASHES|JSON_THROW_ON_ERROR)."\n";
} catch (Throwable $e) { fwrite(STDERR,'Release verification failed: '.$e->getMessage()."\n");exit(1); }
