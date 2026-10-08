# Durable job queue

`job-queue` is an optional bundled API 1 plugin with a provider-neutral Queue
interface, SQLite implementation and explicit handler registry. It is disabled
in the starter. Consumers declare a manifest dependency; the queued contact
example is one consumer. Enable it alone with `plugins => ['job-queue']`.
Registration defines services without creating storage or installing a schema.

```sh
php core/plugins/job-queue/cli.php install
php core/plugins/job-queue/cli.php status
php core/plugins/job-queue/cli.php work
php core/plugins/job-queue/cli.php retry <job-id>
php core/plugins/job-queue/cli.php prune 30
```

Invoke the worker through cron using an absolute script path. It holds the
framework's shared update lock for its invocation and refuses pending recovery.
Activation waits for workers, so keep custom handlers bounded. Ten jobs are
processed per CLI invocation; scheduling, process time limits, monitoring and
restart policy belong to the operator. Missing/paused handlers consume no jobs
or attempts. Unexpected handler failures record a safe category and log job ID,
exception class and source location, without exception text or payload data.
CLI failures exit nonzero with private class/file/line context.

## Services and handlers

```php
$queue = $app->services->get(\Webspine\Jobs\Queue::class);
$handlers = $app->services->get(\Webspine\Jobs\Handlers::class);
$handlers->register('example.process', new class implements \Webspine\Jobs\Handler {
    public function ready(): bool { return true; }
    public function handle(\Webspine\Jobs\Job $job): void {
        if ($job->version !== 1) throw new \Webspine\Jobs\PermanentFailure('unsupported_version');
        // Validate domain fields and perform an idempotent operation using $job->id.
    }
});
$id = $queue->enqueue('example.process', 1, ['entity_id'=>'123'], dedupe:'entity-123');
```

Payloads are JSON objects containing bounded JSON values, up to 64 KiB and
bounded depth. PHP objects, binary strings and non-finite numbers are rejected.
Nested object keys are canonicalized for dedupe comparison; list order is kept.
The same type/key/version/data returns the existing ID, while conflicting data
is rejected. Dedupe keys remain reserved while their jobs exist.

Claims use a five-minute lease and unique claim tokens. Expired claims can be
reclaimed; stale workers cannot complete/fail/renew replacement claims. Long
handlers must renew their lease through Queue and keep transport timeouts within
it. Retries back off from 60 seconds to one hour, defaulting to five attempts.
Expired final attempts become failed when that ready job type is next claimed.
Permanent failures and exhausted attempts remain available for explicit review
and retry. Handlers must validate payload versions independently of producers.

Delivery is at least once. Side effects may finish before a worker crashes, so
idempotence belongs to each handler and SMTP duplicates remain possible. Enqueue
is durable once SQLite commits, but is not atomic with a separate domain database
transaction; those workflows need their own transactional outbox.

## Private storage, retention and backups

The database lives at `storage/job-queue/jobs.sqlite` in WAL mode, using FULL
synchronous writes and a five-second busy timeout. Paths through symbolic links
are rejected. On POSIX, installation creates a setgid 2770 directory matching
the parent storage group and a 0660 database. Put the PHP worker and cron user in
that common group; make parent storage traversable and writable for both. An
installer that cannot assign the group fails explicitly. Windows needs suitable
private ACLs. Do not expose this directory through the document root.

`prune [days]` explicitly deletes up to 1,000 completed or terminal-failed jobs
per call whose terminal timestamp is older than the retention period (default
30 days, allowed 1–3650). Pending and processing jobs are retained. Review failed
jobs before pruning; deletion removes submitted data and dedupe history, so a
later enqueue using the old key can create a new job. Schedule repeated pruning
according to the site's retention needs; nothing is pruned during registration
or HTTP requests. This deletes logical rows, not historical backups or forensic
copies of database pages. Protect backups and manage their retention separately.

Core updates preserve queue data and do not install or migrate this schema.
Stop/drain PHP and queue writers for full-site archives, or use SQLite backup
facilities for a consistent snapshot. An active WAL database must not be backed
up by copying only its main file. Queue status and CLI summaries expose counts,
not submissions; there is no public administration endpoint or dashboard.

The implementation was adapted from the MIT-licensed source contribution
`webspine-queue-contact-0.1.0-source.zip`; provenance is recorded in the plugin's
`source.json` and its license is retained. Tests use disposable data and captured
mail, including concurrent claims, retries, retention and HTTP token recovery.
