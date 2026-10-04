<?php
declare(strict_types=1);
// A completely different provider, with no PDO or SQLite dependency.
return new class implements \Webspine\Contracts\Plugin {
    public function register(\Webspine\App $app): void {
        $app->services->set(\Webspine\Contracts\Storage::class, new class implements \Webspine\Contracts\Storage, \Webspine\Contracts\Settings, \Webspine\Contracts\Pages {
            private array $values = [];
            private array $content = [];
            private bool $installed = false;
            public function install(): void { $this->installed = true; }
            public function health(): array { return ['ok' => $this->installed, 'provider' => 'memory']; }
            public function settings(): \Webspine\Contracts\Settings { return $this; }
            public function pages(): \Webspine\Contracts\Pages { return $this; }
            public function get(string $key, ?string $default = null): ?string { return $this->values[$key] ?? $default; }
            public function set(string $key, string $value): void { $this->values[$key] = $value; }
            public function find(string $slug): ?array { return $this->content[$slug] ?? null; }
            public function put(string $slug, string $title, string $body): void { $this->content[$slug] = compact('slug', 'title', 'body'); }
        });
    }
};
