# Optional queued contact form

The current copyable example always saves valid submissions to the durable
`job-queue` before email delivery. There is one delivery workflow; synchronous
sending is not a configuration mode. The starter still has no enabled form.

Copy `plugin.php`, `plugin.json`, `FormText.php`, `ContactForm.php`, and `MailDelivery.php` into
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
    'retry' => ['max_attempts' => 30], // Optional: this is the default.
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
Schedule private queue monitoring as well: `php core/plugins/job-queue/cli.php
health` reports overdue/failed jobs and worker activity, with a nonzero exit
status when attention is needed. It defaults to a 15-minute overdue threshold;
see the queue guide for configuration and warning categories. Saved inquiries
still require an operational worker and mail provider; a running cron alone
does not prove delivery. Keep diagnostics private and use external monitoring.

A successful POST queues a message and redirects with 303. Confirmation says
**saved for delivery**, rather than claiming that an email was delivered. Mail
transport runs outside the request and outside the queue's claim transaction.
Retries are at least once: a crash after SMTP acceptance may produce duplicates.
The submission ID is included in mail to identify repeats. Exactly-once SMTP
and atomic transactions with a separate domain database are not provided.

## Retry window

New contact jobs default to 30 total delivery attempts. Configure
`contact_form.retry.max_attempts` as an integer 1–100; unknown retry keys and
invalid values fail at registration before any submission is saved. The bundled
queue waits 1, 2, 4, 8, 16 and 32 minutes between initial failures, then caps
each further wait at one hour. With 30 attempts, the final attempt is scheduled
24 hours 3 minutes after the first attempt, excluding delivery time and cron
delays. Five attempts span only 15 minutes. One attempt means no automatic retry.
Paused or unavailable handlers consume no attempts.

The selected limit is stored in each job's existing envelope. Configuration
changes and core updates do not rewrite old jobs; an existing five-attempt job
retains five attempts, including after an explicit retry. Dedupe returns the
original job and its original limit. No schema or payload-version change is
needed. The updated example requires bundled `job-queue` 0.3.0 or newer; apply a
compatible core release before adopting the example files.

Retries remain finite and do not guarantee delivery. Permanent payload failures
stop immediately. Continue monitoring the queue and review/retry terminal
failures with the private CLI; a longer retry window does not provide
exactly-once SMTP delivery.

## Fields, errors and sessions

Defaults are name, email and message. An optional `fields` array defines 1–20
fields keyed by a safe identifier, with plain `label`, `type` (`text`, `email`,
`textarea`), boolean `required`, and integer UTF-16 code-unit limits `min`/`max`
(up to 5,000), matching native HTML `maxlength`. Umlauts count as one; astral
emoji count as two; combining marks count separately. No `mbstring` or `intl`
extension is required. Each redisplayed value has a separate 20,000-byte cap;
the request and queue payload limits still apply. Existing custom byte-based
limits should be reviewed when adopting this version.
New jobs use payload version 2; delivery continues to accept version 1 jobs with
their original byte limits. No database migration is needed. Keep user-facing
labels/copy consistent with your site. Custom trusted renderers receive
`($app, $data, $status)` and must return a Response; the controller preserves the
intended HTTP status and no-store. Normal rendering uses the selected theme's
shared layout and escapes plain values with `e()`.

Missing or foreign CSRF tokens return a rendered HTTP 403 with a fresh usable
token and no-store; their input is discarded. A matching expired session token
also returns 403 and queues nothing, but preserves safe field values and asks
the visitor to review and submit with the fresh token. Ordinary validation errors
return 422 and retain safe UTF-8 text even above the field's min/max limit, up to
the hard byte cap. Malformed, control-character or hard-byte-limit values are
discarded. All redisplayed values are escaped by the template.

After a successful save, the session keeps up to three submission receipts for
ten minutes. An exact replay of the same validated fields and token in that
session returns the success redirect without adding another job, including
concurrent double submissions. This receipt check does not consume another rate
attempt. Changed fields, unknown/expired receipts and another session do not
qualify. The success notice is shown again after a recognized replay. Receipts
hold only hashes, expiry and job IDs, not submitted text. This handles HTTP
resubmission; it does not guarantee exactly-once SMTP delivery.

A honeypot, unknown
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
plugin files (including `FormText.php`), template and new copy keys (including
`expired`), retain compatible private settings,
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
