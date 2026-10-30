<?php
declare(strict_types=1);
$prepare = require __DIR__.'/contact-fixture.php';
use Webspine\{App,Request};
use Webspine\Contracts\{Attachment,Mail,MailMessage,MailWithMessage,ReplyTo};
use Webspine\Jobs\Queue;
use PHPMailer\PHPMailer\PHPMailer;

$source=dirname(__DIR__,2);$root=$source.'/storage/mail-message-test-'.bin2hex(random_bytes(6));
$app=null;$queue=null;$results=[];
function messageCheck(bool $ok,string $name):void { global $results;if(!$ok)throw new RuntimeException($name);$results[]=$name; }
function invalidMessage(callable $fn):bool { try{$fn();}catch(InvalidArgumentException){return true;}return false; }
function removeMessageFixture(string $path,string $base):void {
    if(!str_starts_with(realpath($path)?:'',realpath($base).DIRECTORY_SEPARATOR))throw new RuntimeException('Unsafe cleanup.');
    foreach(new DirectoryIterator($path) as $entry){if($entry->isDot())continue;if($entry->isDir()&&!$entry->isLink())removeMessageFixture($entry->getPathname(),$base);else unlink($entry->getPathname());}rmdir($path);
}
function messageSubmit(App $app,array $fields):void {
    $get=$app->handle(new Request('GET','/inquiry'));preg_match('/name="csrf" value="([a-f0-9]{64})"/',$get->body,$m);
    $response=$app->handle(new Request('POST','/inquiry',['Content-Type'=>'application/x-www-form-urlencoded'],http_build_query(['csrf'=>$m[1],'website'=>'']+$fields),'127.0.0.1'));
    messageCheck($response->status===303,'Validated submission is queued');
}
try {
    foreach(['','.','..','a/b','a\\b',"a\r\nb",'a"b',"\xff",str_repeat('x',256)] as $name) {
        messageCheck(invalidMessage(fn()=>new Attachment($name,'data')),'Unsafe attachment filename is rejected');
    }
    foreach(['','text','text/','text/plain; charset=utf-8',"text/plain\r\nX: y"] as $type) {
        messageCheck(invalidMessage(fn()=>new Attachment('a.txt','data',$type)),'Malformed attachment media type is rejected');
    }
    messageCheck(invalidMessage(fn()=>new Attachment('a.png','data','image/png','<logo>')),'Unsafe content ID is rejected');
    messageCheck(invalidMessage(fn()=>new Attachment('big.bin',str_repeat('x',Attachment::MAX_BYTES+1))),'Oversized attachment is rejected');
    $file=$source.'/storage/mail-message-attachment-'.bin2hex(random_bytes(4)).'.txt';file_put_contents($file,'From disk');
    try { $disk=Attachment::fromFile($file,'notes.txt','text/plain'); } finally { unlink($file); }
    messageCheck($disk->content==='From disk' && $disk->filename==='notes.txt','Trusted local file becomes an attachment');
    messageCheck(invalidMessage(fn()=>Attachment::fromFile($file)),'Missing attachment file is rejected');

    $ok=new Attachment('a.txt','data','text/plain');
    foreach([
        fn()=>new MailMessage('bad','Subject','Text'),
        fn()=>new MailMessage("owner@example.test\r\nBcc: evil@example.test",'Subject','Text'),
        fn()=>new MailMessage('owner@example.test',"Subject\r\nBcc: evil@example.test",'Text'),
        fn()=>new MailMessage('owner@example.test','Subject',"\xff"),
        fn()=>new MailMessage('owner@example.test','Subject','Text',"<p>\0</p>"),
        fn()=>new MailMessage('owner@example.test','Subject','Text',attachments:['a.txt'=>$ok]),
        fn()=>new MailMessage('owner@example.test','Subject','Text',attachments:['not an attachment']),
        fn()=>new MailMessage('owner@example.test','Subject','Text',attachments:array_fill(0,11,$ok)),
        fn()=>new MailMessage('owner@example.test','Subject','Text',attachments:[new Attachment('logo.png','png','image/png','logo')]),
        fn()=>new MailMessage('owner@example.test','Subject','Text','<p>x</p>',attachments:[new Attachment('a.png','png','image/png','logo'),new Attachment('b.png','png','image/png','logo')]),
        fn()=>new MailMessage('owner@example.test','Subject','Text',attachments:array_fill(0,3,new Attachment('big.bin',str_repeat('x',Attachment::MAX_BYTES)))),
    ] as $invalid) messageCheck(invalidMessage($invalid),'Invalid message fails before transport');

    foreach(['Exception','PHPMailer','SMTP'] as $class)require_once $source.'/core/vendor/phpmailer/src/'.$class.'.php';
    require_once $source.'/core/plugins/smtp/SmtpMail.php';
    // Exercise the actual SMTP provider and PHPMailer MIME builder, replacing only transport.
    $smtp=new class(['host'=>'127.0.0.1','port'=>587,'encryption'=>'tls','username'=>'','password'=>'','from'=>'sender@example.test','from_name'=>'Site owner']) extends \Webspine\Smtp\SmtpMail {
        public array $messages=[];
        protected function mailer():PHPMailer {
            $owner=$this;
            return new class($owner) extends PHPMailer {
                public function __construct(private object $owner){parent::__construct(true);}
                public function send(){if(!$this->preSend())return false;$this->owner->messages[]=$this->getSentMIMEMessage();return true;}
            };
        }
    };
    messageCheck($smtp instanceof MailWithMessage,'SMTP provider exposes the message capability');
    $smtp->send('owner@example.test','Plain','Body');
    messageCheck(str_contains($smtp->messages[0],'Content-Type: text/plain') && !str_contains($smtp->messages[0],'multipart/'),'Three-argument send remains plain text');
    $smtp->sendMessage(new MailMessage('owner@example.test','Rich','Plain alternative','<p>Rich <strong>body</strong></p>',new ReplyTo('visitor@example.test'),[
        new Attachment('report.pdf','%PDF-1.4 sample','application/pdf'),
        new Attachment('logo.png',"\x89PNG\r\n\x1a\nlogo",'image/png','logo'),
    ]));
    $mime=$smtp->messages[1];
    messageCheck(str_contains($mime,'multipart/alternative') && str_contains($mime,'Content-Type: text/plain') && str_contains($mime,'Content-Type: text/html')
        && str_contains($mime,'Plain alternative') && str_contains($mime,'<strong>body</strong>'),'HTML is sent with its plain-text alternative');
    messageCheck(str_contains($mime,'multipart/mixed') && str_contains($mime,'Content-Type: application/pdf; name=report.pdf')
        && str_contains($mime,'Content-Disposition: attachment; filename=report.pdf') && str_contains($mime,base64_encode('%PDF-1.4 sample')),'Attachment is base64 encoded with its filename and type');
    messageCheck(str_contains($mime,'multipart/related') && str_contains($mime,'Content-ID: <logo>') && str_contains($mime,'Content-Disposition: inline'),'Content ID embeds an inline image');
    messageCheck(str_contains($mime,'Reply-To: visitor@example.test') && str_contains($mime,'From: Site owner <sender@example.test>') && str_contains($mime,'To: owner@example.test'),'Message keeps Reply-To, From and recipient');
    $smtp->send('owner@example.test','Next','Body');
    messageCheck(!str_contains($smtp->messages[2],'report.pdf') && !str_contains($smtp->messages[2],'Reply-To:'),'Later messages do not inherit attachments or Reply-To');

    $prepare($root,'/inquiry');$app=new App($root);$queue=$app->services->get(Queue::class);
    messageCheck($app->services->get(Mail::class) instanceof MailWithMessage,'Capture provider exposes the message capability through Mail');
    $fields=['name'=>'Visitor <b>bold</b>','email'=>'visitor@example.test','message'=>"First line & more\n<script>alert(1)</script>"];
    messageSubmit($app,$fields);$job=$queue->claim(['contact.deliver']);
    (new \Webspine\Examples\MailDelivery($app))->handle($job);$queue->complete($job);
    $captured=json_decode(trim(file_get_contents($root.'/storage/captured-mail.jsonl')),true,flags:JSON_THROW_ON_ERROR);
    messageCheck(str_contains($captured['body'],$fields['message']) && $captured['reply_to']['address']===$fields['email'],'Contact text body and Reply-To are unchanged');
    messageCheck(str_contains($captured['html'],'Visitor &lt;b&gt;bold&lt;/b&gt;') && str_contains($captured['html'],"First line &amp; more\n&lt;script&gt;")
        && !str_contains($captured['html'],'<script>') && !str_contains($captured['html'],'<b>bold'),'Contact HTML escapes visitor values');
    $config=require $root.'/config/local.php';$config['contact_form']['html_mail']=false;$plain=new App($root,$config);
    messageSubmit($plain,$fields);$job=$queue->claim(['contact.deliver']);
    (new \Webspine\Examples\MailDelivery($plain))->handle($job);$queue->complete($job);
    $messages=file($root.'/storage/captured-mail.jsonl',FILE_IGNORE_NEW_LINES|FILE_SKIP_EMPTY_LINES);
    messageCheck(json_decode($messages[1],true)['html']===null,'html_mail false sends plain text only');
    foreach(['yes',1,null] as $bad){$invalid=$config;$invalid['contact_form']['html_mail']=$bad;messageCheck(invalidMessage(fn()=>new App($root,$invalid)),'Invalid html_mail configuration is rejected');}
    foreach($results as $name)echo "PASS $name\n";
    echo "Mail message tests passed; MIME and capture only, no SMTP connection.\n";
}catch(Throwable $e){fwrite(STDERR,$e->getMessage()."\n".$e->getTraceAsString()."\n");$code=1;}
finally {if(session_status()===PHP_SESSION_ACTIVE)session_write_close();$queue=null;$app=null;unset($plain);gc_collect_cycles();if(is_dir($root))removeMessageFixture($root,$source.'/storage');}
exit($code??0);
