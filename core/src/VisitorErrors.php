<?php
declare(strict_types=1);
namespace Webspine;

/** Data-only copy and standalone HTML; usable before bootstrap/App can load. */
final class VisitorErrors {
    private array $http = [];
    private array $unavailable = [
        'title'=>'webspine · unavailable',
        'message'=>'The site is temporarily unavailable.',
        'operator'=>'Site operator: run php core/bin/console.php health from the project directory and inspect the PHP error log. For initial setup only, run php core/bin/console.php install after checking configuration.',
    ];
    private string $language = 'en';
    public static function language(mixed $value): string {
        if (!is_string($value) || strlen($value)>63 || !preg_match('/^[a-zA-Z]{2,8}(?:-[a-zA-Z]{4})?(?:-(?:[a-zA-Z]{2}|[0-9]{3}))?(?:-(?:[a-zA-Z0-9]{5,8}|[0-9][a-zA-Z0-9]{3}))*$/D',$value)) throw new \InvalidArgumentException('Invalid site language; use language[-Script][-REGION][-variant], at most 63 characters.');
        return $value;
    }
    public static function load(string $root): self {
        $copy=new self();$file=$root.'/site/errors.json';
        if (!file_exists($file) && !is_link($file)) return $copy;
        if (is_link($file) || !is_file($file)) throw new \RuntimeException('site/errors.json must be a regular JSON file.');
        $raw=@file_get_contents($file,false,null,0,65537);
        if ($raw===false || strlen($raw)>65536) throw new \RuntimeException('Cannot read site/errors.json (maximum 64 KiB).');
        try { $data=json_decode($raw,false,8,JSON_THROW_ON_ERROR); }
        catch (\JsonException $e) { throw new \RuntimeException('Invalid JSON in site/errors.json.',0,$e); }
        if (!$data instanceof \stdClass) throw new \RuntimeException('site/errors.json must contain a JSON object.');
        $data=get_object_vars($data);
        if (array_diff(array_keys($data),['language','http','unavailable'])) throw new \RuntimeException('Invalid site/errors.json catalogue keys.');
        if (array_key_exists('language',$data)) $copy->language=self::language($data['language']);
        if (array_key_exists('http',$data)) {
            if (!$data['http'] instanceof \stdClass) throw new \RuntimeException('Invalid site/errors.json http map.');
            $data['http']=get_object_vars($data['http']);
            if (count($data['http'])>100) throw new \RuntimeException('Invalid site/errors.json http map.');
            foreach ($data['http'] as $status=>$entry) {
                if (!$entry instanceof \stdClass) throw new \RuntimeException('Invalid site/errors.json HTTP entry.');
                $entry=get_object_vars($entry);
                if (!preg_match('/^[45][0-9]{2}$/D',(string)$status)
                    || array_diff(array_keys($entry),['title','message']) || !isset($entry['title'],$entry['message'])) throw new \RuntimeException('Invalid site/errors.json HTTP entry.');
                $copy->http[(int)$status]=['title'=>self::text($entry['title'],200),'message'=>self::text($entry['message'],4000)];
            }
        }
        if (array_key_exists('unavailable',$data)) {
            if (!$data['unavailable'] instanceof \stdClass) throw new \RuntimeException('Invalid site/errors.json unavailable entry.');
            $data['unavailable']=get_object_vars($data['unavailable']);
            if (array_diff(array_keys($data['unavailable']),['title','message','operator'])) throw new \RuntimeException('Invalid site/errors.json unavailable entry.');
            foreach ($data['unavailable'] as $key=>$value) $copy->unavailable[$key]=self::text($value,$key==='title'?200:4000);
        }
        return $copy;
    }
    private static function text(mixed $value,int $maximum):string {
        if (!is_string($value) || strlen($value)>$maximum || !preg_match('//u',$value) || preg_match('/[\x00-\x08\x0b\x0c\x0e-\x1f\x7f]/',$value)) throw new \RuntimeException('Invalid plain-text value in site/errors.json.');
        return $value;
    }
    public function present(Response $response):Response {
        $entry=$this->http[$response->status]??null;
        if ($entry===null) return $response;
        $headers=$response->headers;
        $headers['Content-Type']='text/html; charset=utf-8';$headers['Content-Language']=$this->language;
        $headers['Cache-Control']??='no-store';
        return new Response($this->html($entry),$response->status,$headers);
    }
    private function html(array $entry):string {
        $escape=static fn(string $value):string=>htmlspecialchars($value,ENT_QUOTES|ENT_SUBSTITUTE,'UTF-8');
        return '<!doctype html><html lang="'.$escape($this->language).'"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>'.$escape($entry['title']).'</title><h1>'.$escape($entry['title']).'</h1><p>'.$escape($entry['message']).'</p>'.(isset($entry['operator'])&&$entry['operator']!==''?'<p>'.$escape($entry['operator']).'</p>':'').'</html>';
    }
    public function unavailableHtml():string { return $this->html($this->unavailable); }
    public function languageTag():string { return $this->language; }
}
