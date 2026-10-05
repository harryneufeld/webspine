<?php
declare(strict_types=1);
// Read version metadata as text; never execute PHP from a Git revision.
function releaseVersion(string $source): string {
    $number = '(?:0|[1-9][0-9]*)';
    preg_match_all("/'version'\\s*=>\\s*'([^']*)'/", $source, $matches);
    if (count($matches[1]) !== 1 || !preg_match("/^$number\\.$number\\.$number$/D", $matches[1][0])) throw new RuntimeException('Expected exactly one stable major.minor.patch version.');
    return $matches[1][0];
}
function requireVersionBump(string $base, string $head): void {
    if (!version_compare($head, $base, '>')) throw new RuntimeException("Raise core/version.php above $base (found $head).");
}
function revisionVersion(string $ref): string {
    if (!preg_match('~^[A-Za-z0-9][A-Za-z0-9_./-]*$~D', $ref)) throw new RuntimeException('Invalid Git revision.');
    $process = proc_open(['git','show',$ref . ':core/version.php'], [0=>['pipe','r'],1=>['pipe','w'],2=>['pipe','w']], $pipes, dirname(__DIR__, 2));
    if (!is_resource($process)) throw new RuntimeException('Cannot read Git version metadata.');
    fclose($pipes[0]); $text = stream_get_contents($pipes[1]); $error = stream_get_contents($pipes[2]);
    fclose($pipes[1]); fclose($pipes[2]);
    if (proc_close($process) !== 0) throw new RuntimeException('Cannot read version from ' . $ref . ': ' . trim($error));
    return releaseVersion($text);
}
try {
    if (($argv[1] ?? '') === '--self-test') {
        $cases = [
            ['0.1.2','0.1.3',true], ['0.1.2','0.1.2',false], ['0.1.2','0.1.1',false],
            ['0.1.9','0.1.10',true], ['0.1.9','0.2.0',true], ['0.9.9','1.0.0',true],
        ];
        foreach ($cases as [$base,$head,$expected]) {
            try { requireVersionBump($base, $head); $accepted = true; } catch (RuntimeException) { $accepted = false; }
            if ($accepted !== $expected) throw new RuntimeException('Incorrect version ordering.');
        }
        foreach (["<?php return ['version'=>'01.2.3'];", "<?php return ['version'=>'1.2'];", "<?php return ['version'=>'1.2.3-rc1'];", "<?php return ['version'=>'1.2.3','version'=>'1.2.4'];", "<?php return ['version'=>'1.2.3','version'=>'bad'];"] as $invalid) {
            try { releaseVersion($invalid); throw new LogicException('Malformed version accepted.'); } catch (RuntimeException) {}
        }
        if (releaseVersion("<?php throw new Exception('must not execute'); return ['version'=>'0.1.3'];") !== '0.1.3') throw new RuntimeException('Text parsing failed.');
        echo "12 version checks passed.\n";
    } else {
        if (count($argv) !== 3) throw new RuntimeException('Usage: php core/tests/version-bump.php <base-ref> <head-ref>');
        $base = revisionVersion($argv[1]); $head = revisionVersion($argv[2]);
        requireVersionBump($base, $head);
        echo "Version bump verified: $base -> $head\n";
    }
} catch (Throwable $e) { fwrite(STDERR, $e->getMessage() . "\n"); exit(1); }
