<?php
declare(strict_types=1);
namespace Webspine\Examples;
use Webspine\Jobs\{Handler,Job,PermanentFailure};
final class MailDelivery implements Handler {
    public function __construct(private \Webspine\App $app) {}
    public function ready(): bool {
        return ($this->app->config['contact_form']['delivery_enabled']??true) === true
            && $this->app->services->has(\Webspine\Contracts\Mail::class);
    }
    public function handle(Job $job): void {
        if (!in_array($job->version,[1,2],true)) throw new PermanentFailure('unsupported_version');
        $p=$job->payload;
        if (!is_string($p['recipient']??null) || !filter_var($p['recipient'],FILTER_VALIDATE_EMAIL)
            || !is_string($p['subject']??null) || strlen($p['subject'])>200 || preg_match('/[\r\n\x00]/',$p['subject'])
            || !is_array($p['fields']??null) || !is_array($p['labels']??null) || count($p['fields'])>20
            || !is_string($p['form_id']??null) || !preg_match('/^[a-z][a-z0-9-]{0,39}$/D',$p['form_id'])) throw new PermanentFailure();
        $body="Submission ID: {$job->id}\nForm: {$p['form_id']}\n\n";
        foreach ($p['fields'] as $key=>$value) {
            if (!is_string($key) || !preg_match('/^[a-z][a-z0-9_]{0,39}$/D',$key) || !is_string($value)
                || ($job->version===1 ? strlen($value)>5000 || !preg_match('//u',$value) : !FormText::safe($value,true) || FormText::units($value)>5000)
                || !is_string($p['labels'][$key]??null) || strlen($p['labels'][$key])>120 || preg_match('/[\r\n\x00]/',$p['labels'][$key])) throw new PermanentFailure();
            $body.=$p['labels'][$key].":\n".$value."\n\n";
        }
        $this->app->services->get(\Webspine\Contracts\Mail::class)->send($p['recipient'],$p['subject'],$body);
    }
}
