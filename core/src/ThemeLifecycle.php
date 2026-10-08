<?php
declare(strict_types=1);
namespace Webspine;
use Webspine\Contracts\Settings;

/** Opt-in checks at outer rendering boundaries, never per nested component. */
final class ThemeLifecycle {
    private ?string $id = null;
    private array $files = [];
    private ?string $plugins = null;
    private ?string $stale = null;
    public function __construct(private App $app, private bool $enabled) {}
    public function reset(): void { $this->id = null; $this->files = []; $this->plugins = null; $this->stale = null; }
    private function signature(string $file, bool $content): mixed {
        $stamp = AssetHashCache::stamp($file);
        if (!$content || $stamp === null) return $stamp;
        if ($stamp['size'] > 1048576) throw new \RuntimeException('Theme debug manifest exceeds 1 MiB.');
        return [$stamp, @hash_file('sha256', $file)];
    }
    public function remember(string $file, bool $content = false): void {
        if (!$this->enabled || isset($this->files[$file])) return;
        if (count($this->files) >= 256) throw new \RuntimeException('Theme debug lifecycle exceeds 256 observed files; refresh before the next page.');
        $this->files[$file] = [$content, $this->signature($file, $content)];
    }
    public function begin(string $id, array $manifest): void {
        if (!$this->enabled) return;
        $this->id = $id;
        $this->plugins = hash('sha256', serialize($this->app->plugins->loaded));
        $directory = $this->app->root . '/site/themes/' . $id;
        $this->remember($directory . '/theme.json', true);
        $this->remember($directory . '/layout.php');
        foreach (array_keys($manifest['dependencies']) as $plugin) {
            $system = $this->app->coreRoot . '/plugins/' . $plugin;
            $this->remember((is_dir($system) ? $system : $this->app->root . '/site/plugins/' . $plugin) . '/plugin.json', true);
        }
    }
    public function check(): void {
        if (!$this->enabled || $this->id === null) return;
        if ($this->stale !== null) throw new \RuntimeException($this->stale);
        $reason = null;
        if ($this->app->services->get(Settings::class)->get('theme') !== $this->id) $reason = 'Settings selection';
        if (hash('sha256', serialize($this->app->plugins->loaded)) !== $this->plugins) $reason = 'loaded plugin state';
        foreach ($this->files as $file => [$content, $signature]) {
            if ($this->signature($file, $content) !== $signature) {
                $reason = 'observed file ' . basename(dirname($file)) . '/' . basename($file); break;
            }
        }
        if ($reason !== null) {
            $this->stale = 'Stale theme rendering lifecycle for ' . $this->id . ' (' . $reason . '); call $app->theme->refresh() before the next page (create a new App after plugin code changes).';
            throw new \RuntimeException($this->stale);
        }
    }
}
