<?php
declare(strict_types=1);
namespace Webspine\Jobs;

final class SqliteQueue implements Queue, QueueMonitor {
    public function __construct(private string $root) {}
    private function path(): string { return \Webspine\Files::target($this->root, 'storage/job-queue/jobs.sqlite'); }
    private function db(): \PDO {
        if (!is_file($this->path())) throw new \RuntimeException('Install the job queue explicitly first.');
        $db = new \PDO('sqlite:'.$this->path(), null, null, [\PDO::ATTR_ERRMODE => \PDO::ERRMODE_EXCEPTION]);
        $db->exec('PRAGMA busy_timeout=5000');
        $db->exec('PRAGMA synchronous=FULL');
        if ((int)$db->query('PRAGMA user_version')->fetchColumn() !== 1) throw new \RuntimeException('Unsupported queue schema.');
        return $db;
    }
    public function install(): void {
        $directory = dirname($this->path());
        $previous = umask(0007);
        try {
            if (!is_dir($directory)) {
                if (!mkdir($directory, 02770, true) && !is_dir($directory)) throw new \RuntimeException('Cannot initialize private queue storage.');
                if (PHP_OS_FAMILY !== 'Windows') {
                    $group = filegroup($this->root . '/storage');
                    if ($group === false || !chgrp($directory, $group) || !chmod($directory, 02770)) throw new \RuntimeException('Cannot configure shared queue storage group.');
                }
            }
            $db = new \PDO('sqlite:'.$this->path(), null, null, [\PDO::ATTR_ERRMODE => \PDO::ERRMODE_EXCEPTION]);
            $db->exec('PRAGMA busy_timeout=5000');
            $db->exec('PRAGMA journal_mode=WAL');
            $db->exec('PRAGMA synchronous=FULL');
            $schema = (int)$db->query('PRAGMA user_version')->fetchColumn();
            if (!in_array($schema, [0,1], true)) throw new \RuntimeException('Unsupported queue schema.');
            $db->exec('BEGIN IMMEDIATE');
            try {
                $db->exec("CREATE TABLE IF NOT EXISTS jobs (
                    id TEXT PRIMARY KEY, type TEXT NOT NULL, payload_version INTEGER NOT NULL,
                    payload TEXT NOT NULL, dedupe_key TEXT, status TEXT NOT NULL DEFAULT 'pending'
                    CHECK(status IN ('pending','processing','completed','failed')),
                    attempts INTEGER NOT NULL DEFAULT 0, max_attempts INTEGER NOT NULL,
                    available_at INTEGER NOT NULL, created_at INTEGER NOT NULL, completed_at INTEGER,
                    claim_token TEXT, lease_until INTEGER, error_code TEXT,
                    UNIQUE(type,dedupe_key))");
                $db->exec('CREATE INDEX IF NOT EXISTS jobs_due ON jobs(status,available_at,lease_until)');
                $db->exec('PRAGMA user_version=1'); $db->exec('COMMIT');
            } catch (\Throwable $e) { $db->exec('ROLLBACK'); throw $e; }
            if (PHP_OS_FAMILY !== 'Windows' && (!chgrp($this->path(), filegroup($directory)) || !chmod($this->path(), 0660))) throw new \RuntimeException('Cannot configure queue database permissions.');
        } finally { umask($previous); }
    }
    private static function jsonValue(mixed $value, int $depth = 0): void {
        if ($depth > 15) throw new \InvalidArgumentException('Job payload is too deep.');
        if (is_array($value)) { foreach ($value as $item) self::jsonValue($item, $depth + 1); return; }
        if (!is_null($value) && !is_scalar($value)) throw new \InvalidArgumentException('Job payload must contain JSON values only.');
    }
    private static function canonical(array $value): array {
        if (!array_is_list($value)) ksort($value);
        foreach ($value as $key=>$item) if (is_array($item)) $value[$key]=self::canonical($item);
        return $value;
    }
    public function enqueue(string $type, int $version, array $payload, ?string $dedupe = null, int $delay = 0, int $maxAttempts = 5): string {
        if (!preg_match('/^[a-z][a-z0-9.-]{0,79}$/D', $type) || $version < 1 || $version > 1000000 || $delay < 0 || $delay > 31536000 || $maxAttempts < 1 || $maxAttempts > 100) throw new \InvalidArgumentException('Invalid job envelope.');
        foreach (array_keys($payload) as $key) if (!is_string($key)) throw new \InvalidArgumentException('Payload root must be an object.');
        if ($dedupe !== null && ($dedupe === '' || strlen($dedupe) > 200 || !preg_match('//u', $dedupe))) throw new \InvalidArgumentException('Invalid deduplication key.');
        self::jsonValue($payload);
        $json = json_encode((object)self::canonical($payload), JSON_THROW_ON_ERROR | JSON_UNESCAPED_UNICODE);
        if (strlen($json) > 65536) throw new \InvalidArgumentException('Job payload exceeds 64 KiB.');
        $id = bin2hex(random_bytes(16)); $now = time(); $db = $this->db();
        $stmt = $db->prepare('INSERT INTO jobs(id,type,payload_version,payload,dedupe_key,max_attempts,available_at,created_at) VALUES(?,?,?,?,?,?,?,?) ON CONFLICT(type,dedupe_key) DO NOTHING');
        $stmt->execute([$id,$type,$version,$json,$dedupe,$maxAttempts,$now+$delay,$now]);
        if ($stmt->rowCount() === 1) return $id;
        $find = $db->prepare('SELECT id,payload_version,payload FROM jobs WHERE type=? AND dedupe_key=?'); $find->execute([$type,$dedupe]); $existing = $find->fetch(\PDO::FETCH_ASSOC);
        if (!$existing || (int)$existing['payload_version'] !== $version || $existing['payload'] !== $json) throw new \LogicException('Deduplication key reused for different job data.');
        return $existing['id'];
    }
    public function claim(array $types, int $leaseSeconds = 300, ?int $now = null): ?Job {
        if (!$types) return null;
        if (count($types) > 100 || $leaseSeconds < 1 || $leaseSeconds > 86400) throw new \InvalidArgumentException('Invalid claim options.');
        foreach ($types as $type) if (!is_string($type) || !preg_match('/^[a-z][a-z0-9.-]{0,79}$/D', $type)) throw new \InvalidArgumentException('Invalid job type.');
        $now ??= time(); $db = $this->db(); $marks = implode(',', array_fill(0, count($types), '?'));
        $db->exec('BEGIN IMMEDIATE'); $active = true;
        try {
            $expired = $db->prepare("UPDATE jobs SET status='failed',completed_at=?,error_code='lease_exhausted',claim_token=NULL,lease_until=NULL WHERE status='processing' AND lease_until<=? AND attempts>=max_attempts AND type IN ($marks)");
            $expired->execute([$now,$now,...$types]);
            $stmt = $db->prepare("SELECT * FROM jobs WHERE type IN ($marks) AND attempts<max_attempts AND ((status='pending' AND available_at<=?) OR (status='processing' AND lease_until<=?)) ORDER BY available_at,created_at,id LIMIT 1");
            $stmt->execute([...$types,$now,$now]); $row = $stmt->fetch(\PDO::FETCH_ASSOC);
            if (!$row) { $db->exec('COMMIT'); $active = false; return null; }
            $token = bin2hex(random_bytes(24));
            $db->prepare("UPDATE jobs SET status='processing',attempts=attempts+1,claim_token=?,lease_until=? WHERE id=?")->execute([$token,$now+$leaseSeconds,$row['id']]);
            $db->exec('COMMIT'); $active = false;
            return new Job($row['id'],$row['type'],(int)$row['payload_version'],json_decode($row['payload'],true,32,JSON_THROW_ON_ERROR),(int)$row['attempts']+1,(int)$row['max_attempts'],$token);
        } catch (\Throwable $e) { if ($active) $db->exec('ROLLBACK'); throw $e; }
    }
    public function complete(Job $job, ?int $now = null): bool {
        $now ??= time(); $db = $this->db();
        $stmt = $db->prepare("UPDATE jobs SET status='completed',completed_at=?,claim_token=NULL,lease_until=NULL,error_code=NULL WHERE id=? AND status='processing' AND claim_token=? AND lease_until>?");
        $stmt->execute([$now,$job->id,$job->claimToken,$now]); return $stmt->rowCount() === 1;
    }
    public function fail(Job $job, string $code, bool $permanent = false, ?int $now = null): bool {
        if (!preg_match('/^[a-z][a-z0-9_]{0,63}$/D', $code)) throw new \InvalidArgumentException('Use a safe failure category.');
        $now ??= time(); $terminal = $permanent || $job->attempt >= $job->maxAttempts;
        $stmt = $this->db()->prepare("UPDATE jobs SET status=?,available_at=?,completed_at=?,error_code=?,claim_token=NULL,lease_until=NULL WHERE id=? AND status='processing' AND claim_token=? AND lease_until>?");
        $stmt->execute([$terminal?'failed':'pending',$now+min(3600,60*(2**min(10,$job->attempt-1))),$terminal?$now:null,$code,$job->id,$job->claimToken,$now]); return $stmt->rowCount() === 1;
    }
    public function renew(Job $job, int $seconds = 300, ?int $now = null): bool {
        if ($seconds < 1 || $seconds > 86400) throw new \InvalidArgumentException('Invalid lease duration.');
        $now ??= time(); $stmt=$this->db()->prepare("UPDATE jobs SET lease_until=MAX(lease_until,?) WHERE id=? AND status='processing' AND claim_token=? AND lease_until>?");
        $stmt->execute([$now+$seconds,$job->id,$job->claimToken,$now]); return $stmt->rowCount() === 1;
    }
    public function retry(string $id, ?int $now = null): bool {
        $stmt=$this->db()->prepare("UPDATE jobs SET status='pending',attempts=0,available_at=?,completed_at=NULL,error_code=NULL,claim_token=NULL,lease_until=NULL WHERE id=? AND status='failed'");
        $stmt->execute([$now??time(),$id]); return $stmt->rowCount() === 1;
    }
    public function status(): array {
        $counts=array_fill_keys(['pending','processing','completed','failed'],0);
        foreach ($this->db()->query('SELECT status,COUNT(*) AS n FROM jobs GROUP BY status') as $row) $counts[$row['status']] = (int)$row['n'];
        return $counts;
    }
    public function diagnostics(?int $now = null): array {
        $now ??= time();$db=$this->db();
        $stmt=$db->prepare("SELECT
            SUM(status='pending') AS pending, SUM(status='processing') AS processing,
            SUM(status='completed') AS completed, SUM(status='failed') AS failed,
            MIN(CASE WHEN status='pending' THEN created_at END) AS oldest_pending,
            MIN(CASE WHEN status='pending' AND available_at<=? THEN available_at END) AS oldest_due,
            SUM(status='processing' AND lease_until<=?) AS expired_leases FROM jobs");
        $stmt->execute([$now,$now]);$row=$stmt->fetch(\PDO::FETCH_ASSOC);
        $result=[];
        foreach (['pending','processing','completed','failed','expired_leases'] as $key) $result[$key]=(int)$row[$key];
        $result['oldest_pending_age_seconds']=$row['oldest_pending']===null?null:max(0,$now-(int)$row['oldest_pending']);
        $result['oldest_due_age_seconds']=$row['oldest_due']===null?null:max(0,$now-(int)$row['oldest_due']);
        $heartbeat=$this->heartbeat();
        $result['last_worker_started_at']=$heartbeat['last_started_at'];
        $result['last_worker_finished_at']=$heartbeat['last_finished_at'];
        $result['last_worker_age_seconds']=$heartbeat['last_started_at']===null?null:max(0,$now-$heartbeat['last_started_at']);
        $result['latest_worker_run_finished']=$heartbeat['last_started_at']===null?null:$heartbeat['latest_finished'];
        return $result;
    }
    public function workerStarted(?int $now = null): string {
        $now ??= time();if($now<0)throw new \InvalidArgumentException('Invalid worker timestamp.');
        $this->db();$run=bin2hex(random_bytes(16));
        $this->heartbeat(static function(array $state) use($run,$now): array {
            if ($state['last_started_at']===null || $now >= $state['last_started_at']) {
                $state['last_started_at']=$now;$state['latest_run']=$run;$state['latest_finished']=false;
            }
            return $state;
        });
        return $run;
    }
    public function workerFinished(string $run, ?int $now = null): void {
        if (!preg_match('/^[a-f0-9]{32}$/D',$run)) throw new \InvalidArgumentException('Invalid worker run identity.');
        $now ??= time();
        if($now<0)throw new \InvalidArgumentException('Invalid worker timestamp.');
        $this->heartbeat(static function(array $state) use($run,$now): array {
            $state['last_finished_at']=max($state['last_finished_at']??0,$now);
            if ($state['latest_run']===$run) $state['latest_finished']=true;
            return $state;
        });
    }
    private function heartbeat(?callable $update = null): array {
        $path=\Webspine\Files::target($this->root,'storage/job-queue/worker.json');
        $empty=['last_started_at'=>null,'last_finished_at'=>null,'latest_run'=>null,'latest_finished'=>false];
        if ($update===null && !is_file($path)) return $empty;
        $mask=umask(0007);
        try {$file=fopen($path,$update===null?'rb':'c+');} finally {umask($mask);}
        if (!$file) throw new \RuntimeException('Cannot open private queue heartbeat.');
        try {
            if (!flock($file,$update===null?LOCK_SH:LOCK_EX)) throw new \RuntimeException('Cannot lock queue heartbeat.');
            $raw=stream_get_contents($file,1025);
            if ($raw===false || strlen($raw)>1024) throw new \RuntimeException('Invalid queue heartbeat.');
            $state=$raw===''?$empty:json_decode($raw,true,4,JSON_THROW_ON_ERROR);
            if (!is_array($state) || array_keys($state)!==array_keys($empty)
                || !(is_null($state['last_started_at']) || is_int($state['last_started_at']) && $state['last_started_at']>=0)
                || !(is_null($state['last_finished_at']) || is_int($state['last_finished_at']) && $state['last_finished_at']>=0)
                || !(is_null($state['latest_run']) || is_string($state['latest_run']) && preg_match('/^[a-f0-9]{32}$/D',$state['latest_run']))
                || !is_bool($state['latest_finished'])
                || (($state['last_started_at']===null)!==($state['latest_run']===null))
                || ($state['latest_finished'] && $state['latest_run']===null)) throw new \RuntimeException('Invalid queue heartbeat.');
            if ($update!==null) {
                $state=$update($state);$json=json_encode($state,JSON_THROW_ON_ERROR);rewind($file);
                if (!ftruncate($file,0) || fwrite($file,$json)!==strlen($json) || !fflush($file)) throw new \RuntimeException('Cannot save queue heartbeat.');
            }
            return $state;
        } finally {flock($file,LOCK_UN);fclose($file);}
    }
    public function prune(int $days = 30): int {
        if ($days < 1 || $days > 3650) throw new \InvalidArgumentException('Retention must be 1–3650 days.');
        $stmt = $this->db()->prepare("DELETE FROM jobs WHERE id IN (SELECT id FROM jobs WHERE status IN ('completed','failed') AND completed_at<=? ORDER BY completed_at,id LIMIT 1000)");
        $stmt->execute([time() - $days * 86400]);
        return $stmt->rowCount();
    }
}
