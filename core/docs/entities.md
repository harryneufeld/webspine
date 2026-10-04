# Custom entities without custom CRUD

SQLite provides the optional `Webspine\Contracts\Entities` service. Plugins
declare entities in their `register()` method; no database access occurs during
declaration. Names must be unique across enabled plugins. Prefer a feature prefix
when independently developed plugins might choose the same name.

```php
use Webspine\Contracts\Entities;

$entities = $app->services->get(Entities::class);
$entities->define('products', [
    'name' => ['type' => 'string', 'required' => true, 'max_length' => 200],
    'price_cents' => ['type' => 'integer', 'required' => true, 'min' => 0],
    'active' => ['type' => 'boolean', 'default' => true],
]);
```

Install the site first with `php core/bin/console.php install`, then enable the
optional `catalog` example in the private config's `plugins` list. Install its
declarations locally with:

```sh
php core/bin/console.php entities:install
php core/bin/console.php health
```

This creates shared entity tables and records the declarations in the configured
SQLite database. It preserves settings/pages and existing entity records. Run it
again when adding a new entity. No table-per-entity SQL or repository is needed.
The installer is transactional and idempotent. It does not seed example products.
Health reports missing installation when an enabled plugin declares an entity.

Use the same service in trusted application code after installation:

```php
$product = $entities->create('products', ['name' => 'Notebook', 'price_cents' => 1200]);
$product = $entities->read('products', $product['id']);
$products = $entities->list('products', limit: 20, offset: 0);
$product = $entities->update('products', $product['id'], ['price_cents' => 1400], $product['revision']);
$deleted = $entities->delete('products', $product['id'], $product['revision']);
```

These are API examples, not a public mutation endpoint. Records contain `id`,
`entity`, `values`, `revision`, `created_at`, and `updated_at`. IDs are generated
across all entities; reads, updates, and deletes always scope them to the entity.
`read`/`update` return null for missing records; `delete` returns false. `list`
orders by ID ascending, accepts a limit of 1–100 and a nonnegative offset.

Updates merge only the supplied fields and increment revision. Pass the last-read
revision to update/delete to reject stale writes; omission uses serialized,
last-write-wins behavior. Invalid input or a conflict throws and leaves the record
unchanged. Database transactions serialize writes on local SQLite with a busy
timeout; this is intended for modest concurrency.

Supported field types: `string`, `integer`, `number`, `boolean`. Types are strict;
form strings need explicit parsing before calling the service. Optional rules:
`required`, `nullable`, `default`, `max_length` for strings, and `min`/`max` for
numbers. Strings must be UTF-8; length counts characters (default 10,000). Required
strings cannot be blank. Missing optional fields remain absent unless a default
is provided; null is accepted only with `nullable: true`. Unknown fields/rules,
invalid defaults, and non-finite numbers are rejected. Declarations allow up to
64 fields; JSON record size is capped at 1 MB. Use integer cents for money.

Definitions are trusted plugin code, never submitted by a public request. Changing
an installed field definition requires rerunning the explicit installer. Empty
entities can be redefined during development; populated definitions are rejected
to protect existing records. Automatic field changes and a migration runner are
not implemented. Prototype in an isolated local fixture; existing data must be
deliberately converted before changing a populated definition. Neither core updates nor ordinary requests
install tables, change definitions, or rewrite data.

The service does not supply relations, foreign keys between entities, uniqueness
rules, field filtering/search, custom indexes, an ORM, forms, or authentication.
Use plugin-owned repositories for those needs. SQLite stores values as JSON in
shared tables with an entity/ID index. Keep SQL in providers, and check whether a
substitute provider supplies `Entities` before depending on it. Existing providers
implementing only Settings/Pages remain supported.

Escape record values in themes with `e()`. Authorization, validation, CSRF
protection, and authentication belong to any future mutation endpoints; current
HTTP routes remain read-only. Back up the private SQLite database consistently
along with site/config files. Entity records live under storage/ and are preserved
by core updates and excluded from releases.
