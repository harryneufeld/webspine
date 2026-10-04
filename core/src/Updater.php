<?php
declare(strict_types=1);
namespace Webspine;
final class Updater {
    public function __construct(private string $root) {}
    private function locked(callable $action): mixed {
        $file = $this->root . '/storage/update.lock';
        $lock = fopen($file, 'c+');
        if (!$lock || !flock($lock, LOCK_EX)) throw new \RuntimeException('Cannot lock activation.');
        try { return $action(); } finally { flock($lock, LOCK_UN); fclose($lock); }
    }
    private function probe(string $codeRoot, string $expectedVersion): void {
        // Separate process: candidate code never shares classes with the running updater.
        $command = [PHP_BINARY, '-c', php_ini_loaded_file() ?: '', $codeRoot . '/core/bin/console.php', 'health'];
        $environment = getenv();
        $environment['WEBSPINE_SITE_ROOT'] = $this->root;
        $environment['WEBSPINE_CORE_ROOT'] = $codeRoot . '/core';
        $environment['WEBSPINE_UPDATE_PROBE'] = '1';
        $pipes = [];
        $process = proc_open($command, [0 => ['pipe', 'r'], 1 => ['pipe', 'w'], 2 => ['pipe', 'w']], $pipes, $this->root, $environment);
        if (!is_resource($process)) throw new \RuntimeException('Cannot start candidate health check.');
        fclose($pipes[0]);
        stream_set_blocking($pipes[1], false); stream_set_blocking($pipes[2], false);
        $output = ''; $error = ''; $deadline = microtime(true) + 30; $status = null;
        do {
            $output .= stream_get_contents($pipes[1]); $error .= stream_get_contents($pipes[2]);
            $status = proc_get_status($process);
            if (!$status['running']) break;
            if (microtime(true) > $deadline || strlen($output) + strlen($error) > 2000000) { proc_terminate($process); break; }
            usleep(20000);
        } while (true);
        $output .= stream_get_contents($pipes[1]); $error .= stream_get_contents($pipes[2]);
        fclose($pipes[1]); fclose($pipes[2]); $close = proc_close($process);
        $code = $status['exitcode'] >= 0 ? $status['exitcode'] : $close;
        $health = json_decode($output, true);
        if ($code !== 0 || empty($health['ok']) || ($health['version'] ?? '') !== $expectedVersion) throw new \RuntimeException('Candidate health check failed. ' . substr($error, 0, 200));
    }
    public function apply(string $archive): array {
        return $this->locked(function () use ($archive) {
            if (is_file($this->root . '/storage/update-pending.json')) throw new \RuntimeException('Run recover before another update.');
            $current = require $this->root . '/core/version.php';
            $id = gmdate('YmdHis') . '-' . bin2hex(random_bytes(5));
            $base = $this->root . '/storage/updates/' . $id;
            $stage = $base . '/stage'; $backup = $base . '/backup';
            $release = Release::stage($archive, $stage, $current);
            $candidate = require $stage . '/core/version.php';
            if ($candidate !== ['version' => $release['version'], 'api' => $release['api'], 'php' => $release['php']]) throw new \RuntimeException('Version file disagrees with release.');
            $this->probe($stage, $release['version']);
            $old = Files::inventory($this->root);
            foreach ($old as $path => $hash) {
                $source = Files::target($this->root, $path);
                Files::write(Files::target($backup, $path), file_get_contents($source));
                if (hash_file('sha256', $backup . '/' . $path) !== $hash) throw new \RuntimeException('Backup verification failed.');
            }
            foreach ($release['files'] as $path => $_) Files::target($this->root, $path);
            $journal = ['id' => $id, 'old' => $old, 'new' => $release['files'], 'from' => $current['version'], 'to' => $release['version']];
            Files::json($base . '/journal.json', $journal);
            Files::json($this->root . '/storage/update-pending.json', $journal);
            try {
                foreach ($release['files'] as $path => $hash) Files::write(Files::target($this->root, $path), file_get_contents($stage . '/' . $path));
                foreach (array_diff_key($old, $release['files']) as $path => $_) if (!unlink(Files::target($this->root, $path))) throw new \RuntimeException('Cannot remove stale managed file.');
                $this->probe($this->root, $release['version']);
                Files::json($this->root . '/storage/update-last.json', $journal);
                if (!unlink($this->root . '/storage/update-pending.json')) throw new \RuntimeException('Cannot clear update journal.');
            } catch (\Throwable $e) {
                $this->restore($journal);
                throw new \RuntimeException('Activation failed; previous core restored. ' . $e->getMessage(), 0, $e);
            }
            return ['ok' => true, 'from' => $journal['from'], 'to' => $journal['to'], 'backup' => $id];
        });
    }
    private function restore(array $journal): void {
        if (!preg_match('/^[0-9]{14}-[a-f0-9]{10}$/D', $journal['id'] ?? '')) throw new \RuntimeException('Invalid recovery journal.');
        $backup = $this->root . '/storage/updates/' . $journal['id'] . '/backup';
        foreach ($journal['old'] as $path => $hash) {
            if (!Files::managed($path) || !is_file(Files::target($backup, $path)) || hash_file('sha256', $backup . '/' . $path) !== $hash) throw new \RuntimeException('Recovery backup integrity failure.');
        }
        // Record rollback itself: an interrupted restore must also be recoverable.
        Files::json($this->root . '/storage/update-pending.json', $journal);
        foreach ($journal['old'] as $path => $hash) Files::write(Files::target($this->root, $path), file_get_contents($backup . '/' . $path));
        foreach (array_diff_key($journal['new'], $journal['old']) as $path => $_) {
            if (!Files::managed($path)) throw new \RuntimeException('Unsafe recovery path.');
            $file = Files::target($this->root, $path);
            if (is_file($file) && !unlink($file)) throw new \RuntimeException('Cannot remove new file during recovery.');
        }
        $this->probe($this->root, $journal['from']);
        $last = $this->root . '/storage/update-last.json';
        if (is_file($last) && (json_decode(file_get_contents($last), true)['id'] ?? null) === $journal['id']) unlink($last);
        if (!unlink($this->root . '/storage/update-pending.json')) throw new \RuntimeException('Cannot clear recovery journal.');
    }
    public function rollback(bool $recover = false): array {
        return $this->locked(function () use ($recover) {
            $pending = $this->root . '/storage/update-pending.json';
            if (!$recover && is_file($pending)) throw new \RuntimeException('Run recover for the interrupted activation.');
            $file = $recover ? $pending : $this->root . '/storage/update-last.json';
            if (!is_file($file)) throw new \RuntimeException($recover ? 'No interrupted update.' : 'No update to roll back.');
            $journal = json_decode(file_get_contents($file), true, 32, JSON_THROW_ON_ERROR);
            $this->restore($journal);
            return ['ok' => true, 'restored' => $journal['from']];
        });
    }
}
