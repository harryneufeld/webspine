<?php
declare(strict_types=1);
namespace Webspine\Providers;
use Webspine\{EntityDefinition,EntityFilter,EntityQuery};

/** SQL, JSON expressions and binding types stay inside the SQLite provider. */
final class SQLiteEntityQuery {
    public static function field(string $name):string {
        if (!preg_match('/^[a-z][a-z0-9_]{0,63}$/D',$name)) throw new \InvalidArgumentException('Invalid indexed entity field.');
        return "json_extract(data, '$.$name')";
    }
    public static function compile(EntityQuery $query,EntityDefinition $definition):array {
        $query->validate($definition);$bindings=[];
        $where=$query->filter===null?'1':self::filter($query->filter,$definition,$bindings);
        $order=[];
        foreach($query->order as $name=>$direction) {
            $column=str_starts_with($name,'@')?substr($name,1):self::field($name);
            if(($definition->fields[$name]['type']??null)==='string')$column=self::text($name);
            $order[]=$column.' '.strtoupper($direction);
        }
        if(!array_key_exists('@id',$query->order))$order[]='id ASC';
        return ['where'=>$where,'order'=>implode(', ',$order),'bindings'=>$bindings];
    }
    private static function parameter(mixed $value,array &$bindings):string {
        if(is_float($value)) {$bindings[]=[json_encode($value,JSON_PRESERVE_ZERO_FRACTION|JSON_THROW_ON_ERROR),\PDO::PARAM_STR];return 'CAST(? AS REAL)';}
        $bindings[]=[is_bool($value)?(int)$value:$value,is_int($value)||is_bool($value)?\PDO::PARAM_INT:\PDO::PARAM_STR];return '?';
    }
    private static function text(string $field):string {
        return "webspine_entity_text(data, '$field')";
    }
    private static function filter(EntityFilter $filter,EntityDefinition $definition,array &$bindings):string {
        if(in_array($filter->operator,['all','any'],true)) {
            $parts=[];foreach($filter->children as $child)$parts[]=self::filter($child,$definition,$bindings);
            return '('.implode($filter->operator==='all'?' AND ':' OR ',$parts).')';
        }
        $column=self::field($filter->field);
        if($filter->operator==='is-null')return $column.' IS NULL';
        if($filter->operator==='not-null')return $column.' IS NOT NULL';
        $candidate=null;
        if($definition->fields[$filter->field]['type']==='string') {
            if(in_array($filter->operator,['eq','in'],true)) {
                $values=$filter->operator==='in'?$filter->value:[$filter->value];
                if(!$values)return '0';
                $candidates=[];
                foreach($values as $value) {
                    $candidates[]=$value;
                    if(str_contains($value,"\0"))$candidates[]=explode("\0",$value,2)[0];
                }
                $parameters=[];
                foreach(array_unique($candidates,SORT_STRING)as$value)$parameters[]=self::parameter($value,$bindings);
                $candidate=$column.' IN ('.implode(', ',$parameters).')';
            }
            $column=self::text($filter->field);
        }
        if($filter->operator==='in') {
            if(!$filter->value)return '0';$parameters=[];
            foreach($filter->value as $value)$parameters[]=self::parameter($value,$bindings);
            $predicate=$column.' IN ('.implode(', ',$parameters).')';
            return $candidate===null?$predicate:'('.$candidate.' AND '.$predicate.')';
        }
        $operator=match($filter->operator){'eq'=>'=','ne'=>'!=','lt'=>'<','lte'=>'<=','gt'=>'>','gte'=>'>='};
        $predicate=$column.' '.$operator.' '.self::parameter($filter->value,$bindings);
        return $candidate===null?$predicate:'('.$candidate.' AND '.$predicate.')';
    }
}
