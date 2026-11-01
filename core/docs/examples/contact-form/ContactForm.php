<?php
declare(strict_types=1);
namespace Webspine\Examples;
use Webspine\{App,Request,Response,HttpError};
use Webspine\Forms\{FormGuard,FormResult,FormText};
use Webspine\Jobs\Queue;
final class ContactForm {
    private array $fields;
    private array $copy;
    private string $id = 'contact';
    private array $config;
    private int $maxAttempts;
    private ?string $replyToField;
    private FormGuard $guard;
    public function __construct(private App $app) {
        $id = $this->id;
        $config = $app->config['contact_form'] ?? [];
        if (!is_array($config)) throw new \InvalidArgumentException('Invalid contact form configuration.');
        $this->config = $config = array_replace(['path'=>'/contact-example', 'subject'=>'Website contact'], $config);
        $retry=$config['retry']??[];
        if(!is_array($retry) || array_diff(array_keys($retry),['max_attempts'])) throw new \InvalidArgumentException('Configure contact retry max_attempts as an integer 1–100.');
        $retry=array_replace(['max_attempts'=>30],$retry);
        if(!is_int($retry['max_attempts']) || $retry['max_attempts']<1 || $retry['max_attempts']>100) throw new \InvalidArgumentException('Configure contact retry max_attempts as an integer 1–100.');
        $this->maxAttempts=$retry['max_attempts'];
        if (!preg_match('/^[a-z][a-z0-9-]{0,39}$/D',$id)) throw new \InvalidArgumentException('Invalid form identity.');
        $path=$config['path']??null;
        if (!is_string($path) || !preg_match('~^/[a-z0-9/-]+$~D',$path) || str_contains($path,'//') || in_array($path,['/health','/_insights'],true) || str_starts_with($path,'/assets/')) throw new \InvalidArgumentException('Invalid form route.');
        if (!is_string($config['recipient']??null) || !filter_var($config['recipient'],FILTER_VALIDATE_EMAIL)) throw new \InvalidArgumentException('Configure a fixed form recipient.');
        if (!is_string($config['subject']??'Contact enquiry') || strlen($config['subject']??'Contact enquiry')>200 || preg_match('/[\r\n\x00]/',$config['subject']??'Contact enquiry')) throw new \InvalidArgumentException('Invalid subject.');
        if (array_key_exists('html_mail',$config) && !is_bool($config['html_mail'])) throw new \InvalidArgumentException('Configure html_mail as a boolean.');
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
        $replyToField = array_key_exists('reply_to_field', $config) ? $config['reply_to_field']
            : ((($this->fields['email']['type'] ?? null) === 'email') ? 'email' : null);
        if ($replyToField !== null && (!is_string($replyToField) || !isset($this->fields[$replyToField])
            || ($this->fields[$replyToField]['type'] ?? null) !== 'email')) throw new \InvalidArgumentException('Reply-To field must name an email field, or be null.');
        $this->replyToField = $replyToField;
        $this->guard = new FormGuard($app->root,$id,['path'=>$path,'fields'=>array_keys($this->fields),'token_ttl'=>600,'secure_cookie'=>$config['secure_cookie']??true,
            'min_seconds'=>$config['min_seconds']??0,'max_attempts'=>max(1,min(20,(int)($config['max_attempts']??5))),'window'=>600]);
    }
    public function path(): string { return $this->config['path']; }
    public function show(Request $request): Response {
        return $this->guard->show(fn(FormResult $form): Response => $this->render([],[],$form->flash==='sent'?'sent':'intro',$form->token));
    }
    public function submit(Request $request): Response {
        return $this->guard->submit($request,function(FormResult $form): Response {
            if ($form->reason==='limited') return new Response($this->copy['rate_error'],429);
            if ($form->reason==='replayed') return $this->success();
            if ($form->reason==='invalid') return $this->render([],[],'token_error',$form->token,403);
            if ($form->reason==='rejected') throw new HttpError(422,$this->copy['rejected']);
            [$fields,$errors]=$this->validate($form->fields);
            if ($form->reason==='expired') return $this->render($fields,$errors,'expired',$form->token,403);
            if ($form->reason==='toofast') return $this->render($fields,$errors,'too_fast',$form->token,422);
            if ($errors) return $this->render($fields,$errors,'invalid',$form->token,422);
            $labels=array_map(static fn(array $field): string => $field['label'],$this->fields);
            $replyTo = $this->replyToField === null ? '' : $fields[$this->replyToField];
            $payload = ['form_id'=>$this->id,'recipient'=>$this->config['recipient'],'subject'=>$this->config['subject']??'Contact enquiry',
                'fields'=>$fields,'labels'=>$labels];
            if ($replyTo !== '') $payload['reply_to'] = $replyTo;
            try {
                $this->app->services->get(Queue::class)->enqueue('contact.deliver',2,$payload,hash('sha256',$this->id.':'.$form->token),maxAttempts:$this->maxAttempts);
            } catch (\Throwable $e) { error_log('Contact submission could not be queued: ' . $e::class . ' at ' . $e->getFile() . ':' . $e->getLine());return $this->render($fields,[],'unavailable',$form->token,503); }
            $form->complete('sent');
            return $this->success();
        });
    }
    private function success(): Response { return new Response('',303,['Location'=>$this->path()]); }
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
    private function render(array $values,array $errors,string $notice,string $token,int $status=200): Response {
        $data=['title'=>$this->copy['title'],'formPath'=>$this->path(),'definitions'=>$this->fields,'fields'=>$values,'errors'=>$errors,'copy'=>$this->copy,
            'notice'=>$this->copy[$notice],'token'=>$token];
        if (isset($this->config['renderer'])) {
            $response=($this->config['renderer'])($this->app,$data,$status);
            if (!$response instanceof Response) throw new \RuntimeException('Form renderer must return a Response.');
        } else {
            $response=$this->app->theme->render('contact-form',$data,$status);
        }
        $response->status=$status;
        return $response;
    }
}
