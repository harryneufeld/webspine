<?php
declare(strict_types=1);
namespace Webspine\Jobs;
use Webspine\{App,ConsoleCommands};

final class QueueCommands {
    public static function register(ConsoleCommands $commands, App $app): void {
        foreach ([
            'install'=>['Explicitly install queue storage',0,0,''],
            'status'=>['Inspect durable queue state',0,0,''],
            'health'=>['Check private queue health',0,0,''],
            'work'=>['Run a bounded worker and completed-job retention',0,0,''],
            'retry'=>['Requeue a failed job',1,1,'<job-id>'],
            'prune'=>['Prune completed and failed jobs',0,1,'[days]'],
        ] as $name => [$description, $min, $max, $arguments]) {
            $commands->register('job-queue:'.$name,$description,static fn(array $args):int => self::run($app,$name,$args),$min,$max,$arguments);
        }
    }
    private static function run(App $app, string $command, array $args): int {
        $queue=$app->services->get(Queue::class);
        $result=match($command) {
            'install'=>(static function()use($queue){$queue->install();return ['installed'=>true];})(),
            'status'=>$queue instanceof QueueMonitor ? $queue->diagnostics() : $queue->status(),
            'health'=>QueueHealth::check($queue,self::threshold($app)),
            'work'=>QueueWork::run($queue,$app->services->get(Handlers::class),self::options($app)),
            'retry'=>['requeued'=>$queue->retry($args[0])],
            'prune'=>['pruned'=>$queue->prune(isset($args[0]) ? (filter_var($args[0],FILTER_VALIDATE_INT)!==false ? (int)$args[0] : throw new \InvalidArgumentException('Usage: job-queue:prune [days]')) : 30)],
        };
        echo json_encode($result,JSON_PRETTY_PRINT|JSON_THROW_ON_ERROR)."\n";
        return $command==='health' && !$result['ok'] ? 1 : 0;
    }
    private static function options(App $app): array {
        $config=$app->config['job_queue']??[];
        if(!is_array($config))throw new \InvalidArgumentException('Configure a queue options array.');
        return $config;
    }
    private static function threshold(App $app): int {
        $seconds=self::options($app)['stale_after_seconds']??900;
        if(!is_int($seconds))throw new \InvalidArgumentException('Configure an integer queue health threshold.');
        return $seconds;
    }
}
