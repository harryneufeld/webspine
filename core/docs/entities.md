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
rules, full-text search, an ORM, forms, or authentication. Use plugin-owned
repositories for those needs. SQLite stores values as JSON in shared tables.
Keep SQL in providers, and check whether a substitute provider supplies `Entities`
before depending on it. Existing providers implementing only Settings/Pages
remain supported.

## Typed queries

SQLite additionally implements the optional `EntityQueries` capability on the
same `Entities` service. The original `Entities` interface is unchanged; older
providers can continue implementing CRUD alone. Check the capability explicitly
and report unsupported functionality rather than loading every record to filter
in PHP:

```php
use Webspine\Contracts\EntityQueries;
use Webspine\{EntityFilter as F, EntityQuery};

if (!$entities instanceof EntityQueries) {
    throw new RuntimeException('This feature requires an entity query provider.');
}
$filter = F::all(
    F::eq('active', true),
    F::any(F::lt('price_cents', 1500), F::in('name', ['Notebook', 'Book'])),
);
$products = $entities->search('products', new EntityQuery(
    $filter, ['price_cents' => 'asc', 'name' => 'asc'],
), limit: 20, offset: 0);
$total = $entities->count('products', $filter);
```

For a request plugin that has declared a string `status` field on `requests`,
the common workflow is simply:

```php
$open = $entities->search('requests', new EntityQuery(F::eq('status', 'open')),
    limit: 20, offset: 0);
```

The provider filters first and paginates the matching records in stable ID order.

Filters support `eq`, `ne`, `lt`, `lte`, `gt`, `gte`, `in`, `isNull`, `notNull`,
and nonempty `all` (AND)/`any` (OR) groups. Fields must be declared. Values use
the same strict types as their fields; a number accepts integers or finite floats.
Booleans support equality, inequality, IN and null checks only. Query values are
not restricted by write-time min/max, required or string-length rules: searching
outside an allowed write range simply matches no records.

Null checks match both absent fields and explicit null; `notNull` requires a
present non-null value. Other comparisons exclude both absent and null values,
including `ne`. Null in equality or IN is rejected; use null predicates instead.
An empty IN list matches nothing. SQLite string comparisons use case-sensitive
binary ordering, without locale folding or substring matching. Defaults are
stored on creation; queries do not synthesize defaults for raw database edits.
String predicates verify complete decoded values inside the SQLite query because
older SQLite JSON functions truncate decoded NUL bytes. Equality/IN additionally
use indexed JSON candidates, including truncated and complete forms when needed.
The verification runs on candidates selected by the database, before pagination.
String ranges and sorting use complete values and can require scanning/sorting;
JSON expression indexes do not accelerate those string operations. Stored indexes
use only SQLite built-ins, preserving older-framework write/rollback compatibility.

Search returns the usual record arrays. Limits are 1–100, offsets nonnegative.
Sorting accepts up to four declared fields with `asc`/`desc`, or metadata keys
`@id`, `@revision`, `@created_at`, `@updated_at`. A declared `id` sorts a value;
`@id` sorts the record ID. ID ascending breaks ties unless an explicit ID sort
already makes the order unique. Null/missing sort before values ascending and
after values descending. With no sort keys, search orders by ID ascending.
Pagination is stable for an unchanged dataset; concurrent writes can move offset
pages. Count and search are separate observations, not a shared snapshot.

Queries allow at most 64 filter nodes, eight nested group edges, 100 values in
each IN list, 256 values total, and 64 KiB of value bytes (strings count UTF-8
bytes; other scalars count eight bytes). Bounds and field/type validation happen
before query execution. These bounds limit input complexity, not the number of
matching database rows or execution time. Pass public request values only after
authorization and explicit parsing; never accept raw SQL or field expressions.

## Explicit query indexes

SQLite implements the optional `EntityIndexes` capability. Declare indexes after
the entity in plugin registration, checking the capability as needed:

```php
use Webspine\Contracts\EntityIndexes;

if ($entities instanceof EntityIndexes) {
    $entities->defineIndex('products', ['active', 'price_cents']);
}
```

Declarations access no database. Run `entities:install` explicitly to create
indexes, including for already populated entities. No index is created by a
query, application boot, health check, or core update. Index declarations allow
up to eight distinct sequences per entity, each with one to four distinct declared
fields; repeated declarations are harmless. Installation remains transactional
and idempotent and does not rewrite records or relax definition migration rules.
Removing a declaration does not drop its installed index automatically.

SQLite requires its JSON functions for queries/indexes. Expression indexes use
the entity, declared field sequence, and record ID. Identical sequences share a
physical index across entities; indexes cover the shared table and add storage
and write costs. Select index fields and order for actual queries: leading
equalities followed by a range/sort field often help. Arbitrary OR/inequality
filters, unmatched sorts, large offsets and broad counts can still scan many rows
or sort results. Use SQLite query plans and representative local fixtures to
measure workloads; indexes do not guarantee constant-time queries.

The integration fixture compares the same selective status query before and
after installing a matching index. At 5,000/20,000 records (one match per 1,000),
an instrumented candidate predicate evaluates 5,000/20,000 times without the
index and 20 times at 20,000 records with it. The provider's compiled query plan
also selects the expression index without a temporary sort. This measures
candidate evaluations, not SQLite VM steps or a timing guarantee.

Escape record values in themes with `e()`. Authorization, validation, CSRF
protection, and authentication belong to any future mutation endpoints; current
HTTP routes remain read-only. Back up the private SQLite database consistently
along with site/config files. Entity records live under storage/ and are preserved
by core updates and excluded from releases.
