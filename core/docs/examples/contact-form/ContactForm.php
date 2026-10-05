<?php
declare(strict_types=1);
namespace Webspine\Examples;
use Webspine\{App, Files, HttpError, Request, Response};
use Webspine\Contracts\Mail;
/** Copyable public-contact example, not authentication or a general form framework. */
final class ContactForm {
    public function __construct(private App $app) {}
    public function show(Request $request): Response {
        $this->startSession();
        try {
            $sent = !empty($_SESSION['contact_sent']); unset($_SESSION['contact_sent']);
            return $this->render([], [], $sent ? 'sent' : 'intro');
        } finally { session_write_close(); }
    }
    public function submit(Request $request): Response {
        if (!$this->attempt($request->remoteAddress)) return new Response('Too many attempts. Try again later.', 429, ['Retry-After'=>'600','Cache-Control'=>'no-store']);
        $form = $request->form();
        $this->startSession();
        try {
            $token = $form['csrf'] ?? null;
            if (!is_string($token) || !isset($_SESSION['contact_token'], $_SESSION['contact_expires']) || $_SESSION['contact_expires'] < time() || !hash_equals($_SESSION['contact_token'], $token)) throw new HttpError(403, 'Invalid or expired form token. Reload the form.');
            if (!is_string($form['website'] ?? '') || ($form['website'] ?? '') !== '') throw new HttpError(422, 'Submission rejected.');
            $copy = $this->app->site->content('contact-form');
            $fields = []; $errors = [];
            foreach (['name','email','message'] as $key) $fields[$key] = is_string($form[$key] ?? null) ? trim($form[$key]) : '';
            if ($fields['name'] === '' || strlen($fields['name']) > 120 || !preg_match('//u', $fields['name']) || preg_match('/[\x00-\x1f\x7f]/', $fields['name'])) $errors['name'] = $copy['name_error'];
            if (strlen($fields['email']) > 254 || !filter_var($fields['email'], FILTER_VALIDATE_EMAIL)) $errors['email'] = $copy['email_error'];
            if (strlen($fields['message']) < 10 || strlen($fields['message']) > 5000 || !preg_match('//u', $fields['message']) || preg_match('/[\x00-\x08\x0b\x0c\x0e-\x1f\x7f]/', $fields['message'])) $errors['message'] = $copy['message_error'];
            if ($errors || array_diff(array_keys($form), ['csrf','name','email','message','website'])) return $this->render($fields, $errors, 'invalid', 422);
            $recipient = $this->app->config['contact_form']['recipient'] ?? null;
            if (!is_string($recipient) || !filter_var($recipient, FILTER_VALIDATE_EMAIL) || !$this->app->services->has(Mail::class)) return new Response('Contact service unavailable.',503,['Cache-Control'=>'no-store']);
            // Only the configured recipient and static subject reach mail headers.
            $this->app->services->get(Mail::class)->send($recipient, 'Website contact', "Name: {$fields['name']}\nEmail: {$fields['email']}\n\n{$fields['message']}");
            unset($_SESSION['contact_token'], $_SESSION['contact_expires']);
            $_SESSION['contact_sent'] = true;
            return new Response('',303,['Location'=>'/contact-example','Cache-Control'=>'no-store']);
        } finally { session_write_close(); }
    }
    private function startSession(): void {
        if (session_status() !== PHP_SESSION_NONE) throw new \RuntimeException('Contact example requires its own session; integrate explicitly with an existing session.');
        $directory = Files::target($this->app->root, 'storage/contact-form/sessions');
        if (!is_dir($directory) && !mkdir($directory,0700,true) && !is_dir($directory)) throw new \RuntimeException('Cannot create private session directory.');
        session_name('webspine_contact');
        if (!session_start(['save_path'=>$directory,'use_strict_mode'=>1,'use_only_cookies'=>1,'cookie_httponly'=>1,'cookie_samesite'=>'Lax','cookie_secure'=>$this->app->config['contact_form']['secure_cookie'] ?? true,'cookie_path'=>'/','gc_maxlifetime'=>1800,'gc_probability'=>1,'gc_divisor'=>100])) throw new \RuntimeException('Cannot start contact session.');
    }
    private function render(array $fields, array $errors, string $notice, int $status = 200): Response {
        if (!isset($_SESSION['contact_token']) || ($_SESSION['contact_expires'] ?? 0) < time()) {
            $_SESSION['contact_token'] = bin2hex(random_bytes(32)); $_SESSION['contact_expires'] = time() + 600;
        }
        $copy = $this->app->site->content('contact-form');
        $response = $this->app->theme->render('contact-form', ['title'=>$copy['title'],'copy'=>$copy,'fields'=>$fields,'errors'=>$errors,'notice'=>$copy[$notice],'token'=>$_SESSION['contact_token']], $status);
        $response->headers['Cache-Control'] = 'no-store';
        return $response;
    }
    private function attempt(?string $address): bool {
        // Direct peer only: never trust client-supplied Forwarded/X-Forwarded-For.
        if ($address === null || !filter_var($address,FILTER_VALIDATE_IP)) return false;
        $directory = Files::target($this->app->root, 'storage/contact-form');
        if (!is_dir($directory) && !mkdir($directory,0700,true) && !is_dir($directory)) throw new \RuntimeException('Cannot create rate-limit directory.');
        $file = fopen(Files::target($this->app->root,'storage/contact-form/rate.json'),'c+');
        if (!$file || !flock($file,LOCK_EX)) throw new \RuntimeException('Cannot lock contact limiter.');
        try {
            $raw = stream_get_contents($file, 1048577);
            if ($raw === false || strlen($raw)>1048576) throw new \RuntimeException('Invalid limiter state.');
            $state = $raw === '' ? ['salt'=>bin2hex(random_bytes(32)),'clients'=>[]] : json_decode($raw,true,16,JSON_THROW_ON_ERROR);
            if (!is_array($state) || !is_string($state['salt'] ?? null) || !preg_match('/^[a-f0-9]{64}$/D',$state['salt']) || !is_array($state['clients'] ?? null) || count($state['clients'])>2048) throw new \RuntimeException('Invalid limiter state.');
            $now = time();
            foreach ($state['clients'] as $key=>$record) {
                if (!is_array($record) || !is_int($record['until'] ?? null) || !is_int($record['count'] ?? null) || $record['count']<0) throw new \RuntimeException('Invalid limiter record.');
                if ($record['until'] <= $now) unset($state['clients'][$key]);
            }
            $key = hash_hmac('sha256',$address,$state['salt']);
            $limit = max(1,min(20,(int)($this->app->config['contact_form']['max_attempts'] ?? 5)));
            $allowed = false;
            if (isset($state['clients'][$key]) || count($state['clients']) < 2048) {
                $record = $state['clients'][$key] ?? ['until'=>$now+600,'count'=>0];
                $allowed = $record['count'] < $limit;
                $record['count'] = min($limit,$record['count']+1); $state['clients'][$key] = $record;
            }
            $body = json_encode($state,JSON_THROW_ON_ERROR);
            rewind($file);
            if (!ftruncate($file,0) || fwrite($file,$body) !== strlen($body) || !fflush($file)) throw new \RuntimeException('Cannot persist contact limiter.');
            return $allowed;
        } finally { flock($file,LOCK_UN); fclose($file); }
    }
}
