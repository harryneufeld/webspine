<?php
declare(strict_types=1);
// Disposable local fixtures only. Never copy private configuration or live data.
require_once dirname(__DIR__).'/bootstrap.php';
function contactCopy(string $source,string $destination): void {
    if(!is_dir($destination))mkdir($destination,0700,true);
    foreach(new DirectoryIterator($source) as $entry){
        if($entry->isDot())continue;
        if($entry->isLink())throw new RuntimeException('Fixture sources may not contain links.');
        $target=$destination.'/'.$entry->getFilename();
        if($entry->isDir())contactCopy($entry->getPathname(),$target);else copy($entry->getPathname(),$target);
    }
}
return static function(string $root,string $path='/contact-example'): void {
    $source=dirname(__DIR__,2);$storage=realpath($source.'/storage');
    if(is_dir($root)||realpath(dirname($root))!==$storage)throw new RuntimeException('Expected a new fixture directly inside development storage.');
    foreach(['core','public'] as $directory)contactCopy($source.'/'.$directory,$root.'/'.$directory);
    contactCopy(__DIR__.'/fixtures/website/site',$root.'/site');
    mkdir($root.'/storage');mkdir($root.'/config');
    $example=$source.'/core/docs/examples/contact-form';
    foreach(['plugin.php','plugin.json','FormText.php','ContactForm.php','MailDelivery.php'] as $file){
        \Webspine\Files::write($root.'/site/plugins/contact-form/'.$file,file_get_contents($example.'/'.$file));
    }
    foreach(['template.php'=>'site/themes/test-theme/contact-form.php','content.php'=>'site/content/contact-form.php','contact-form.css'=>'site/themes/test-theme/assets/contact-form.css'] as $file=>$target){
        \Webspine\Files::write($root.'/'.$target,file_get_contents($example.'/'.$file));
    }
    rename($root.'/site/routes.php',$root.'/site/base-routes.php');
    copy(__DIR__.'/hosting/routes.php',$root.'/site/routes.php');
    $config=['providers'=>['storage'=>'sqlite','mail'=>'capture-mail'],'plugins'=>['contact-form'],'sqlite'=>['path'=>'storage/site.sqlite'],
        'contact_form'=>['recipient'=>'owner@example.test','secure_cookie'=>false,'max_attempts'=>20]];
    \Webspine\Files::write($root.'/config/example.php','<?php return '.var_export($config,true).';');
    $app=new \Webspine\App($root,$config);$app->install();
    foreach(['/health','/assets/test','//contact','https://example.test/contact'] as $badPath){
        $bad=$config;$bad['contact_form']['path']=$badPath;
        try{new \Webspine\App($root,$bad);throw new LogicException('Invalid form path accepted.');}catch(InvalidArgumentException){}
    }
    if($app->router->dispatch('PUT','/contact-example')->status!==405)throw new RuntimeException('Default route changed.');
    if(is_file($root.'/storage/job-queue/jobs.sqlite'))throw new RuntimeException('Registration must not install queue storage.');
    $app->services->get(\Webspine\Jobs\Queue::class)->install();
    $badJob=new \Webspine\Jobs\Job('test','contact.deliver',99,[],1,5,'unused');
    try{(new \Webspine\Examples\MailDelivery($app))->handle($badJob);throw new LogicException('Invalid payload version accepted.');}catch(\Webspine\Jobs\PermanentFailure){}
    $payload=['form_id'=>'contact','recipient'=>'owner@example.test','subject'=>'Legacy job','fields'=>['message'=>'Legacy queued message.'],'labels'=>['message'=>'Message']];
    (new \Webspine\Examples\MailDelivery($app))->handle(new \Webspine\Jobs\Job('legacy','contact.deliver',1,$payload,1,5,'unused'));
    $captured=json_decode(trim(file_get_contents($root.'/storage/captured-mail.jsonl')),true,flags:JSON_THROW_ON_ERROR);
    if(!str_contains($captured['body'],$payload['fields']['message']))throw new RuntimeException('Legacy queued payload was not delivered.');
    unlink($root.'/storage/captured-mail.jsonl');
    foreach([1,2] as $version){
        $payload['fields']['message']=str_repeat('ä',5001);
        try{(new \Webspine\Examples\MailDelivery($app))->handle(new \Webspine\Jobs\Job('oversized','contact.deliver',$version,$payload,1,5,'unused'));throw new LogicException('Oversized delivery payload accepted.');}catch(\Webspine\Jobs\PermanentFailure){}
    }
    $config['contact_form']['path']=$path;
    $config['contact_form']['fields']=[
        'name'=>['label'=>'Your name','type'=>'text','required'=>true,'max'=>120],
        'email'=>['label'=>'Your email','type'=>'email','required'=>true,'max'=>254],
        'message'=>['label'=>'Your message','type'=>'textarea','required'=>true,'min'=>10,'max'=>5000],
        'company'=>['label'=>'Company','type'=>'text','required'=>false,'max'=>200],
    ];
    \Webspine\Files::write($root.'/config/local.php','<?php return '.var_export($config,true).';');
    \Webspine\Files::write($root.'/storage/hosting-test-fixture','Disposable contact/queue fixture');
    unset($app);gc_collect_cycles();
};
