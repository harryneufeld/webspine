# Optional queued contact form

The current copyable example always saves valid submissions to the durable
`job-queue` before email delivery. There is one delivery workflow; synchronous
sending is not a configuration mode. The starter still has no enabled form.

Copy `plugin.php`, `plugin.json`, `ContactForm.php`, and `MailDelivery.php` into
`site/plugins/contact-form/`. Copy `template.php` to `contact-form.php` in your
selected theme, its CSS to that theme's `assets/`, and `content.php` to
`site/content/contact-form.php`. Keep presentation and translated copy site-owned.
The `job-queue` dependency is bundled and loads automatically when this example
is enabled. No `core/plugins/contact-form` identity is introduced.

Enable `contact-form` in private configuration, configure the SMTP provider, and
set a fixed recipient:

```php
'plugins' => ['contact-form'],
'contact_form' => [
    'recipient' => 'you@example.com',
    'path' => '/contact-example', // Optional: this remains the default.
],
```

The validated path is used for GET/POST registration, the form action and the
success redirect. Choose a local path of lowercase letters, digits, hyphens and
slashes; root, reserved framework paths and duplicate routes are rejected.
Configure it once. Subject defaults to `Website contact` and can be overridden
with a plain, bounded `subject`. Recipient/subject snapshots are saved with each
job, so later configuration changes do not redirect existing messages.

Initialize the ordinary site normally, then install queue storage explicitly:

```sh
php core/bin/console.php install
php core/plugins/job-queue/cli.php install
php core/plugins/job-queue/cli.php status
php core/plugins/job-queue/cli.php work
```

Configure cron to invoke `work`, for example every minute using the full project
path and your PHP executable. Each invocation processes at most ten jobs. There
is no web worker endpoint and no automatic worker on form submission. See
[queue operation and guarantees](../../queue.md). `delivery_enabled => false`
pauses mail jobs without consuming attempts; delivery is otherwise ready when a
Mail provider is configured. A missing provider also leaves jobs untouched.

A successful POST queues a message and redirects with 303. Confirmation says
**saved for delivery**, rather than claiming that an email was delivered. Mail
transport runs outside the request and outside the queue's claim transaction.
Retries are at least once: a crash after SMTP acceptance may produce duplicates.
The submission ID is included in mail to identify repeats. Exactly-once SMTP
and atomic transactions with a separate domain database are not provided.

## Fields, errors and sessions

Defaults are name, email and message. An optional `fields` array defines 1–20
fields keyed by a safe identifier, with plain `label`, `type` (`text`, `email`,
`textarea`), boolean `required`, and integer byte limits `min`/`max` (up to 5,000).
This changes the versioned job payload, not the database schema. Keep user-facing
labels/copy consistent with your site. Custom trusted renderers receive
`($app, $data, $status)` and must return a Response; the controller preserves the
intended HTTP status and no-store. Normal rendering uses the selected theme's
shared layout and escapes plain values with `e()`.

Invalid, missing, expired and replayed CSRF tokens return a rendered HTTP 403
with a fresh usable token and no-store. The failed submission is never queued,
and its values are discarded. Ordinary validation errors return 422 and retain
only bounded, valid UTF-8 values, escaped by the template. A honeypot, unknown
field rejection, fixed recipient and rate limiting remain in place. Queue
failures return a recoverable 503 without reporting a successful save.

The form owns a session under `storage/contact-forms/sessions`. Secure,
HttpOnly, SameSite=Lax cookies are the default; `secure_cookie => false` is only
for local HTTP fixtures. Tokens last ten minutes and sessions are eligible for
PHP garbage collection after thirty minutes. Integrate explicitly with an
existing session/authentication system. Sessions close before response emission
and before background delivery. Early output produces an actionable private
session diagnostic; CLI session checks must run in a subprocess or HTTP fixture,
not after printing progress in the same PHP process. Cookie security is unchanged
for production to accommodate tests.

The locked rate file permits five attempts per direct peer IP in ten minutes
(`max_attempts` can be 1–20). It caps stored clients at 2,048 and stores salted
address hashes. It does not trust forwarded headers. Configure trusted real-IP
handling at the server when behind a proxy. This is local abuse protection, not
a distributed anti-spam service or privileged-action authorization.

## Existing installations

Core releases preserve customized `site/plugins/contact-form`, content and
templates. They do not silently replace an installed synchronous example, enable
a form, install queue tables, or migrate pending messages from another queue.
Adopting this example is a deliberate local site change: review/copy the new
plugin files, template and new copy keys, retain compatible private settings,
install queue storage and schedule the worker. Existing content keys including
`sent` remain, with wording changed to saved-for-delivery. Drain/migrate any old
queue and resolve route overlap before switching. No alternative synchronous
mode is maintained in this updated example.

Reply-To remains separate issue #58; the visitor email is currently in the body.
Queue capability does not implement plugin console registration (#68) or an
after-response hook (#69).

The framework checks run isolated queue and HTTP tests with captured mail only.
Hosting CI exercises the same form, queue and worker as the PHP runtime group on
Apache, nginx/FPM and CloudPanel-style routing before/after updates and rollback.
