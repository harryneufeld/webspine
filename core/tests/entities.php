<?php
declare(strict_types=1);
use Webspine\{App, EntityDefinition};
use Webspine\Contracts\Entities;

return static function (string $base, string $installedSite): void {
    $root = $base . '/entity-site'; copyTree($installedSite, $root);
    $config = require $root . '/config/example.php'; $config['plugins'][] = 'catalog';
    file_put_contents($root . '/config/local.php', '<?php return ' . var_export($config,true) . ';');
    $before = hash_file('sha256', $root . '/storage/site.sqlite');
    $app = new App($root); $entities = $app->services->get(Entities::class);
    check('Entity declarations never write or migrate the database', fn() => hash_file('sha256',$root.'/storage/site.sqlite') === $before);
    check('Declared entities report missing installation without creating tables', fn() => !$app->health()['ok'] && expectError(fn() => $entities->create('products',['name'=>'Book','price_cents'=>1000]), 'entities:install') && hash_file('sha256',$root.'/storage/site.sqlite') === $before);
    check('CLI explicitly installs generic entity storage and plugin declarations', fn() => cli($root,['entities:install'])['code'] === 0 && $app->health()['ok']);
    $product = $entities->create('products',['name'=>'Book','price_cents'=>1000]);
    check('Entity creation applies defaults and generates metadata', fn() => $product['id'] > 0 && $product['values']['active'] === true && $product['revision'] === 1 && isset($product['created_at'],$product['updated_at']) && !array_key_exists('description',$product['values']));
    check('Entity records persist across independent application instances', fn() => (new App($root))->services->get(Entities::class)->read('products',$product['id']) === $product);
    check('Entity validation rejects unknown fields, missing required values, types, and ranges', fn() =>
        expectError(fn() => $entities->create('products',['name'=>'Book','price_cents'=>1000,'surprise'=>true])) &&
        expectError(fn() => $entities->create('products',['name'=>'Book'])) &&
        expectError(fn() => $entities->create('products',['name'=>'Book','price_cents'=>'1000'])) &&
        expectError(fn() => $entities->create('products',['name'=>'Book','price_cents'=>-1])) &&
        expectError(fn() => $entities->create('products',['name'=>' ','price_cents'=>10])) &&
        expectError(fn() => $entities->create('products',['name'=>str_repeat('x',201),'price_cents'=>10])));
    check('Invalid declarations and duplicate entity names fail explicitly', fn() =>
        expectError(fn() => $entities->define('products',['other'=>['type'=>'string']])) &&
        expectError(fn() => $entities->define('../bad',['name'=>['type'=>'string']])) &&
        expectError(fn() => new EntityDefinition('bad',['name'=>['type'=>'string','unknown'=>true]])) &&
        expectError(fn() => new EntityDefinition('bad',['name'=>['type'=>'integer','default'=>'1']])));
    $updated = $entities->update('products',$product['id'],['price_cents'=>1200,'description'=>null],1);
    check('Partial updates preserve untouched fields and increment revision', fn() => $updated['values']['name'] === 'Book' && $updated['values']['price_cents'] === 1200 && $updated['values']['description'] === null && $updated['revision'] === 2 && $updated['created_at'] === $product['created_at']);
    check('Revision conflicts protect records from stale updates and deletes', fn() => expectError(fn() => $entities->update('products',$product['id'],['name'=>'Stale'],1),'revision conflict') && expectError(fn() => $entities->delete('products',$product['id'],1),'revision conflict') && $entities->read('products',$product['id']) === $updated);
    check('Invalid updates leave stored records intact', fn() => expectError(fn() => $entities->update('products',$product['id'],['price_cents'=>null])) && $entities->read('products',$product['id']) === $updated);
    $second = $entities->create('products',['name'=>"<script>' OR 1=1 --</script>",'price_cents'=>1]);
    check('Parameterized entity storage retains arbitrary text as data', fn() => $entities->read('products',$second['id'])['values']['name'] === "<script>' OR 1=1 --</script>");
    check('Listing is stable, paginated, and bounded', fn() => $entities->list('products',1,0)[0]['id'] === $product['id'] && $entities->list('products',1,1)[0]['id'] === $second['id'] && count($entities->list('products')) === 2 && expectError(fn() => $entities->list('products',101)) && expectError(fn() => $entities->list('products',1,-1)));
    $entities->define('blog-entries',['title'=>['type'=>'string','required'=>true]]);
    check('New entity declarations require explicit installation', fn() => expectError(fn() => $entities->create('blog-entries',['title'=>'Hello']),'not installed'));
    $entities->install(); $entry = $entities->create('blog-entries',['title'=>'Hello']);
    check('Entity reads and mutations are scoped to their declared entity', fn() => $entities->read('products',$entry['id']) === null && $entities->update('products',$entry['id'],['name'=>'Wrong']) === null && !$entities->delete('products',$entry['id']) && count($entities->list('blog-entries')) === 1);
    check('Unknown entities and invalid IDs are rejected', fn() => expectError(fn() => $entities->read("' OR 1=1 --",1)) && expectError(fn() => $entities->read('products',0)));
    $entities->install();
    check('Repeated entity installation preserves existing records', fn() => $entities->read('products',$product['id']) === $updated);
    $changed = new App($root, array_replace($config,['plugins'=>[]])); $changedEntities = $changed->services->get(Entities::class);
    $changedEntities->define('extra',['title'=>['type'=>'string']]);
    $changedEntities->define('products',['name'=>['type'=>'integer']]);
    check('Definition changes require deliberate migration and installation rolls back', function () use ($changedEntities,$root): bool {
        if (!expectError(fn() => $changedEntities->install(),'definition changed')) return false;
        $db = new PDO('sqlite:'.$root.'/storage/site.sqlite');
        return !$db->query("SELECT name FROM entity_definitions WHERE name = 'extra'")->fetchColumn() && expectError(fn() => $changedEntities->read('products',1),'definition changed');
    });
    $entities->define('drafts',['title'=>['type'=>'string']]); $entities->install();
    $emptyApp = new App($root, array_replace($config,['plugins'=>[]])); $emptyEntities = $emptyApp->services->get(Entities::class);
    $emptyEntities->define('drafts',['title'=>['type'=>'string','required'=>true], 'published'=>['type'=>'boolean','default'=>false]]);
    check('Empty entity definitions can be revised explicitly without a migration runner', function () use ($emptyEntities): bool {
        $emptyEntities->install();
        return $emptyEntities->create('drafts',['title'=>'Draft'])['values']['published'] === false;
    });
    check('Deletion is persistent and missing records return predictable results', fn() => $entities->delete('products',$second['id'],1) && $entities->read('products',$second['id']) === null && !$entities->delete('products',$second['id']) && $entities->update('products',$second['id'],['name'=>'Missing']) === null);
    $definition = new EntityDefinition('values',[
        'ratio'=>['type'=>'number','min'=>0,'max'=>1],
        'label'=>['type'=>'string','max_length'=>2],
        'enabled'=>['type'=>'boolean'],
    ]);
    check('Typed fields validate Unicode length, finite numbers, and strict booleans', fn() => $definition->validate(['label'=>'éé','ratio'=>0.5,'enabled'=>false])['label'] === 'éé' && expectError(fn() => $definition->validate(['ratio'=>INF])) && expectError(fn() => $definition->validate(['label'=>'ééé'])) && expectError(fn() => $definition->validate(['enabled'=>1])) && expectError(fn() => $definition->validate(['label'=>"\xff"])));
    unset($app,$entities,$changed,$changedEntities,$emptyApp,$emptyEntities); gc_collect_cycles();
};
