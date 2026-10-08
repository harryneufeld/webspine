<?php
declare(strict_types=1);
$prepare = require __DIR__.'/contact-fixture.php';
use Webspine\{App,Request};
use Webspine\Contracts\{Mail,MailWithReplyTo,ReplyTo};
use Webspine\Jobs\{Job,Queue,PermanentFailure};
use PHPMailer\PHPMailer\PHPMailer;

$source=dirname(__DIR__,2);$root=$source.'/storage/mail-reply-to-test-'.bin2hex(random_bytes(6));
$app=null;$db=null;$queue=null;$results=[];
function mailboxCheck(bool $ok,string $name):void { global $results;if(!$ok)throw new RuntimeException($name);$results[]=$name; }
function invalidMailbox(callable $fn):bool { try{$fn();}catch(InvalidArgumentException){return true;}return false; }
function removeMailboxFixture(string $path,string $base):void {
    if(!str_starts_with(realpath($path)?:'',realpath($base).DIRECTORY_SEPARATOR))throw new RuntimeException('Unsafe cleanup.');
    foreach(new DirectoryIterator($path) as $entry){if($entry->isDot())continue;if($entry->isDir()&&!$entry->isLink())removeMailboxFixture($entry->getPathname(),$base);else unlink($entry->getPathname());}rmdir($path);
}
function mailboxSubmit(App $app,array $fields):void {
    $get=$app->handle(new Request('GET','/inquiry'));preg_match('/name="csrf" value="([a-f0-9]{64})"/',$get->body,$m);
    $response=$app->handle(new Request('POST','/inquiry',['Content-Type'=>'application/x-www-form-urlencoded'],http_build_query(['csrf'=>$m[1],'website'=>'']+$fields),'127.0.0.1'));
    mailboxCheck($response->status===303,'Validated submission is queued');
}
try {
    foreach(['bad',"visitor@example.test\r\nBcc: other@example.test","visitor@example.test\0",'Visitor <visitor@example.test>'] as $address) {
        mailboxCheck(invalidMailbox(fn()=>new ReplyTo($address)),'Malformed or injected mailbox is rejected');
    }
    foreach(["Visitor\r\nBcc: other@example.test","Visitor\0",str_repeat('x',121),"\xff"] as $name) {
        mailboxCheck(invalidMailbox(fn()=>new ReplyTo('visitor@example.test',$name)),'Unsafe or oversized display name is rejected');
    }
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
    $smtp->send('owner@example.test','Plain subject','Body');
    mailboxCheck(!str_contains($smtp->messages[0],'Reply-To:'),'Omitted Reply-To adds no header');
    $smtp->sendWithReplyTo('owner@example.test','Inquiry','Body',new ReplyTo('visitor@example.test','Visitor'));
    $mime=$smtp->messages[1];
    mailboxCheck(str_contains($mime,'Reply-To: Visitor <visitor@example.test>') && str_contains($mime,'From: Site owner <sender@example.test>')
        && str_contains($mime,'To: owner@example.test') && !str_contains($mime,'Bcc:'),'PHPMailer sets only the intended Reply-To and preserves From and recipient');
    $smtp->sendWithReplyTo('owner@example.test','Inquiry','Body',new ReplyTo('second@example.test','Änne'));
    mailboxCheck(str_contains($smtp->messages[2],'second@example.test') && !str_contains($smtp->messages[2],'visitor@example.test'),'UTF-8 names work and subsequent messages do not inherit previous Reply-To');
    mailboxCheck(invalidMailbox(fn()=>$smtp->send('bad','Subject','Body')) && invalidMailbox(fn()=>$smtp->send('owner@example.test',"Hello\r\nBcc: evil",'Body'))
        && count($smtp->messages)===3,'Invalid recipient and subject fail before transport');

    $prepare($root,'/inquiry');$app=new App($root);$queue=$app->services->get(Queue::class);$db=new PDO('sqlite:'.$root.'/storage/job-queue/jobs.sqlite');
    mailboxCheck($app->services->get(Mail::class) instanceof MailWithReplyTo,'Capture provider exposes the optional capability through Mail');
    $fields=['name'=>'Visitor','email'=>'visitor@example.test','message'=>'A queued inquiry message.'];
    mailboxSubmit($app,$fields);
    $job=$queue->claim(['contact.deliver']);
    mailboxCheck($job->payload['reply_to']===$fields['email'],'Visitor Reply-To is snapshotted in the durable job');
    $config=require $root.'/config/local.php';$config['contact_form']['reply_to_field']=null;$disabled=new App($root,$config);
    (new \Webspine\Examples\MailDelivery($disabled))->handle($job);$queue->complete($job);
    $captured=json_decode(trim(file_get_contents($root.'/storage/captured-mail.jsonl')),true,flags:JSON_THROW_ON_ERROR);
    mailboxCheck($captured['reply_to']['address']===$fields['email'] && $captured['to']==='owner@example.test','Later configuration preserves queued Reply-To and fixed delivery destination');
    mailboxSubmit($disabled,$fields);$job=$queue->claim(['contact.deliver']);
    mailboxCheck(!array_key_exists('reply_to',$job->payload),'Explicit null disables new Reply-To headers');
    (new \Webspine\Examples\MailDelivery($disabled))->handle($job);$queue->complete($job);
    $messages=file($root.'/storage/captured-mail.jsonl',FILE_IGNORE_NEW_LINES|FILE_SKIP_EMPTY_LINES);
    mailboxCheck(json_decode($messages[1],true)['reply_to']===null,'Disabled Reply-To sends ordinary mail');
    $config['contact_form']['fields']['sender']=$config['contact_form']['fields']['email'];unset($config['contact_form']['fields']['email']);
    unset($config['contact_form']['reply_to_field']);$customDefault=new App($root,$config);
    mailboxSubmit($customDefault,['sender'=>'custom@example.test']+array_diff_key($fields,['email'=>true]));$job=$queue->claim(['contact.deliver']);
    mailboxCheck(!isset($job->payload['reply_to']),'Custom forms without an email key retain compatibility');$queue->complete($job);
    $config['contact_form']['reply_to_field']='sender';$custom=new App($root,$config);
    mailboxSubmit($custom,['sender'=>'custom@example.test']+array_diff_key($fields,['email'=>true]));$job=$queue->claim(['contact.deliver']);
    mailboxCheck($job->payload['reply_to']==='custom@example.test','Configured email field is used');$queue->complete($job);
    $config['contact_form']['fields']['sender']['required']=false;$optional=new App($root,$config);
    mailboxSubmit($optional,['sender'=>'']+array_diff_key($fields,['email'=>true]));$job=$queue->claim(['contact.deliver']);
    mailboxCheck(!isset($job->payload['reply_to']),'Empty optional email omits Reply-To');$queue->complete($job);
    foreach(['missing','name',false,[],1] as $bad){$invalid=$config;$invalid['contact_form']['reply_to_field']=$bad;mailboxCheck(invalidMailbox(fn()=>new App($root,$invalid)),'Invalid Reply-To field configuration is rejected');}

    \Webspine\Files::json($root.'/site/plugins/legacy-mail/plugin.json',['id'=>'legacy-mail','version'=>'0.1.0','api'=>1,'provider'=>'mail','dependencies'=>[]]);
    \Webspine\Files::write($root.'/site/plugins/legacy-mail/plugin.php', '<?php return new class implements \\Webspine\\Contracts\\Plugin {
        public function register(\\Webspine\\App $app):void {$app->services->set(\\Webspine\\Contracts\\Mail::class,
            new class implements \\Webspine\\Contracts\\Mail {public array $messages=[];public function send(string $to,string $subject,string $body):void{$this->messages[]=[$to,$subject,$body];}});}};');
    $config['providers']['mail']='legacy-mail';$legacyApp=new App($root,$config);
    $legacy=$legacyApp->services->get(Mail::class);$delivery=new \Webspine\Examples\MailDelivery($legacyApp);
    $payload=['form_id'=>'contact','recipient'=>'owner@example.test','subject'=>'Inquiry','fields'=>$fields,'labels'=>['name'=>'Name','email'=>'Email','message'=>'Message'],'reply_to'=>'visitor@example.test'];
    $delivery->handle(new Job('legacy-provider','contact.deliver',2,$payload,1,5,'unused'));
    mailboxCheck(count($legacy->messages)===1 && str_contains($legacy->messages[0][2],'visitor@example.test'),'Unchanged three-argument provider receives body fallback');
    unset($payload['reply_to']);
    foreach([1,2] as $version)$delivery->handle(new Job('old-job','contact.deliver',$version,$payload,1,5,'unused'));
    mailboxCheck(count($legacy->messages)===3,'Previously saved version 1 and 2 jobs remain deliverable without Reply-To');
    foreach(["visitor@example.test\r\nBcc: evil@example.test",'bad','',null,['address'=>'visitor@example.test']] as $hostile){
        $payload['reply_to']=$hostile;
        try{$delivery->handle(new Job('hostile','contact.deliver',2,$payload,1,5,'unused'));throw new LogicException('Hostile job accepted.');}catch(PermanentFailure){}
    }
    mailboxCheck(count($legacy->messages)===3,'Hostile queued Reply-To fails permanently before either provider send method');
    foreach($results as $name)echo "PASS $name\n";
    echo "Mail Reply-To tests passed; MIME and capture only, no SMTP connection.\n";
}catch(Throwable $e){fwrite(STDERR,$e->getMessage()."\n".$e->getTraceAsString()."\n");$code=1;}
finally {if(session_status()===PHP_SESSION_ACTIVE)session_write_close();$db=null;$queue=null;$app=null;unset($disabled,$customDefault,$custom,$optional,$delivery,$legacyApp);gc_collect_cycles();if(is_dir($root))removeMailboxFixture($root,$source.'/storage');}
exit($code??0);
