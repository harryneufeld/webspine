<?php
declare(strict_types=1);
namespace Webspine;

/** Provider-independent declaration and value validation; no SQL or database access. */
final class EntityDefinition {
    public readonly array $fields;
    public function __construct(public readonly string $name, array $fields) {
        if (!preg_match('/^[a-z][a-z0-9-]{0,63}$/D', $name) || !$fields || count($fields) > 64) throw new \InvalidArgumentException('Invalid entity definition.');
        $normalized = [];
        foreach ($fields as $field => $rules) {
            if (!is_string($field) || !preg_match('/^[a-z][a-z0-9_]{0,63}$/D', $field) || !is_array($rules)) throw new \InvalidArgumentException('Invalid entity field.');
            if (array_diff(array_keys($rules), ['type','required','nullable','default','max_length','min','max'])) throw new \InvalidArgumentException('Unknown field rule: ' . $field);
            $type = $rules['type'] ?? '';
            if (!in_array($type, ['string','integer','number','boolean'], true)) throw new \InvalidArgumentException('Unsupported field type: ' . $field);
            foreach (['required','nullable'] as $flag) if (array_key_exists($flag, $rules) && !is_bool($rules[$flag])) throw new \InvalidArgumentException('Invalid field flag.');
            $rules += ['required'=>false, 'nullable'=>false];
            if ($type === 'string') $rules += ['max_length'=>10000];
            if (array_key_exists('max_length', $rules) && ($type !== 'string' || !is_int($rules['max_length']) || $rules['max_length'] < 1 || $rules['max_length'] > 1000000)) throw new \InvalidArgumentException('Invalid string length rule.');
            foreach (['min','max'] as $bound) if (array_key_exists($bound, $rules) && (!in_array($type,['integer','number'],true) || !is_int($rules[$bound]) && !is_float($rules[$bound]) || !is_finite((float)$rules[$bound]))) throw new \InvalidArgumentException('Invalid numeric bound.');
            if (isset($rules['min'], $rules['max']) && $rules['min'] > $rules['max']) throw new \InvalidArgumentException('Invalid numeric range.');
            if (array_key_exists('default', $rules)) $this->value($field, $rules['default'], $rules);
            ksort($rules); $normalized[$field] = $rules;
        }
        ksort($normalized); $this->fields = $normalized;
    }
    public function json(): string {
        return json_encode($this->fields, JSON_PRESERVE_ZERO_FRACTION | JSON_THROW_ON_ERROR);
    }
    public function validate(array $values): array {
        if (array_diff(array_keys($values), array_keys($this->fields))) throw new \InvalidArgumentException('Unknown entity field.');
        $result = [];
        foreach ($this->fields as $field => $rules) {
            if (!array_key_exists($field, $values)) {
                if (array_key_exists('default', $rules)) $values[$field] = $rules['default'];
                elseif ($rules['required']) throw new \InvalidArgumentException('Required entity field: ' . $field);
                else continue;
            }
            $this->value($field, $values[$field], $rules); $result[$field] = $values[$field];
        }
        if (strlen(json_encode($result, JSON_THROW_ON_ERROR)) > 1000000) throw new \InvalidArgumentException('Entity record is too large.');
        return $result;
    }
    private function value(string $field, mixed $value, array $rules): void {
        if ($value === null && $rules['nullable']) return;
        $valid = match ($rules['type']) {
            'string' => is_string($value),
            'integer' => is_int($value),
            'number' => (is_int($value) || is_float($value)) && is_finite((float)$value),
            'boolean' => is_bool($value),
        };
        if (!$valid) throw new \InvalidArgumentException('Invalid entity field type: ' . $field);
        if (is_string($value)) {
            $length = preg_match_all('/./us', $value);
            if ($length === false || $length > $rules['max_length'] || $rules['required'] && trim($value) === '') throw new \InvalidArgumentException('Invalid entity string: ' . $field);
        }
        if (isset($rules['min']) && $value < $rules['min'] || isset($rules['max']) && $value > $rules['max']) throw new \InvalidArgumentException('Entity field out of range: ' . $field);
    }
}
