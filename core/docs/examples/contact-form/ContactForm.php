<?php
declare(strict_types=1);
namespace Webspine\Examples;
use Webspine\{App,Request,Response,Files,HttpError};
use Webspine\Jobs\Queue;
final class ContactForm {
    private array $fields;
    private array $copy;
    private string $id = 'contact';
    private array $config;
    public function __construct(private App $app) {
        $id = $this->id;
        $config = $app->config['contact_form'] ?? [];
        if (!is_array($config)) throw new \InvalidArgumentException('Invalid contact form configuration.');
        $this->config = $config = array_replace(['path'=>'/contact-example', 'subject'=>'Website contact'], $config);
        if (!preg_match('/^[a-z][a-z0-9-]{0,39}$/D',$id)) throw new \InvalidArgumentException('Invalid form identity.');
        $path=$config['path']??null;
        if (!is_string($path) || !preg_match('~^/[a-z0-9/-]+$~D',$path) || str_contains($path,'//') || in_array($path,['/health','/_insights'],true) || str_starts_with($path,'/assets/')) throw new \InvalidArgumentException('Invalid form route.');
        if (!is_string($config['recipient']??null) || !filter_var($config['recipient'],FILTER_VALIDATE_EMAIL)) throw new \InvalidArgumentException('Configure a fixed form recipient.');
        if (!is_string($config['subject']??'Contact enquiry') || strlen($config['subject']??'Contact enquiry')>200 || preg_match('/[\r\n\x00]/',$config['subject']??'Contact enquiry')) throw new \InvalidArgumentException('Invalid subject.');
        if (isset($config['renderer']) && !is_callable($config['renderer'])) throw new \InvalidArgumentException('Renderer must be a trusted callable.');
        $this->copy=$app->site->content('contact-form');
        foreach ($this->copy as $value) if (!is_string($value) || !preg_match('//u',$value)) throw new \InvalidArgumentException('Form copy must be plain UTF-8 text.');
        $this->fields=$config['fields']??[
            'name'=>['label'=>$this->copy['name'],'type'=>'text','required'=>true,'max'=>120],
            'email'=>['label'=>$this->copy['email'],'type'=>'email','required'=>true,'max'=>254],
            'message'=>['label'=>$this->copy['message'],'type'=>'textarea','required'=>true,'min'=>10,'max'=>5000],
        ];
        if (!is_array($this->fields) || !$this->fields || count($this->fields)>20) throw new \InvalidArgumentException('Configure 1–20 form fields.');
        foreach ($this->fields as $key=>$field) {
            if (!is_string($key) || !preg_match('/^[a-z][a-z0-9_]{0,39}$/D',$key) || in_array($key,['csrf','website'],true) || !is_array($field)
                || !is_string($field['label']??null) || strlen($field['label'])>120 || !preg_match('//u',$field['label']) || preg_match('/[\x00-\x1f\x7f]/',$field['label'])
                || !in_array($field['type']??'text',['text','email','textarea'],true) || !is_bool($field['required']??false)
                || !is_int($field['min']??0) || !is_int($field['max']??5000) || ($field['min']??0)<0 || ($field['max']??5000)<1 || ($field['max']??5000)>5000 || ($field['min']??0)>($field['max']??5000)) throw new \InvalidArgumentException('Invalid field definition.');
        }
    }
    public function path(): string { return $this->config['path']; }
    public function show(Request $request): Response {
        $this->session();
        try {
            $saved=!empty($_SESSION['contact_forms'][$this->id]['saved']);unset($_SESSION['contact_forms'][$this->id]['saved']);
            return $this->render([],[],$saved?'sent':'intro');
        } finally { session_write_close(); }
    }
    public function submit(Request $request): Response {
        $form=$request->form();$this->session();
        try {
            $state=$_SESSION['contact_forms'][$this->id]??[];$token=$form['csrf']??null;
            [$fields,$errors]=$this->validate($form);
            $unknown=(bool)array_diff(array_keys($form),['csrf','website',...array_keys($this->fields)]);
            $clean=!$errors && !$unknown && is_string($form['website']??'') && ($form['website']??'')==='';
            $fingerprint=hash('sha256',json_encode($fields,JSON_THROW_ON_ERROR));
            $receipts=$state['receipts']??[];
            foreach ($receipts as $key=>$receipt) if ($receipt['expires']<=time()) unset($receipts[$key]);
            $_SESSION['contact_forms'][$this->id]['receipts']=$receipts;
            $receipt=is_string($token)?($receipts[hash('sha256',$token)]??null):null;
            if ($receipt && $clean && hash_equals($receipt['fingerprint'],$fingerprint)) {
                // A receipt proves this exact submission was saved in this session.
                $_SESSION['contact_forms'][$this->id]['saved']=true;
                return $this->success();
            }
            if (!$this->attempt($request->remoteAddress)) return new Response($this->copy['rate_error'],429,['Retry-After'=>'600','Cache-Control'=>'no-store']);
            if (!is_string($token) || !is_string($state['token']??null) || ($state['expires']??0)<=time() || !hash_equals($state['token'],$token)) {
                $expired=is_string($token) && is_string($state['token']??null) && hash_equals($state['token'],$token) && ($state['expires']??0)<=time();
                unset($_SESSION['contact_forms'][$this->id]['token'], $_SESSION['contact_forms'][$this->id]['expires']);
                return $this->render($expired?$fields:[],$expired?$errors:[],$expired?'expired':'token_error',403);
            }
            if (!is_string($form['website']??'') || ($form['website']??'')!=='') throw new HttpError(422,$this->copy['rejected']);
            if ($errors || $unknown) return $this->render($fields,$errors,'invalid',422);
            $labels=array_map(static fn(array $field): string => $field['label'],$this->fields);
            try {
                $job=$this->app->services->get(Queue::class)->enqueue('contact.deliver',2,[
                    'form_id'=>$this->id,'recipient'=>$this->config['recipient'],'subject'=>$this->config['subject']??'Contact enquiry',
                    'fields'=>$fields,'labels'=>$labels,
                ],hash('sha256',$this->id.':'.$token));
            } catch (\Throwable $e) { error_log('Contact submission could not be queued: ' . $e::class . ' at ' . $e->getFile() . ':' . $e->getLine());return $this->render($fields,[],'unavailable',503); }
            $receipts[hash('sha256',$token)]=['expires'=>time()+600,'fingerprint'=>$fingerprint,'job'=>$job];
            $_SESSION['contact_forms'][$this->id]=['saved'=>true,'receipts'=>array_slice($receipts,-3,null,true)];
            return $this->success();
        } finally { session_write_close(); }
    }
    private function success(): Response { return new Response('',303,['Location'=>$this->path(),'Cache-Control'=>'no-store']); }
    private function validate(array $form): array {
        $fields=[];$errors=[];
        foreach ($this->fields as $key=>$field) {
            $raw=$form[$key]??'';$value=is_string($raw)?trim($raw):'';
            $safe=is_string($raw) && FormText::safe($raw,($field['type']??'text')==='textarea');
            $fields[$key]=$safe?$value:'';
            $length=$safe?FormText::units($value):0;
            if (!$safe || $length>($field['max']??5000) || (($field['required']??false) && $value==='')
                || ($value!=='' && $length<($field['min']??0))
                || ($value!=='' && ($field['type']??'text')==='email' && !filter_var($value,FILTER_VALIDATE_EMAIL))) $errors[$key]=$this->copy['field_error'];
        }
        return [$fields,$errors];
    }
    private function session(): void {
        if (session_status()!==PHP_SESSION_NONE) throw new \RuntimeException('Contact form requires a separate session; integrate explicitly with an existing session.');
        if (headers_sent($file, $line)) throw new \RuntimeException('Contact session cannot start after output at ' . $file . ':' . $line . '; use an isolated HTTP/subprocess test.');
        $directory=Files::target($this->app->root,'storage/contact-forms/sessions');
        if (!is_dir($directory) && !mkdir($directory,0700,true) && !is_dir($directory)) throw new \RuntimeException('Cannot create private form sessions.');
        session_name('webspine_contact_forms');
        if (!session_start(['save_path'=>$directory,'use_strict_mode'=>1,'use_only_cookies'=>1,'cookie_httponly'=>1,'cookie_samesite'=>'Lax','cookie_secure'=>$this->config['secure_cookie']??true,'cookie_path'=>'/','gc_maxlifetime'=>1800,'gc_probability'=>1,'gc_divisor'=>100])) throw new \RuntimeException('Cannot start form session.');
    }
    private function render(array $values,array $errors,string $notice,int $status=200): Response {
        if (!isset($_SESSION['contact_forms'][$this->id]['token']) || ($_SESSION['contact_forms'][$this->id]['expires']??0)<=time()) {
            $_SESSION['contact_forms'][$this->id]['token']=bin2hex(random_bytes(32));$_SESSION['contact_forms'][$this->id]['expires']=time()+600;
        }
        $data=['title'=>$this->copy['title'],'formPath'=>$this->path(),'definitions'=>$this->fields,'fields'=>$values,'errors'=>$errors,'copy'=>$this->copy,
            'notice'=>$this->copy[$notice],'token'=>$_SESSION['contact_forms'][$this->id]['token']];
        if (isset($this->config['renderer'])) {
            $response=($this->config['renderer'])($this->app,$data,$status);
            if (!$response instanceof Response) throw new \RuntimeException('Form renderer must return a Response.');
        } else {
            $response=$this->app->theme->render('contact-form',$data,$status);
        }
        $response->status=$status;
        $response->headers['Cache-Control']='no-store';return $response;
    }
    private function attempt(?string $address): bool {
        if ($address===null || !filter_var($address,FILTER_VALIDATE_IP)) return false;
        $directory=Files::target($this->app->root,'storage/contact-forms');
        if (!is_dir($directory) && !mkdir($directory,0700,true) && !is_dir($directory)) throw new \RuntimeException('Cannot create private limiter.');
        $mask=umask(0077);
        try { $file=fopen($directory.'/rate.json','c+'); } finally { umask($mask); }
        if (!$file) throw new \RuntimeException('Cannot open limiter.');
        if (!flock($file,LOCK_EX)) { fclose($file);throw new \RuntimeException('Cannot lock limiter.'); }
        try {
            $raw=stream_get_contents($file,1048577);if ($raw===false || strlen($raw)>1048576) throw new \RuntimeException('Invalid limiter state.');
            $state=$raw===''?['salt'=>bin2hex(random_bytes(32)),'clients'=>[]]:json_decode($raw,true,16,JSON_THROW_ON_ERROR);
            if (!is_array($state) || !is_string($state['salt']??null) || !preg_match('/^[a-f0-9]{64}$/D',$state['salt']) || !is_array($state['clients']??null) || count($state['clients'])>2048) throw new \RuntimeException('Invalid limiter state.');
            $now=time();
            foreach ($state['clients'] as $key=>$record) {
                if (!is_array($record) || !is_int($record['until']??null) || !is_int($record['count']??null) || $record['count']<0) throw new \RuntimeException('Invalid limiter record.');
                if ($record['until']<=$now) unset($state['clients'][$key]);
            }
            $key=hash_hmac('sha256',$this->id.':'.$address,$state['salt']);$limit=max(1,min(20,(int)($this->config['max_attempts']??5)));
            if (!isset($state['clients'][$key]) && count($state['clients'])>=2048) return false;
            $record=$state['clients'][$key]??['until'=>$now+600,'count'=>0];$allowed=$record['count']<$limit;
            $record['count']=min($limit,$record['count']+1);$state['clients'][$key]=$record;
            $body=json_encode($state,JSON_THROW_ON_ERROR);rewind($file);
            if (!ftruncate($file,0) || fwrite($file,$body)!==strlen($body) || !fflush($file)) throw new \RuntimeException('Cannot persist limiter.');
            return $allowed;
        } finally { flock($file,LOCK_UN);fclose($file); }
    }
}
