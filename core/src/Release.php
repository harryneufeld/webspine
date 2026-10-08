<?php
declare(strict_types=1);
namespace Webspine;
final class Release {
    public static function package(string $root, bool $full = false, ?string $output = null): string {
        if (!class_exists(\ZipArchive::class)) throw new \RuntimeException('The PHP zip extension is required.');
        $version = require $root . '/core/version.php';
        $inventory = Files::inventory($root, $full);
        // Reject noncanonical source; archived bytes and inventory hashes stay identical.
        foreach ($inventory as $path => $hash) ReleaseVerification::text($path, file_get_contents($root . '/' . $path));
        $manifest = $version + ['format' => 1, 'type' => $full ? 'bootstrap' : 'core', 'files' => $inventory];
        $output ??= $root . '/.dist/webspine-' . ($full ? '' : 'core-') . $version['version'] . '.zip';
        if (!is_dir(dirname($output))) mkdir(dirname($output), 0700, true);
        $temp = $output . '.tmp';
        $zip = new \ZipArchive();
        if ($zip->open($temp, \ZipArchive::CREATE | \ZipArchive::OVERWRITE) !== true) throw new \RuntimeException('Cannot create archive.');
        $zip->addFromString('release.json', json_encode($manifest, JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR) . "\n");
        foreach ($inventory as $path => $hash) {
            $body = file_get_contents($root . '/' . $path);
            if (hash('sha256', $body) !== $hash || !$zip->addFromString($path, $body)) throw new \RuntimeException('File changed while packaging.');
        }
        if (!$zip->close()) throw new \RuntimeException('Cannot finalize release.');
        try { ReleaseVerification::archive($temp); }
        catch (\Throwable $e) { unlink($temp); throw $e; }
        if (!rename($temp, $output)) throw new \RuntimeException('Cannot finalize release.');
        Files::json($output . '.sha256.json', ['file' => basename($output), 'sha256' => hash_file('sha256', $output)]);
        return $output;
    }
    public static function stage(string $archive, string $stage, array $current): array {
        if (!class_exists(\ZipArchive::class)) throw new \RuntimeException('The PHP zip extension is required.');
        $zip = new \ZipArchive();
        if ($zip->open($archive) !== true) throw new \RuntimeException('Cannot open release ZIP.');
        try {
            if ($zip->numFiles > 3000) throw new \RuntimeException('Too many archive entries.');
            $raw = $zip->getFromName('release.json', 2000001);
            if ($raw === false || strlen($raw) > 2000000) throw new \RuntimeException('Missing or oversized release inventory.');
            $m = json_decode($raw, true, 32, JSON_THROW_ON_ERROR);
            if (($m['format'] ?? null) !== 1 || ($m['type'] ?? null) !== 'core' || ($m['api'] ?? null) !== $current['api'] || !preg_match('/^\d+\.\d+\.\d+$/D', $m['version'] ?? '') || !is_array($m['files'] ?? null) || !preg_match('/^\d+\.\d+\.\d+$/D', $m['php'] ?? '') || version_compare(PHP_VERSION, $m['php'], '<')) throw new \RuntimeException('Incompatible release.');
            if (version_compare($m['version'], $current['version'], '<=')) throw new \RuntimeException('Release must be newer than the installed core.');
            foreach (['core/bootstrap.php', 'core/version.php', 'core/bin/console.php', 'public/index.php', 'core/src/App.php', 'core/src/Updater.php'] as $required) if (!isset($m['files'][$required])) throw new \RuntimeException('Incomplete release inventory.');
            $seen = []; $total = 0;
            for ($i = 0; $i < $zip->numFiles; $i++) {
                $s = $zip->statIndex($i); $path = $s['name']; $lower = strtolower($path);
                if (isset($seen[$lower])) throw new \RuntimeException('Duplicate archive path.');
                $seen[$lower] = true;
                $zip->getExternalAttributesIndex($i, $opsys, $attributes);
                if ((($attributes >> 16) & 0170000) === 0120000) throw new \RuntimeException('Archive links are forbidden.');
                $total += $s['size'];
                if ($s['size'] > 20000000 || $total > 100000000) throw new \RuntimeException('Archive exceeds size limits.');
                if ($path === 'release.json') continue;
                if (!Files::safe($path) || !Files::managed($path) || !isset($m['files'][$path]) || !preg_match('/^[a-f0-9]{64}$/D', $m['files'][$path])) throw new \RuntimeException('Unlisted, private, or unsafe archive entry.');
                $body = $zip->getFromIndex($i);
                if ($body === false || hash('sha256', $body) !== $m['files'][$path]) throw new \RuntimeException('Release hash mismatch: ' . $path);
                Files::write(Files::target($stage, $path), $body);
            }
            if (count($seen) !== count($m['files']) + 1) throw new \RuntimeException('Archive inventory is not exact.');
            foreach ($m['files'] as $path => $hash) if (!isset($seen[strtolower($path)]) || !is_file(Files::target($stage, $path)) || hash_file('sha256', $stage . '/' . $path) !== $hash) throw new \RuntimeException('Missing inventory file.');
            return $m;
        } finally { $zip->close(); }
    }
}
