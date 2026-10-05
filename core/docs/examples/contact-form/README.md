# Optional contact form

This copyable example adds `/contact-example`; it is disabled in the starter.
Copy `plugin.php`, `plugin.json`, and `ContactForm.php` to
`site/plugins/contact-form/`; copy `template.php` as `contact-form.php` in your
selected theme, its CSS to that theme's `assets/`, and `content.php` to
`site/content/contact-form.php`. The template loads its scoped stylesheet.
Customize copy and presentation in those site-owned files.

Enable `contact-form` in private configuration, select the configured SMTP mail
provider, and add:

```php
'contact_form' => ['recipient' => 'you@example.com'],
```

The recipient and subject are fixed; visitor input is plain message text, never
a mail header. GET renders the form; POST validates fields, session CSRF, a
honeypot, and allowed fields before sending through the Mail contract. Success
uses a 303 redirect. Errors retain escaped input. Responses are not cached.

Use HTTPS: cookies default to Secure, HttpOnly, SameSite=Lax. Set
`secure_cookie => false` only for local HTTP testing. The example owns a PHP
session; integrate its token/flash handling explicitly if the site already has
a session/authentication system. Public inquiries are deliberately anonymous;
this grants no editing privileges. Add authentication and authorization for
private actions.

Private `storage/contact-form/` must be writable. Session tokens expire after
10 minutes; sessions are eligible for PHP garbage collection after 30 minutes.
A locked, bounded rate file allows five attempts per peer IP per 10 minutes,
including rejected submissions; expired entries are removed on later attempts.
It stores salted address hashes and caps clients at 2,048. Cookie rotation and
untrusted forwarded headers do not bypass the limit. Behind a proxy, configure
trusted real-IP handling at the webserver or clients may share one limit.
This is modest local abuse protection, not a distributed anti-spam service.
Mail failures return an error; retries are not guaranteed to be exactly once.

The hosting suite uses a fake mail provider and tests rejection, valid delivery,
token replay, cookies, redirects, and rate limits without contacting SMTP.
