<?php
declare(strict_types=1);
namespace Webspine\Jobs;

/** Explicit operator workflow, never run from registration or a submission request. */
final class QueueWork {
    public static function run(Queue $queue, Handlers $handlers, array $config = []): array {
        $days=array_key_exists('retention_days',$config)?$config['retention_days']:30;
        if ($days!==null && (!is_int($days) || $days<1 || $days>3650)) throw new \InvalidArgumentException('Configure queue retention_days as 1–3650 or null.');
        $result=(new Worker($queue,$handlers))->run();
        $supported=$queue instanceof CompletedJobRetention;
        $result['retention']=['days'=>$days,'supported'=>$supported,'enabled'=>$days!==null && $supported,
            'pruned_completed'=>$days!==null && $supported?$queue->pruneCompleted($days):0];
        return $result;
    }
}
