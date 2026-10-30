# Publishing a website for a user

The agent owns deployment from start to finish. The user may have no technical
background: keep commands, paths, and server details out of the conversation,
explain outcomes in plain language, and ask only what the agent cannot find
out itself. Technical reference: [deployment.md](deployment.md).

## Ask three to five deployment questions

This limit applies to deployment only; questions while building the site are
not counted. Reuse what building already settled, such as the web address,
contact email and language. Ask in plain words, one topic at a time:

1. **Hosting:** "Which hosting provider do you use, and is your web address
   registered there?" If the address points elsewhere, guide the user to
   connect it.
2. **Access:** "Please turn on SSH access there and add this key." Generate a
   dedicated key pair locally, give the user only the public key, and guide
   them click by click for their provider. Avoid passwords in chat; if the host
   allows only passwords, ask the user to enter it into the SSH prompt themselves.
3. **Permission:** "May I publish the website to your live server via SSH?"
4. **Search engines:** "Should Google and other search engines find the site
   right away, or only once you say it is finished?"
5. **Mail (only if the site sends mail):** "Which mailbox should send the
   form messages?" Ask for the provider's SMTP access in the same guided way.

Everything else (PHP version, document root, file permissions, TLS, cron,
backups) the agent determines and configures itself.

## Hosting without SSH

webspine needs SSH or SFTP for safe, verifiable updates. If the host offers
only FTP or a control panel, tell the user plainly that it cannot be automated
safely, and recommend switching to a host with SSH access and PHP 8.3+ with
`pdo_sqlite`. Do not upload by hand through a web interface.

## Recorded permission

A clear "yes" to question 3 is recorded once in private `config/local.php` of
the local project, so later deployments to the same target need no new
question:

```php
'deploy' => ['production' => [
    'host' => 'ssh.example-host.com', 'user' => 'site', 'path' => '/var/www/site',
    'url' => 'https://example.com', 'approved' => '2026-10-30',
]],
```

The permission covers only this host, user, and path. Ask again if any of
them changes or the record is missing, and remove the record when the user
withdraws permission. Never store passwords or private keys in this file or in
Git; keep keys in the user's SSH key directory.

## First deployment

1. Verify locally: `install`, `health`, `core/tests/run.php`,
   `site/tests/run.php`, and a browser check of every page.
2. Connect and inspect the server: PHP 8.3+ CLI and web worker, `pdo_sqlite`,
   `zip`, `openssl`, writable location outside the web root.
3. Upload the project, but not `.dist/`, `.tools/`, local `storage/` data or
   local `config/local.php`. Create the server's own `config/local.php`.
4. Point the document root to `public/`, enable HTTPS, and set permissions so
   the PHP worker writes only to `storage/` (see [deployment.md](deployment.md)).
5. Run `install` and `health` on the server with the web worker's PHP.
6. Check `/health`, every page, assets, and features such as the contact form.
7. Apply the answer to question 4: set `'indexable' => true` in
   `site/meta.php` and deploy it, or leave it `false` and remind the user that
   one sentence ("make the site findable") is enough later. Custom themes need
   their own indexing policy ([search indexing](deployment.md#search-indexing)).

## Later deployments

Before changing a live site, create and verify a backup of the server's
project (code, `public/`, `site/`, `config/`, `storage/` with a consistent
SQLite snapshot) stored privately outside `public/`. Upload changed files,
restart PHP workers to clear OPcache, run `health`, and recheck affected pages.
If anything fails, restore the backup and tell the user. Framework updates
follow [update.md](update.md).

## Report to the user

Say in two or three plain sentences what is now live, at which address, and
anything the user still needs to do (for example renewing a domain). Keep
technical details in the agent's own notes unless the user asks.
