<?php
declare(strict_types=1);
namespace Webspine\Contracts;
interface Pages {
    /** @return array{slug:string,title:string,body:string}|null Plain text, never trusted HTML. */
    public function find(string $slug): ?array;
    public function put(string $slug, string $title, string $body): void;
}
