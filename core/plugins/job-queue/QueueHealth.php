<?php
declare(strict_types=1);
namespace Webspine\Jobs;

final class QueueHealth {
    public static function check(Queue $queue, int $staleAfter = 900, ?int $now = null): array {
        if ($staleAfter < 60 || $staleAfter > 604800) throw new \InvalidArgumentException('Queue health threshold must be 60–604800 seconds.');
        if (!$queue instanceof QueueMonitor) return ['ok'=>false,'warnings'=>['monitor_unavailable'],'stale_after_seconds'=>$staleAfter,'queue'=>$queue->status()];
        $status=$queue->diagnostics($now);$warnings=[];
        if ($status['failed']>0) $warnings[]='failed_jobs';
        if ($status['expired_leases']>0) $warnings[]='expired_leases';
        if ($status['oldest_due_age_seconds']!==null && $status['oldest_due_age_seconds'] >= $staleAfter) {
            $warnings[]='overdue_jobs';
            if ($status['last_worker_started_at']===null) $warnings[]='worker_missing';
            elseif ($status['last_worker_age_seconds'] >= $staleAfter) $warnings[]='worker_stale';
        }
        if ($status['latest_worker_run_finished']===false && $status['last_worker_age_seconds'] >= $staleAfter) $warnings[]='worker_run_incomplete';
        return ['ok'=>!$warnings,'warnings'=>$warnings,'stale_after_seconds'=>$staleAfter,'queue'=>$status];
    }
}
