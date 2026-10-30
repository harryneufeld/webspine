# Mail

Select a trusted mail provider explicitly in private configuration. Resolve it
with `$app->services->get(\Webspine\Contracts\Mail::class)`. The existing
`Mail::send($to, $subject, $body)` contract sends plain text and is unchanged.
The bundled SMTP provider keeps its configured From identity and requires TLS.

Providers may additionally implement `MailWithReplyTo`, which extends `Mail`:

```php
use Webspine\Contracts\{Mail, MailWithReplyTo, ReplyTo};

$mail = $app->services->get(Mail::class);
$replyTo = new ReplyTo('visitor@example.com', 'Visitor'); // Name is optional.
if ($mail instanceof MailWithReplyTo) {
    $mail->sendWithReplyTo($owner, $subject, $body, $replyTo);
} else {
    $mail->send($owner, $subject, $body);
}
```

`ReplyTo` accepts one bare validated email address (at most 254 bytes), plus an
optional UTF-8 display name (at most 120 bytes). Control characters, malformed
addresses and invalid UTF-8 names throw `InvalidArgumentException` before
transport. Whitespace is not automatically trimmed; normalize form input before
construction. The provider must encode the mailbox as a Reply-To header using
its mail library, without changing the recipient or configured From. SMTP uses
PHPMailer's `addReplyTo`; callers never concatenate user input into raw headers.

The capability is discovered on the existing Mail service with `instanceof`;
there is no additional provider selection or mandatory method on old providers.
API 1 is preserved. A caller decides whether lack of this capability is an
error or whether ordinary mail is acceptable. The queued contact example uses
ordinary mail as a fallback, keeping the visitor's email in the message body.

See the [queued contact example](examples/contact-form/README.md) for validated
visitor input and durable Reply-To snapshots. Replies are a mail-client action;
Reply-To does not authenticate the visitor or guarantee mail delivery. CC and
BCC are not supported.

## HTML and attachments

Providers may also implement `MailWithMessage`, which extends `Mail` and sends
one validated `MailMessage`. The bundled SMTP provider implements it.

```php
use Webspine\Contracts\{Attachment, Mail, MailMessage, MailWithMessage, ReplyTo};

$mail = $app->services->get(Mail::class);
if ($mail instanceof MailWithMessage) {
    $mail->sendMessage(new MailMessage(
        to: $owner,
        subject: 'Your invoice',
        text: "Hello,\n\nYour invoice is attached.",
        html: '<p>Hello,</p><p>Your invoice is attached.</p><img src="cid:logo" alt="">',
        replyTo: new ReplyTo('billing@example.com'), // Optional.
        attachments: [
            Attachment::fromFile($app->root . '/storage/invoices/42.pdf', 'invoice-42.pdf', 'application/pdf'),
            new Attachment('logo.png', $logoBytes, 'image/png', contentId: 'logo'),
        ],
    ));
} else {
    $mail->send($owner, 'Your invoice', "Hello,\n\nYour invoice is available.");
}
```

The plain-text `text` body is always required. With `html`, it becomes the
alternative part for clients that do not render HTML. HTML is trusted markup,
like a template: escape every visitor or content value with `e()` before
building it, and never accept raw HTML from a form. Bodies must be UTF-8 of at
most 1 MiB each. Mail clients support only basic HTML and inline styles.

`Attachment` holds the file bytes in memory with a filename and media type
(default `application/octet-stream`). Filenames are at most 255 bytes, without
control characters, quotes, slashes or backslashes. Each attachment is at most
10 MiB; a message holds at most 10 attachments and 20 MiB in total, but many
mail servers reject smaller messages, so keep them small. `Attachment::fromFile`
reads a trusted local path; never pass a path derived from visitor input. A
`contentId` embeds the file inline for `cid:` references in the HTML and
requires `html`; content IDs are unique within a message.

Invalid recipients, subjects, bodies or attachments throw
`InvalidArgumentException` before transport. The three-argument `send` and
`sendWithReplyTo` keep sending plain text, and providers without this capability
remain valid. Callers decide on a fallback, as above.
