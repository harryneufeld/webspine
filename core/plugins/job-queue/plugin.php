<?php
declare(strict_types=1);
require_once __DIR__.'/bootstrap.php';
return new class implements \Webspine\Contracts\Plugin {
    public function register(\Webspine\App $app): void {
        $app->services->set(\Webspine\Jobs\Queue::class,new \Webspine\Jobs\SqliteQueue($app->root));
        $app->services->set(\Webspine\Jobs\Handlers::class,new \Webspine\Jobs\HandlerRegistry());
        $app->hooks->on('cli.register',static fn(\Webspine\ConsoleCommands $commands) => \Webspine\Jobs\QueueCommands::register($commands,$app));
    }
};
