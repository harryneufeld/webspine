<?php
declare(strict_types=1);
namespace Webspine\Jobs;
final class Worker {
    public function __construct(private Queue $queue, private Handlers $handlers) {}
    public function run(int $limit = 10, int $leaseSeconds = 300): array {
        if ($limit < 1 || $limit > 1000) throw new \InvalidArgumentException('Invalid worker batch size.');
        if ($leaseSeconds < 1 || $leaseSeconds > 86400) throw new \InvalidArgumentException('Invalid lease duration.');
        $run=$this->queue instanceof QueueMonitor?$this->queue->workerStarted():null;
        $result = ['completed'=>0,'retried'=>0,'failed'=>0,'lost_claims'=>0];
        for ($i=0;$i<$limit;$i++) {
            $job = $this->queue->claim($this->handlers->available(),$leaseSeconds);
            if (!$job) break;
            try {
                $this->handlers->get($job->type)->handle($job);
                if ($this->queue->complete($job)) $result['completed']++; else $result['lost_claims']++;
            } catch (PermanentFailure $e) {
                $code=preg_match('/^[a-z][a-z0-9_]{0,63}$/D',$e->reason)?$e->reason:'invalid_payload';
                if ($this->queue->fail($job,$code,true)) $result['failed']++; else $result['lost_claims']++;
            } catch (\Throwable $e) {
                // Useful private context without message text, recipients or submitted data.
                error_log('Job ' . $job->id . ' handler failed: ' . $e::class . ' at ' . $e->getFile() . ':' . $e->getLine());
                if ($this->queue->fail($job,'handler_failed')) $result[$job->attempt >= $job->maxAttempts?'failed':'retried']++; else $result['lost_claims']++;
            }
        }
        if ($run!==null) $this->queue->workerFinished($run);
        return $result;
    }
}
