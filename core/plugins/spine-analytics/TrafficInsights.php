<?php
declare(strict_types=1);
namespace Webspine\Providers\SpineAnalytics;
final class TrafficInsights {
    public const KINDS=['browser','ai-training','ai-search','ai-user','search-bot','other-bot','unknown'];
    private string $path;
    public function __construct(private string $root, private array $pages=['/'], private bool $deviceMetrics=false) {
        foreach ($pages as $page) {
            if (!is_string($page) || !str_starts_with($page,'/') || strpbrk($page,'?#')!==false || strlen($page)>200) {
                throw new \InvalidArgumentException('Analytics pages must be explicit paths without query strings or fragments.');
            }
        }
        $this->path=$root.'/storage/spine-analytics/counts.sqlite';
    }
    private function db(): \PDO {
        if (!is_file($this->path)) throw new \RuntimeException('Initialize traffic insights first.');
        $db=new \PDO('sqlite:'.$this->path,null,null,[\PDO::ATTR_ERRMODE=>\PDO::ERRMODE_EXCEPTION]);
        $db->exec('PRAGMA busy_timeout=250');
        // Aggregate telemetry can tolerate losing recent commits on power loss.
        // WAL NORMAL avoids a disk sync per hit while preserving DB integrity.
        $db->exec('PRAGMA synchronous=NORMAL');
        return $db;
    }
    public function install(): void {
        $dir=dirname($this->path);
        if (!is_dir($dir) && !mkdir($dir,0700,true) && !is_dir($dir)) throw new \RuntimeException('Cannot initialize insights storage.');
        $db=new \PDO('sqlite:'.$this->path,null,null,[\PDO::ATTR_ERRMODE=>\PDO::ERRMODE_EXCEPTION]);
        $db->exec('PRAGMA journal_mode=WAL');
        $db->exec('CREATE TABLE IF NOT EXISTS counts(day TEXT NOT NULL,page TEXT NOT NULL,kind TEXT NOT NULL,agent TEXT NOT NULL,method TEXT NOT NULL,status INTEGER NOT NULL,hits INTEGER NOT NULL,PRIMARY KEY(day,page,kind,agent,method,status)) WITHOUT ROWID');
        $db->exec('CREATE TABLE IF NOT EXISTS header_totals(day TEXT NOT NULL,dimension TEXT NOT NULL,value TEXT NOT NULL,hits INTEGER NOT NULL,PRIMARY KEY(day,dimension,value)) WITHOUT ROWID');
        if (PHP_OS_FAMILY!=='Windows') chmod($this->path,0600);
    }
    public static function classify(string $ua): array {
        $ua=substr($ua,0,4096);
        foreach ([
            ['GPTBot','ai-training','GPTBot'],['OAI-SearchBot','ai-search','OAI-SearchBot'],['ChatGPT-User','ai-user','ChatGPT-User'],
            ['ClaudeBot','ai-training','ClaudeBot'],['Claude-SearchBot','ai-search','Claude-SearchBot'],['Claude-User','ai-user','Claude-User'],
            ['Googlebot','search-bot','Googlebot'],['bingbot','search-bot','Bingbot'],['DuckDuckBot','search-bot','DuckDuckBot'],
        ] as [$token,$kind,$name]) if (stripos($ua,$token)!==false) return [$kind,$name];
        if (preg_match('/bot\b|crawler|spider|slurp|headless|python-requests|curl\/|wget\/|go-http-client/i',$ua)) return ['other-bot','Other automation'];
        if (preg_match('/Mozilla\/5\.0.*(?:Chrome\/|Firefox\/|Safari\/|Edg\/)/i',$ua)) return ['browser','Browser-like'];
        return ['unknown','Unknown'];
    }
    public static function deviceCategories(string $ua): array {
        $ua=substr($ua,0,4096);
        $browser='Other';$os='Unknown';$device='Unknown';
        foreach (['Edge'=>'/Edg(?:e|A|iOS)?\//i','Opera'=>'/(?:OPR|OPiOS)\//i','Firefox'=>'/(?:Firefox|FxiOS)\//i','Chrome'=>'/(?:Chrome|CriOS)\//i','Safari'=>'/Safari\//i'] as $label=>$pattern) {
            if (preg_match($pattern,$ua)) {$browser=$label;break;}
        }
        foreach (['Android'=>'/Android/i','iOS'=>'/iPhone|iPad|iPod/i','Windows'=>'/Windows/i','ChromeOS'=>'/CrOS/i','macOS'=>'/Macintosh|Mac OS X/i','Linux'=>'/Linux|X11/i'] as $label=>$pattern) {
            if (preg_match($pattern,$ua)) {$os=$label;break;}
        }
        if (preg_match('/iPad|Tablet/i',$ua) || ($os==='Android' && stripos($ua,'Mobile')===false)) $device='Tablet';
        elseif (preg_match('/Mobile|iPhone|iPod/i',$ua)) $device='Phone';
        elseif (in_array($os,['Windows','macOS','Linux','ChromeOS'],true)) $device='Desktop';
        return ['browser'=>$browser,'os'=>$os,'device'=>$device];
    }
    public static function page(string $uri, array $allowed=['/']): ?string {
        $path=parse_url($uri,PHP_URL_PATH);
        if (!is_string($path)) return '[other]';
        if (str_starts_with($path,'/assets/') || in_array($path,['/health','/favicon.ico','/_insights'],true)) return null;
        return in_array($path,$allowed,true) ? $path : '[other]';
    }
    public function record(string $uri,string $ua,string $method,int $status,?int $now=null): void {
        $page=self::page($uri,$this->pages);if ($page===null) return;
        $now??=time();$day=gmdate('Y-m-d',$now);$cutoff=gmdate('Y-m-d',$now-89*86400);
        [$kind,$agent]=self::classify($ua);
        $method=in_array($method,['GET','HEAD','POST'],true)?$method:'OTHER';
        $status=$status>=100 && $status<=599?$status:500;
        $db=$this->db();$db->exec('BEGIN IMMEDIATE');
        try {
            $db->prepare('DELETE FROM counts WHERE day < ?')->execute([$cutoff]);
            $db->prepare('DELETE FROM header_totals WHERE day < ?')->execute([$cutoff]);
            $db->prepare('INSERT INTO counts(day,page,kind,agent,method,status,hits) VALUES(?,?,?,?,?,?,1) ON CONFLICT(day,page,kind,agent,method,status) DO UPDATE SET hits=hits+1')->execute([$day,$page,$kind,$agent,$method,$status]);
            if ($this->deviceMetrics && $kind==='browser') {
                $insert=$db->prepare('INSERT INTO header_totals(day,dimension,value,hits) VALUES(?,?,?,1) ON CONFLICT(day,dimension,value) DO UPDATE SET hits=hits+1');
                foreach (self::deviceCategories($ua) as $dimension=>$value) $insert->execute([$day,$dimension,$value]);
            }
            $db->exec('COMMIT');
        } catch (\Throwable $e) {$db->exec('ROLLBACK');throw $e;}
    }
    public function report(int $days=30,?int $now=null): array {
        $days=max(1,min(90,$days));$now??=time();$today=gmdate('Y-m-d',$now);$start=gmdate('Y-m-d',$now-($days-1)*86400);
        $db=$this->db();$db->exec('PRAGMA query_only=ON');
        $q=$db->prepare('SELECT * FROM counts WHERE day BETWEEN ? AND ? ORDER BY day,page');$q->execute([$start,$today]);
        $r=['days'=>$days,'start'=>$start,'end'=>$today,'requests'=>0,'page_requests'=>0,'errors'=>0,'kinds'=>array_fill_keys(self::KINDS,0),'daily'=>[],'pages'=>[],'agents'=>[]];
        for ($i=$days-1;$i>=0;$i--) $r['daily'][gmdate('Y-m-d',$now-$i*86400)]=0;
        foreach ($q as $row) {
            $n=(int)$row['hits'];$r['requests']+=$n;$r['daily'][$row['day']]+=$n;$r['kinds'][$row['kind']]+=$n;
            $r['pages'][$row['page']]=($r['pages'][$row['page']]??0)+$n;
            $r['agents'][$row['agent']]=($r['agents'][$row['agent']]??0)+$n;
            if ($row['status']>=400) $r['errors']+=$n;
            if ($row['method']==='GET' && $row['status']>=200 && $row['status']<300 && !in_array($row['page'],['[other]','/robots.txt','/sitemap.xml'],true)) $r['page_requests']+=$n;
        }
        $r['headers']=['browser'=>[],'os'=>[],'device'=>[]];
        $q=$db->prepare('SELECT dimension,value,SUM(hits) AS hits FROM header_totals WHERE day BETWEEN ? AND ? GROUP BY dimension,value ORDER BY hits DESC,value');$q->execute([$start,$today]);
        foreach ($q as $row) $r['headers'][$row['dimension']][$row['value']]=(int)$row['hits'];
        arsort($r['pages']);arsort($r['agents']);return $r;
    }
}
