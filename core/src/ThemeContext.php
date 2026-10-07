<?php
declare(strict_types=1);
namespace Webspine;

/** Presentation helpers shared by pages, layouts and isolated components. */
final class ThemeContext {
    public function __construct(private readonly Theme $renderer) {}
    public function id(): string { return $this->renderer->active(); }
    public function component(string $name, array $props = []): string {
        return $this->renderer->component($name, $props);
    }
    public function assetUrl(string $relative): string { return $this->renderer->assetUrl($relative); }
}
