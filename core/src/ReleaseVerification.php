<?php
declare(strict_types=1);
namespace Webspine;

/** Archive inspection only: no PHP from a release is executed. */
final class ReleaseVerification {
    public static function text(string $path, string $body): void {
        if (str_starts_with($path, 'core/vendor/') && $path !== 'core/vendor/dependencies.json') return;
        $extension = strtolower(pathinfo($path, PATHINFO_EXTENSION));
        $text = in_array($extension, ['php','md','css','js','svg','json','html','htm','txt','sh','ps1','psm1','yml','yaml','conf'], true)
            || in_array(basename($path), ['LICENSE','Dockerfile','.gitignore','.gitattributes','.gitkeep'], true);
        if ($text && str_contains($body, "\r")) throw new \RuntimeException('Noncanonical text line endings: ' . $path . '. Use an LF checkout or Git export before packaging.');
    }
    public static function archive(string $archive, ?string $sidecar = null): array {
        $zip = new \ZipArchive();
        if ($zip->open($archive) !== true) throw new \RuntimeException('Cannot open release ZIP.');
        try {
            if ($zip->numFiles > 3000) throw new \RuntimeException('Too many archive entries.');
            $raw = $zip->getFromName('release.json', 2000001);
            if ($raw === false || strlen($raw) > 2000000) throw new \RuntimeException('Missing or oversized release inventory.');
            $m = json_decode($raw, true, 32, JSON_THROW_ON_ERROR);
            $version = '/^(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)$/D';
            if (($m['format'] ?? null) !== 1 || !in_array($m['type'] ?? null, ['core','bootstrap'], true)
                || ($m['api'] ?? null) !== 1 || !is_string($m['version'] ?? null) || !preg_match($version, $m['version'])
                || !is_string($m['php'] ?? null) || !preg_match($version, $m['php']) || !is_array($m['files'] ?? null)) throw new \RuntimeException('Invalid release metadata.');
            self::text('release.json', $raw);
            foreach (['core/bootstrap.php','core/version.php','core/bin/console.php','public/index.php','core/src/App.php','core/src/Updater.php','core/vendor/dependencies.json'] as $required) {
                if (!isset($m['files'][$required])) throw new \RuntimeException('Incomplete release inventory.');
            }
            if ($m['type'] === 'bootstrap') foreach (['site/meta.php','site/pages.php','site/routes.php','config/example.php','AGENTS.md'] as $required) {
                if (!isset($m['files'][$required])) throw new \RuntimeException('Incomplete bootstrap inventory.');
            }
            $seen = []; $total = 0;
            for ($i = 0; $i < $zip->numFiles; $i++) {
                $stat = $zip->statIndex($i); $path = $stat['name']; $lower = strtolower($path);
                if (isset($seen[$lower])) throw new \RuntimeException('Duplicate archive path.');
                $seen[$lower] = true;
                $zip->getExternalAttributesIndex($i, $opsys, $attributes);
                if ((($attributes >> 16) & 0170000) === 0120000) throw new \RuntimeException('Archive links are forbidden.');
                $total += $stat['size'];
                if ($stat['size'] > 20000000 || $total > 100000000) throw new \RuntimeException('Archive exceeds size limits.');
                if ($path === 'release.json') continue;
                $bootstrapPath = str_starts_with($path, 'site/') || in_array($path, ['AGENTS.md','config/example.php','storage/.gitkeep','.gitignore','.gitattributes'], true);
                if (!Files::safe($path) || (!Files::managed($path) && !($m['type'] === 'bootstrap' && $bootstrapPath))
                    || !is_string($m['files'][$path] ?? null) || !preg_match('/^[a-f0-9]{64}$/D', $m['files'][$path])) throw new \RuntimeException('Unlisted, private, or unsafe archive entry.');
                $body = $zip->getFromIndex($i);
                if ($body === false || hash('sha256', $body) !== $m['files'][$path]) throw new \RuntimeException('Release hash mismatch: ' . $path);
                self::text($path, $body);
            }
            if (count($seen) !== count($m['files']) + 1) throw new \RuntimeException('Archive inventory is not exact.');
            foreach ($m['files'] as $path => $hash) {
                if (!is_string($path) || !isset($seen[strtolower($path)]) || $zip->locateName($path) === false) throw new \RuntimeException('Missing inventory file.');
            }
            $dependencies = json_decode($zip->getFromName('core/vendor/dependencies.json'), true, 32, JSON_THROW_ON_ERROR);
            if (!is_array($dependencies['files'] ?? null)) throw new \RuntimeException('Invalid vendor checksums.');
            foreach ($dependencies['files'] as $path => $hash) {
                if (!is_string($path) || !str_starts_with($path, 'core/vendor/') || !Files::safe($path) || !is_string($hash)
                    || !preg_match('/^[a-f0-9]{64}$/D', $hash) || ($m['files'][$path] ?? null) !== $hash) throw new \RuntimeException('Vendor checksum mismatch.');
            }
        } finally { $zip->close(); }
        if ($sidecar !== null) {
            $raw = file_get_contents($sidecar, false, null, 0, 1025);
            if ($raw === false || strlen($raw) > 1024) throw new \RuntimeException('Missing or oversized release sidecar.');
            $digest = json_decode($raw, true, 8, JSON_THROW_ON_ERROR);
            if (($digest['file'] ?? null) !== basename($archive) || ($digest['sha256'] ?? null) !== hash_file('sha256', $archive)) throw new \RuntimeException('Release sidecar mismatch.');
        }
        return $m;
    }
    /** Select only the four verified assets for this version, never a directory glob. */
    public static function assets(string $directory, string $version, ?string $source = null): array {
        if (!preg_match('/^(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)$/D', $version)) throw new \InvalidArgumentException('Invalid stable release version.');
        $assets = []; $manifests = [];
        foreach (['core','bootstrap'] as $type) {
            $name = 'webspine-' . ($type === 'core' ? 'core-' : '') . $version . '.zip';
            $path = rtrim($directory, '/\\') . '/' . $name;
            if (!is_file($path) || is_link($path) || !is_file($path . '.sha256.json') || is_link($path . '.sha256.json')) throw new \RuntimeException('Missing or linked release asset: ' . $name);
            $m = self::archive($path, $path . '.sha256.json');
            if ($m['version'] !== $version || $m['type'] !== $type) throw new \RuntimeException('Release asset identity mismatch.');
            if ($source !== null && $m['files'] !== Files::inventory($source, $type === 'bootstrap')) throw new \RuntimeException('Archive differs from publisher checkout.');
            $manifests[$type] = $m;
            foreach ([$path,$path . '.sha256.json'] as $asset) $assets[] = ['name'=>basename($asset),'path'=>realpath($asset),'sha256'=>hash_file('sha256',$asset),'size'=>filesize($asset)];
        }
        if ($manifests['core']['api'] !== $manifests['bootstrap']['api'] || $manifests['core']['php'] !== $manifests['bootstrap']['php']) throw new \RuntimeException('Release pair metadata mismatch.');
        $managed = array_filter($manifests['bootstrap']['files'], static fn(string $path):bool => Files::managed($path), ARRAY_FILTER_USE_KEY);
        if ($managed !== $manifests['core']['files']) throw new \RuntimeException('Core/bootstrap file inventories differ.');
        return $assets;
    }
}
