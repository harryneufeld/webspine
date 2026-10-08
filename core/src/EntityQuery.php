<?php
declare(strict_types=1);
namespace Webspine;

/** Provider-neutral query validation; no SQL or database access. */
final readonly class EntityQuery {
    public function __construct(public ?EntityFilter $filter = null, public array $order = []) {
        if (count($order)>4) throw new \InvalidArgumentException('At most four entity sort keys.');
        foreach ($order as $field=>$direction) if (!is_string($field) || !in_array($direction,['asc','desc'],true)) throw new \InvalidArgumentException('Use field => asc/desc sort keys.');
        $nodes=0;$values=0;$bytes=0;
        if ($filter !== null) $this->walk($filter,0,$nodes,$values,$bytes);
    }
    private function walk(EntityFilter $filter,int $depth,int &$nodes,int &$values,int &$bytes):void {
        if ($depth>8 || ++$nodes>64) throw new \InvalidArgumentException('Entity filter exceeds depth/node bounds.');
        if (in_array($filter->operator,['all','any'],true)) {
            if (!$filter->children || !array_is_list($filter->children)) throw new \InvalidArgumentException('Filter groups require children.');
            foreach ($filter->children as $child) {
                if (!$child instanceof EntityFilter) throw new \InvalidArgumentException('Invalid entity filter child.');
                $this->walk($child,$depth+1,$nodes,$values,$bytes);
            }
            return;
        }
        if (!is_string($filter->field) || !preg_match('/^[a-z][a-z0-9_]{0,63}$/D',$filter->field)) throw new \InvalidArgumentException('Invalid entity filter field.');
        $items=$filter->operator==='in' ? $filter->value : (in_array($filter->operator,['is-null','not-null'],true) ? [] : [$filter->value]);
        if (!is_array($items) || !array_is_list($items) || count($items)>100) throw new \InvalidArgumentException('IN requires a list of at most 100 values.');
        foreach ($items as $value) {
            if ($value===null || !is_scalar($value) || is_float($value) && !is_finite($value)
                || is_string($value) && !preg_match('//u',$value)) throw new \InvalidArgumentException('Use finite typed query values; use null predicates for null.');
            $values++;$bytes+=is_string($value)?strlen($value):8;
        }
        if ($values>256 || $bytes>65536) throw new \InvalidArgumentException('Entity filter exceeds value/byte bounds.');
    }
    public function validate(EntityDefinition $definition):void {
        if ($this->filter !== null) $this->fields($this->filter,$definition);
        foreach ($this->order as $field=>$direction) if (!isset($definition->fields[$field])
            && !in_array($field,['@id','@revision','@created_at','@updated_at'],true)) throw new \InvalidArgumentException('Unknown entity sort field: '.$field);
    }
    private function fields(EntityFilter $filter,EntityDefinition $definition):void {
        if (in_array($filter->operator,['all','any'],true)) {foreach($filter->children as $child)$this->fields($child,$definition);return;}
        $rules=$definition->fields[$filter->field]??throw new \InvalidArgumentException('Unknown entity filter field: '.$filter->field);
        if (in_array($filter->operator,['is-null','not-null'],true)) return;
        if ($rules['type']==='boolean' && !in_array($filter->operator,['eq','ne','in'],true)) throw new \InvalidArgumentException('Boolean filters support only eq/ne/in and null predicates.');
        foreach ($filter->operator==='in'?$filter->value:[$filter->value] as $value) {
            $valid=match($rules['type']) {'string'=>is_string($value),'integer'=>is_int($value),'number'=>is_int($value)||is_float($value),'boolean'=>is_bool($value)};
            if (!$valid) throw new \InvalidArgumentException('Invalid query value type: '.$filter->field);
        }
    }
}
