# Plugin console commands

Enabled trusted plugins can register commands through the existing hooks:

```php
public function register(\Webspine\App $app): void {
    $app->hooks->on('cli.register', static function (\Webspine\ConsoleCommands $commands) use ($app): void {
        $commands->register(
            'requests:list',
            'List the first 50 stored requests',
            static function (array $args) use ($app): int {
                // The plugin owns its business rules and required storage setup.
                $entities = $app->services->get(\Webspine\Contracts\Entities::class);
                echo json_encode($entities->list('requests', 50), JSON_THROW_ON_ERROR) . "\n";
                return 0;
            }
        );
    });
}
```

This illustrative handler requires an entity named `requests` declared by the
plugin and explicitly installed with `entities:install`. Registration must not
query, create, install or migrate storage. Choose a namespace belonging to your
plugin; commands are local operator tools, not HTTP routes or authentication.

## Definition and arguments

`register(string $name, string $description, callable $handler, int $minArgs = 0,
int $maxArgs = 0, string $arguments = '')` registers one command. Names have
exactly one colon: each part begins with a lowercase letter and contains up to
40 lowercase letters, digits or hyphens. Duplicate names and reserved core
commands (including `entities:install`) throw before dispatch. Descriptions
must be nonempty; description/argument usage text is valid UTF-8, at most 160
bytes each, and has no control characters. The registry is local to one CLI run.

The handler receives a list of literal strings after the command name. There is
no automatic option parsing, expansion or shell execution. For example:

```php
$commands->register('requests:show', 'Show one request',
    static function (array $args) use ($repository): int {
        $id = $args[0]; // Validate the ID before querying the plugin repository.
        echo json_encode($repository->read($id), JSON_THROW_ON_ERROR) . "\n";
        return 0;
    }, 1, 1, '<id>');
```

The registry checks argument count before invoking the handler: bounds are
0–32, with maximum at least minimum. Usage text only documents the arguments;
handlers validate values/options before work. Flags such as `--dry-run` are
ordinary strings and consume an argument. Reject unsupported options explicitly.

Handlers write results to stdout and diagnostics to stderr and return an
integer 0–125 (0 succeeds, nonzero fails). The CLI passes that status through;
it does not interpret output, invent success messages or retry failures.
Uncaught exceptions, unknown commands, invalid argument counts and invalid
return statuses produce local stderr diagnostics and status 1. A command can
have already produced output or side effects when it fails; there is no automatic
transaction or output rollback. Do not include secrets in output or exceptions.

## Discovery and bootstrap

```sh
php core/bin/console.php help
php core/bin/console.php help requests:show
php core/bin/console.php requests:show request-id
php core/bin/console.php help --core
```

Default help boots the configured App and lists core commands followed by sorted
plugin commands. Only explicitly enabled plugins/providers and their loaded
dependencies receive `cli.register`. Disabled plugins are not scanned or loaded.
Web requests never fire this CLI event. Handlers capture their own App/services
explicitly. Discovery does not invoke command handlers or run installation,
health or migrations. Commands needing no installed storage can run before
installation, provided ordinary site/plugin registration can boot without it.

`help --core` prints built-in help without booting the site, useful for broken
configuration or faulty plugin registration. Core `install`, `entities:install`,
`theme` and `health` retain their existing App bootstrap but never invoke CLI
discovery. `package`, `update`, `rollback` and `recover` retain their maintenance
bootstrap without loading site configuration/plugins or firing this event.
Plugin registration failures cannot replace recovery commands.

Plugin discovery/execution holds the same shared framework update lock as
ordinary CLI work, and refuses interrupted activation before booting the App.
This also applies to help, including `--core`; use `recover` when activation is
interrupted. The lock is released on returned/exceptional command completion.
Concurrent plugin commands may still run together: this is not a per-command,
database or singleton lock. Use provider/plugin-owned transactions and locks
where needed. Do not call the updater from a plugin command while holding this
shared lock; use the separate core maintenance commands. No command timeout or
background scheduler is provided; handlers own finite work bounds and clean up
resources before returning. Scheduling belongs to the operator.

## Queue commands and cron

When `job-queue` is enabled (directly or as a dependency), the unified CLI offers
`job-queue:install`, `job-queue:status`, `job-queue:health`, `job-queue:work`,
`job-queue:retry <job-id>` and `job-queue:prune [days]`. Installation is explicit;
help and status never install the queue. Work retains its ten-job bound and
configured completed-job retention; health retains its JSON and warning status.

```sh
php core/bin/console.php job-queue:install
php core/bin/console.php job-queue:work
php core/bin/console.php job-queue:health
```

An operator can schedule the worker every minute, using absolute paths:

```cron
* * * * * /usr/bin/php /srv/example/core/bin/console.php job-queue:work
```

Run it as the local site's runtime user/group, keep output/diagnostics private,
and monitor exit codes. See [queue operations](queue.md). The existing
`core/plugins/job-queue/cli.php` commands and separate site-owned `site/bin/`
scripts remain valid; the legacy queue CLI delegates to the same handlers.
It now rejects surplus arguments instead of silently ignoring them.
