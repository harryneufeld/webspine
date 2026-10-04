<?php
declare(strict_types=1);
namespace Webspine;
final class Files {
    public const ROOTS = ['core', 'public'];
    public const SINGLE = ['README.md', 'LICENSE'];
    public static function managed(string $path): bool {
        return in_array($path, self::SINGLE, true) || in_array(explode('/', $path)[0], self::ROOTS, true);
    }
    public static function safe(string $path): bool {
        if (!preg_match('~^[A-Za-z0-9_.-]+(?:/[A-Za-z0-9_.-]+)*$~D', $path)) return false;
        foreach (explode('/', $path) as $part) {
            if ($part === '.' || $part === '..' || str_ends_with($part, '.') || preg_match('/^(con|prn|aux|nul|com[0-9]|lpt[0-9])(?:\.|$)/i', $part)) return false;
        }
        return true;
    }
    public static function inventory(string $root, bool $full = false): array {
        $roots = self::ROOTS;
        if ($full) $roots[] = 'site';
        $files = [];
        foreach ($roots as $directory) {
            if (!is_dir($root . '/' . $directory)) continue;
            $iterator = new \RecursiveIteratorIterator(new \RecursiveDirectoryIterator($root . '/' . $directory, \FilesystemIterator::SKIP_DOTS));
            foreach ($iterator as $file) {
                if ($file->isLink()) throw new \RuntimeException('Release inventory cannot contain links.');
                if (!$file->isFile()) continue;
                $relative = str_replace('\\', '/', substr($file->getPathname(), strlen($root) + 1));
                if (!self::safe($relative)) throw new \RuntimeException('Unsafe release path: ' . $relative);
                $files[$relative] = hash_file('sha256', $file->getPathname());
            }
        }
        $singles = self::SINGLE;
        if ($full) $singles = array_merge($singles, ['AGENTS.md', 'config/example.php', 'storage/.gitkeep', '.gitignore', '.gitattributes']);
        foreach ($singles as $file) if (is_file($root . '/' . $file)) $files[$file] = hash_file('sha256', $root . '/' . $file);
        ksort($files); return $files;
    }
    public static function target(string $root, string $relative): string {
        if (!self::safe($relative)) throw new \RuntimeException('Unsafe path.');
        $path = $root;
        foreach (explode('/', $relative) as $part) {
            $path .= '/' . $part;
            if (is_link($path)) throw new \RuntimeException('Refusing a symbolic-link destination.');
        }
        return $path;
    }
    public static function write(string $path, string $body): void {
        if (!is_dir(dirname($path)) && !mkdir(dirname($path), 0700, true)) throw new \RuntimeException('Cannot create directory.');
        $tmp = $path . '.tmp-' . bin2hex(random_bytes(5));
        if (file_put_contents($tmp, $body) !== strlen($body)) throw new \RuntimeException('Incomplete write.');
        if (!@rename($tmp, $path)) {
            // Windows locks the executing CLI file against rename. An overwrite
            // is permitted; update callers hold the activation lock and journal
            // interrupted writes. Prefer atomic rename on every other path.
            if (PHP_OS_FAMILY !== 'Windows' || !is_file($path) || !@copy($tmp, $path) || hash_file('sha256', $path) !== hash('sha256', $body)) {
                @unlink($tmp); throw new \RuntimeException('Cannot activate file: ' . $path);
            }
            unlink($tmp);
        }
    }
    public static function json(string $path, array $data): void { self::write($path, json_encode($data, JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR) . "\n"); }
}
