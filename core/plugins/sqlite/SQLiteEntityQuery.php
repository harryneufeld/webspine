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
        $where=$query->filter===null?'1':self::filter($query->filter,$bindings);
        $order=[];
        foreach($query->order as $name=>$direction) $order[]=(str_starts_with($name,'@')?substr($name,1):self::field($name)).' '.strtoupper($direction);
        if(!array_key_exists('@id',$query->order))$order[]='id ASC';
        return ['where'=>$where,'order'=>implode(', ',$order),'bindings'=>$bindings];
    }
    private static function parameter(mixed $value,array &$bindings):string {
        if(is_float($value)) {$bindings[]=[json_encode($value,JSON_PRESERVE_ZERO_FRACTION|JSON_THROW_ON_ERROR),\PDO::PARAM_STR];return 'CAST(? AS REAL)';}
        $bindings[]=[is_bool($value)?(int)$value:$value,is_int($value)||is_bool($value)?\PDO::PARAM_INT:\PDO::PARAM_STR];return '?';
    }
    private static function filter(EntityFilter $filter,array &$bindings):string {
        if(in_array($filter->operator,['all','any'],true)) {
            $parts=[];foreach($filter->children as $child)$parts[]=self::filter($child,$bindings);
            return '('.implode($filter->operator==='all'?' AND ':' OR ',$parts).')';
        }
        $column=self::field($filter->field);
        if($filter->operator==='is-null')return $column.' IS NULL';
        if($filter->operator==='not-null')return $column.' IS NOT NULL';
        if($filter->operator==='in') {
            if(!$filter->value)return '0';$parameters=[];
            foreach($filter->value as $value)$parameters[]=self::parameter($value,$bindings);
            return $column.' IN ('.implode(', ',$parameters).')';
        }
        $operator=match($filter->operator){'eq'=>'=','ne'=>'!=','lt'=>'<','lte'=>'<=','gt'=>'>','gte'=>'>='};
        return $column.' '.$operator.' '.self::parameter($filter->value,$bindings);
    }
}
