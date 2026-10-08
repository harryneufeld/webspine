<?php
declare(strict_types=1);
namespace Webspine;

/** Immutable filter expression; field types are validated against a declaration. */
final readonly class EntityFilter {
    private function __construct(public string $operator, public ?string $field, public mixed $value, public array $children = []) {}
    public static function compare(string $field, string $operator, mixed $value): self {
        if (!in_array($operator,['eq','ne','lt','lte','gt','gte'],true)) throw new \InvalidArgumentException('Unsupported entity filter operator.');
        return new self($operator,$field,$value);
    }
    public static function eq(string $field,mixed $value):self { return self::compare($field,'eq',$value); }
    public static function ne(string $field,mixed $value):self { return self::compare($field,'ne',$value); }
    public static function lt(string $field,mixed $value):self { return self::compare($field,'lt',$value); }
    public static function lte(string $field,mixed $value):self { return self::compare($field,'lte',$value); }
    public static function gt(string $field,mixed $value):self { return self::compare($field,'gt',$value); }
    public static function gte(string $field,mixed $value):self { return self::compare($field,'gte',$value); }
    public static function in(string $field,array $values):self { return new self('in',$field,$values); }
    public static function isNull(string $field):self { return new self('is-null',$field,null); }
    public static function notNull(string $field):self { return new self('not-null',$field,null); }
    public static function all(self ...$children):self { return new self('all',null,null,$children); }
    public static function any(self ...$children):self { return new self('any',null,null,$children); }
}
