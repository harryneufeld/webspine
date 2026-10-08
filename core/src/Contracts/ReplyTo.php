<?php
declare(strict_types=1);
namespace Webspine\Contracts;

/** A validated mailbox for the Reply-To header, not a delivery destination. */
final readonly class ReplyTo {
    public function __construct(public string $address, public string $name = '') {
        if (strlen($address) > 254 || !filter_var($address, FILTER_VALIDATE_EMAIL)
            || preg_match('/[\x00-\x1f\x7f]/', $address)
            || strlen($name) > 120 || !preg_match('//u', $name) || preg_match('/[\x00-\x1f\x7f]/', $name)) {
            throw new \InvalidArgumentException('Invalid Reply-To mailbox.');
        }
    }
}
