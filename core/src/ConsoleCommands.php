<?php
declare(strict_types=1);
namespace Webspine;

/** Explicit command discovery and bounded positional arguments for trusted plugins. */
final class ConsoleCommands {
    private array $commands = [];
    private const CORE = [
        'install'=>['', 'Explicit, idempotent installation'],
        'theme'=>['<id>', 'Switch active theme'],
        'entities:install'=>['', 'Explicitly install declared entity storage'],
        'health'=>['', 'Check services and rendering'],
        'package'=>['[--full]', 'Create core release or bootstrap ZIP'],
        'update'=>['<archive>', 'Apply a trusted local core release'],
        'rollback'=>['', 'Restore the previous core'],
        'recover'=>['', 'Restore after interrupted activation'],
        'help'=>['[command|--core]', 'List commands or show command help'],
    ];
    public function register(string $name, string $description, callable $handler, int $minArgs = 0, int $maxArgs = 0, string $arguments = ''): void {
        if (isset(self::CORE[$name])) throw new \InvalidArgumentException('Reserved core command: ' . $name);
        if (!preg_match('/^[a-z][a-z0-9-]{0,39}:[a-z][a-z0-9-]{0,39}$/D', $name)) throw new \InvalidArgumentException('Use a plugin:command name.');
        if (isset($this->commands[$name])) throw new \InvalidArgumentException('Duplicate command: ' . $name);
        if ($description === '' || strlen($description) > 160 || strlen($arguments) > 160
            || !preg_match('//u', $description) || !preg_match('//u', $arguments) || preg_match('/[\x00-\x1f\x7f]/', $description . $arguments)
            || $minArgs < 0 || $maxArgs < $minArgs || $maxArgs > 32) throw new \InvalidArgumentException('Invalid command definition: ' . $name);
        $this->commands[$name] = ['description'=>$description,'handler'=>$handler,'min'=>$minArgs,'max'=>$maxArgs,'arguments'=>$arguments];
    }
    public function run(string $name, array $arguments): int {
        $command = $this->commands[$name] ?? throw new \InvalidArgumentException('Unknown command: ' . $name);
        if (!array_is_list($arguments) || count($arguments) < $command['min'] || count($arguments) > $command['max']
            || count(array_filter($arguments, 'is_string')) !== count($arguments)) throw new \InvalidArgumentException('Usage: ' . $this->usage($name, $command['arguments']));
        $status = ($command['handler'])($arguments);
        if (!is_int($status) || $status < 0 || $status > 125) throw new \RuntimeException('Command must return an integer exit status 0–125: ' . $name);
        return $status;
    }
    public function help(?string $name = null): string {
        if ($name !== null) {
            if (isset(self::CORE[$name])) [$arguments, $description] = self::CORE[$name];
            else { $command = $this->commands[$name] ?? throw new \InvalidArgumentException('Unknown command: ' . $name);$arguments = $command['arguments'];$description = $command['description']; }
            return 'Usage: php core/bin/console.php ' . $this->usage($name, $arguments) . "\n" . $description . "\n";
        }
        $text = "webspine CLI\n\n";
        foreach (self::CORE as $id => [$arguments, $description]) $text .= $this->line($id, $arguments, $description);
        if ($this->commands) {
            $text .= "\nEnabled plugin commands\n";
            $commands = $this->commands; ksort($commands);
            foreach ($commands as $id => $command) $text .= $this->line($id, $command['arguments'], $command['description']);
        }
        return $text;
    }
    private function usage(string $name, string $arguments): string { return $name . ($arguments === '' ? '' : ' ' . $arguments); }
    private function line(string $name, string $arguments, string $description): string { return str_pad($this->usage($name, $arguments), 32) . ' ' . $description . "\n"; }
}
