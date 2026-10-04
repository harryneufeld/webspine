<?php
declare(strict_types=1);
namespace Webspine\Contracts;
interface Mail {
    public function send(string $to, string $subject, string $body): void;
}
