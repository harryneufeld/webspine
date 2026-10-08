# Mail and Reply-To

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
Reply-To does not authenticate the visitor or guarantee mail delivery. HTML,
attachments, CC and BCC are outside this capability.
