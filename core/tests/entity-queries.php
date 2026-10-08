<?php
declare(strict_types=1);
use Webspine\{EntityFilter as F, EntityQuery as Q, EntityDefinition};
use Webspine\Contracts\{Entities, EntityQueries, EntityIndexes};
use Webspine\Providers\{SQLiteEntities, SQLiteEntityQuery};

return static function (string $base): void {
    $db = new PDO('sqlite:'.$base.'/queries.sqlite');
    $db->setAttribute(PDO::ATTR_ERRMODE, PDO::ERRMODE_EXCEPTION);
    $connections = 0;
    $entities = new SQLiteEntities(function () use ($db, &$connections): PDO { $connections++; return $db; });
    $fields = ['label'=>['type'=>'string'], 'status'=>['type'=>'string'], 'qty'=>['type'=>'integer'],
        'ratio'=>['type'=>'number'], 'enabled'=>['type'=>'boolean','default'=>false],
        'note'=>['type'=>'string','nullable'=>true], 'id'=>['type'=>'integer']];
    $entities->define('items',$fields); $entities->define('other',$fields);
    $entities->defineIndex('items',['status']);
    check('Query/index capabilities extend the unchanged Entities contract', fn() => $entities instanceof Entities && $entities instanceof EntityQueries && $entities instanceof EntityIndexes);
    $legacy = new class($entities) implements Entities {
        public function __construct(private Entities $inner) {}
        public function define(string $name,array $fields):void {$this->inner->define($name,$fields);}
        public function install():void {$this->inner->install();}
        public function health():array {return $this->inner->health();}
        public function create(string $name,array $values):array {return $this->inner->create($name,$values);}
        public function read(string $name,int $id):?array {return $this->inner->read($name,$id);}
        public function list(string $name,int $limit=50,int $offset=0):array {return $this->inner->list($name,$limit,$offset);}
        public function update(string $name,int $id,array $changes,?int $expectedRevision=null):?array {return $this->inner->update($name,$id,$changes,$expectedRevision);}
        public function delete(string $name,int $id,?int $expectedRevision=null):bool {return $this->inner->delete($name,$id,$expectedRevision);}
    };
    $requireQueries = static function(Entities $provider):EntityQueries {
        if (!$provider instanceof EntityQueries) throw new RuntimeException('Entity query capability unavailable.');
        return $provider;
    };
    check('CRUD-only providers explicitly reject optional query functionality', fn() => !$legacy instanceof EntityQueries && !$legacy instanceof EntityIndexes && expectError(fn()=>$requireQueries($legacy),'capability unavailable') && $requireQueries($entities)===$entities);
    check('Index declarations are memory-only and reject invalid shapes', fn() => $connections === 0 &&
        expectError(fn()=>$entities->defineIndex('missing',['status'])) && expectError(fn()=>$entities->defineIndex('items',[])) &&
        expectError(fn()=>$entities->defineIndex('items',['status','status'])) && expectError(fn()=>$entities->defineIndex('items',[[]])) &&
        expectError(fn()=>$entities->defineIndex('items',['unknown'])) && expectError(fn()=>$entities->defineIndex('items',['label','status','qty','ratio','enabled'])));
    check('Queries require explicit entity installation', fn() => expectError(fn()=>$entities->search('items'),'entities:install'));
    $entities->install();
    check('CRUD-only providers continue listing installed entities unchanged', fn() => $legacy->list('items')===[] && $legacy->health()['ok']);
    $a = $entities->create('items',['label'=>'Alpha','status'=>'open','qty'=>1,'ratio'=>0.5,'enabled'=>true,'id'=>30]);
    $b = $entities->create('items',['label'=>'beta','status'=>'closed','qty'=>2,'ratio'=>1.5,'note'=>null,'id'=>20]);
    $c = $entities->create('items',['label'=>'Alpha','status'=>'open','qty'=>3,'ratio'=>2,'note'=>'text','id'=>10]);
    $entities->create('other',['status'=>'open','qty'=>1]);
    $ids = static fn(array $rows):array => array_column($rows,'id');
    $search = static fn(F $filter, array $order=[]):array => $ids($entities->search('items',new Q($filter,$order)));
    check('Equality/inequality and counts stay scoped to the entity', fn() => $search(F::eq('status','open')) === [$a['id'],$c['id']] && $search(F::ne('status','open')) === [$b['id']] && $entities->count('items',F::eq('status','open'))===2 && $entities->count('other')===1);
    check('All four ordered operators preserve integer boundaries', fn() => $search(F::lt('qty',2))===[$a['id']] && $search(F::lte('qty',2))===[$a['id'],$b['id']] && $search(F::gt('qty',2))===[$c['id']] && $search(F::gte('qty',2))===[$b['id'],$c['id']]);
    check('Numeric comparisons bind fractions as numbers and accept integer number values', fn() => $search(F::eq('ratio',0.5))===[$a['id']] && $search(F::gt('ratio',1.25))===[$b['id'],$c['id']] && $search(F::eq('ratio',2.0))===[$c['id']] && $search(F::in('ratio',[0.5,2]))===[$a['id'],$c['id']]);
    check('Boolean equality/IN uses strict booleans including stored defaults', fn() => $search(F::eq('enabled',false))===[$b['id'],$c['id']] && $search(F::in('enabled',[true]))===[$a['id']]);
    check('IN handles multiple values and an empty set', fn() => $search(F::in('qty',[1,3]))===[$a['id'],$c['id']] && $search(F::in('qty',[]))===[]);
    check('Nested AND/OR keeps grouping and precedence', fn() => $search(F::all(F::any(F::eq('qty',1),F::eq('qty',2)),F::eq('status','closed')))===[$b['id']]);
    check('Null predicates include missing values; comparisons exclude null/missing', fn() => $search(F::isNull('note'))===[$a['id'],$b['id']] && $search(F::notNull('note'))===[$c['id']] && $search(F::ne('note','different'))===[$c['id']]);
    check('Sort keys use binary strings, null placement and stable ID ties', fn() => $ids($entities->search('items',new Q(null,['label'=>'asc'])))===[$a['id'],$c['id'],$b['id']] && $ids($entities->search('items',new Q(null,['note'=>'desc'])))===[$c['id'],$a['id'],$b['id']] && $ids($entities->search('items',new Q(null,['status'=>'desc','qty'=>'desc'])))===[$c['id'],$a['id'],$b['id']]);
    check('Metadata sorting distinguishes record IDs from declared id fields', fn() => $ids($entities->search('items',new Q(null,['id'=>'asc'])))===[$c['id'],$b['id'],$a['id']] && $ids($entities->search('items',new Q(null,['@id'=>'desc'])))===[$c['id'],$b['id'],$a['id']] && count($entities->search('items',new Q(null,['@revision'=>'desc','@created_at'=>'asc','@updated_at'=>'desc'])))===3);
    check('Filtered pagination and unfiltered search retain bounded list behavior', fn() => $ids($entities->search('items',new Q(F::eq('status','open')),1,1))===[$c['id']] && $entities->search('items')===$entities->list('items') && expectError(fn()=>$entities->search('items',null,0)) && expectError(fn()=>$entities->search('items',null,101)) && expectError(fn()=>$entities->search('items',null,1,-1)));
    check('Unknown fields/operators, raw identifiers and sort directions fail', fn() => expectError(fn()=>$search(F::eq('missing',1))) && expectError(fn()=>$search(F::eq("qty') OR 1=1 --",1))) && expectError(fn()=>F::compare('qty','LIKE',1)) && expectError(fn()=>new Q(null,['qty'=>'ASC'])) && expectError(fn()=>$entities->search('items',new Q(null,['missing'=>'asc']))) && expectError(fn()=>new Q(null,array_fill_keys(['a','b','c','d','e'],'asc'))));
    check('Query types reject coercion, null values, nonfinite numbers and invalid UTF-8', fn() => expectError(fn()=>$search(F::eq('qty','1'))) && expectError(fn()=>$search(F::eq('qty',1.0))) && expectError(fn()=>$search(F::eq('enabled',1))) && expectError(fn()=>$search(F::gt('enabled',true))) && expectError(fn()=>$search(F::eq('label',1))) && expectError(fn()=>$search(F::eq('ratio',INF))) && expectError(fn()=>$search(F::eq('note',null))) && expectError(fn()=>$search(F::in('note',[null]))) && expectError(fn()=>$search(F::eq('label',"\xff"))));
    $deep=F::eq('qty',1); for($i=0;$i<9;$i++)$deep=F::all($deep);
    check('Query size limits bound groups, depth, nodes, IN values and bytes', fn() => expectError(fn()=>new Q(F::all())) && expectError(fn()=>new Q($deep)) && expectError(fn()=>new Q(F::all(...array_fill(0,64,F::eq('qty',1))))) && expectError(fn()=>new Q(F::in('qty',range(1,101)))) && expectError(fn()=>new Q(F::in('qty',['key'=>1]))) && expectError(fn()=>new Q(F::all(F::in('qty',range(1,100)),F::in('qty',range(1,100)),F::in('qty',range(1,57))))) && expectError(fn()=>new Q(F::eq('label',str_repeat('x',65537)))));
    $text="' OR 1=1 --\0é";
    $special=$entities->create('items',['label'=>$text,'qty'=>9007199254740993]);
    check('Bound string values preserve SQL-looking text, NUL and Unicode', fn() => $search(F::eq('label',$text))===[$special['id']] && $search(F::eq('label',explode("\0",$text)[0]))===[] && $search(F::in('label',[$text]))===[$special['id']] && !in_array($special['id'],$search(F::ne('label',$text)),true));
    check('Bound integers retain 64-bit precision', fn() => $search(F::eq('qty',9007199254740993))===[$special['id']] && $search(F::eq('qty',9007199254740992))===[]);
    $entities->update('items',$a['id'],['status'=>'closed']); $entities->delete('items',$c['id']);
    check('Updates and deletes immediately affect indexed searches and counts', fn() => $entities->count('items',F::eq('status','open'))===0 && $search(F::eq('status','closed'))===[$a['id'],$b['id']]);
    $before=$db->query('SELECT * FROM entity_records ORDER BY id')->fetchAll(PDO::FETCH_ASSOC);
    $schema=$db->query("SELECT sql FROM sqlite_master WHERE type='index' ORDER BY name")->fetchAll(PDO::FETCH_COLUMN);
    $entities->defineIndex('items',['qty','label']);
    check('New index declarations and queries never change the schema', fn() => $entities->count('items')===3 && $schema===$db->query("SELECT sql FROM sqlite_master WHERE type='index' ORDER BY name")->fetchAll(PDO::FETCH_COLUMN));
    $entities->install(); $entities->install();
    check('Explicit index installation on populated entities is idempotent and preserves records', fn() => $before===$db->query('SELECT * FROM entity_records ORDER BY id')->fetchAll(PDO::FETCH_ASSOC) && count($db->query("SELECT name FROM sqlite_master WHERE name LIKE 'entity_query_%'")->fetchAll())===2);
    foreach(['label','qty','ratio','enabled','note','id'] as $field)$entities->defineIndex('items',[$field]);
    check('Index count is bounded and duplicate declarations remain idempotent', fn() => expectError(fn()=>$entities->defineIndex('items',['status','qty'])) && !expectError(fn()=>$entities->defineIndex('items',['status'])));
    $changed=new SQLiteEntities(fn():PDO=>$db);$changed->define('items',['qty'=>['type'=>'string']]);$changed->defineIndex('items',['qty']);
    check('Index installation still rejects populated definition changes transactionally', fn() => expectError(fn()=>$changed->install(),'migration') && $before===$db->query('SELECT * FROM entity_records ORDER BY id')->fetchAll(PDO::FETCH_ASSOC));

    // Reproduce old SQLite's decoded-NUL truncation on every platform.
    $oldDb=new PDO('sqlite:'.$base.'/old-json.sqlite');
    $oldDb->sqliteCreateFunction('json_extract',static function(string $json,string $path):mixed {
        $value=json_decode($json,true,32,JSON_THROW_ON_ERROR)[substr($path,2)]??null;
        return is_string($value)?explode("\0",$value,2)[0]:(is_bool($value)?(int)$value:$value);
    },2,PDO::SQLITE_DETERMINISTIC);
    $old=new SQLiteEntities(fn():PDO=>$oldDb);$old->define('texts',['text'=>['type'=>'string']]);$old->defineIndex('texts',['text']);$old->install();
    $prefix=$old->create('texts',['text'=>'a']);$nul=$old->create('texts',['text'=>"a\0z"]);$tail=$old->create('texts',['text'=>'b']);
    check('Old SQLite NUL extraction preserves exact matches, ranges, counts and sorting', fn() => $old->count('texts',F::eq('text','a'))===1 && $old->count('texts',F::eq('text',"a\0z"))===1 && $old->count('texts',F::in('text',['a',"a\0z"]))===2 && $old->count('texts',F::ne('text','a'))===2 && $ids($old->search('texts',new Q(F::gt('text','a'))))===[$nul['id'],$tail['id']] && $ids($old->search('texts',new Q(null,['text'=>'asc'])))===[$prefix['id'],$nul['id'],$tail['id']]);
    check('Persisted indexes never depend on query-only functions', fn() => !str_contains(implode(' ',$db->query("SELECT sql FROM sqlite_master WHERE type='index'")->fetchAll(PDO::FETCH_COLUMN)),'webspine_entity_text'));

    // Measure candidate evaluations, not wall-clock thresholds or SQLite VM steps.
    $perf=new PDO('sqlite:'.$base.'/query-performance.sqlite');$perf->setAttribute(PDO::ATTR_ERRMODE,PDO::ERRMODE_EXCEPTION);
    $provider=new SQLiteEntities(fn():PDO=>$perf);$provider->define('events',['status'=>['type'=>'string']]);$provider->install();
    $insert=$perf->prepare("INSERT INTO entity_records(entity,data,revision,created_at,updated_at) VALUES ('events',?,1,'2026-01-01','2026-01-01')");
    $probes=0;$perf->sqliteCreateFunction('query_work_probe',function(int $id)use(&$probes):int{$probes++;return 1;},1);
    $measure=static function()use($perf,&$probes):int {
        $probes=0;$q=$perf->prepare("SELECT id FROM entity_records WHERE entity='events' AND query_work_probe(id) AND json_extract(data, '$.status')='open' ORDER BY id");$q->execute();$q->fetchAll();return $probes;
    };
    $counts=[];
    foreach([5000,20000]as$size){
        $start=$counts?5000:0;$perf->beginTransaction();for($i=$start;$i<$size;$i++)$insert->execute([json_encode(['status'=>$i%1000===0?'open':'closed'])]);$perf->commit();
        $counts[$size]=$measure();
    }
    $provider->defineIndex('events',['status']);$provider->install();$indexed=$measure();
    $plan=SQLiteEntityQuery::compile(new Q(F::eq('status','open')),new EntityDefinition('events',['status'=>['type'=>'string']]));
    $explain=$perf->prepare('EXPLAIN QUERY PLAN SELECT * FROM entity_records WHERE entity=? AND ('.$plan['where'].') ORDER BY '.$plan['order']);$explain->execute(['events',...array_column($plan['bindings'],0)]);$detail=implode(' ',array_column($explain->fetchAll(PDO::FETCH_ASSOC),'detail'));
    check('Larger fixtures demonstrate selective index work and compiler index use', fn() => $counts[5000]===5000 && $counts[20000]===20000 && $indexed===20 && str_contains($detail,'entity_query_') && !str_contains($detail,'TEMP B-TREE') && $provider->count('events',F::eq('status','open'))===20 && count($provider->search('events',new Q(F::eq('status','open'))))===20);
    $verifications=0;$perf->sqliteCreateFunction('webspine_entity_text',static function(string $data,string $field)use(&$verifications):?string {
        $verifications++;return json_decode($data,true,32,JSON_THROW_ON_ERROR)[$field]??null;
    },2,PDO::SQLITE_DETERMINISTIC);
    $provider->search('events',new Q(F::eq('status','open')));
    check('Indexed provider search verifies only selected string candidates', fn() => $verifications===20);
    echo 'MEASURE entity query candidates: 5,000 rows='.$counts[5000].'; 20,000 rows='.$counts[20000].'; indexed 20,000 rows='.$indexed."\n";
};
