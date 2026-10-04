<?php
declare(strict_types=1);
namespace Webspine\Contracts;
interface Storage {
    public function install(): void;
    public function health(): array;
    public function settings(): Settings;
    public function pages(): Pages;
}
