<?php
declare(strict_types=1);
namespace Webspine\Examples;

/** Browser-compatible limits without an optional PHP Unicode extension. */
final class FormText {
    public const MAX_BYTES = 20000;
    public static function safe(string $value, bool $multiline): bool {
        return strlen($value) <= self::MAX_BYTES && preg_match('//u', $value) === 1
            && !preg_match('/[\x00-\x08\x0b\x0c\x0e-\x1f\x7f]/', $value)
            && ($multiline || !preg_match('/[\r\n]/', $value));
    }
    public static function units(string $value): int {
        $points = preg_match_all('/./us', $value);
        $pairs = preg_match_all('/[\x{10000}-\x{10ffff}]/u', $value);
        if ($points === false || $pairs === false) throw new \InvalidArgumentException('Expected valid UTF-8 text.');
        return $points + $pairs;
    }
}
