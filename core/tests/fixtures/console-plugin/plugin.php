<?php
declare(strict_types=1);
return new class implements \Webspine\Contracts\Plugin {
    public function register(\Webspine\App $app):void {
        $app->hooks->on('cli.register',static function(\Webspine\ConsoleCommands $commands)use($app):void {
            file_put_contents($app->root.'/storage/command-discovery','registered');
            $commands->register('demo:echo','Echo one or two literal arguments',static function(array $args):int {
                echo json_encode($args,JSON_THROW_ON_ERROR)."\n";return 0;
            },1,2,'<value> [extra]');
            $commands->register('demo:fail','Return a deliberate failure',static function(array $args):int {fwrite(STDERR,"Fixture failure\n");return 7;});
            $commands->register('demo:throw','Throw a deliberate failure',static fn(array $args)=>throw new RuntimeException('Fixture exception'));
            $commands->register('demo:bad-status','Return an invalid status',static fn(array $args)=>'0');
            $commands->register('demo:lock','Verify shared framework lock',static function(array $args)use($app):int {
                $file=fopen($app->root.'/storage/update.lock','c+');$exclusive=flock($file,LOCK_EX|LOCK_NB);
                if($exclusive)flock($file,LOCK_UN);fclose($file);echo $exclusive?"unlocked\n":"locked\n";return $exclusive?1:0;
            });
            if(($app->config['console_fixture']??'')==='duplicate')$commands->register('demo:echo','Duplicate',static fn(array $args):int=>0);
            if(($app->config['console_fixture']??'')==='collision')$commands->register('recover','Collision',static fn(array $args):int=>0);
        });
    }
};
