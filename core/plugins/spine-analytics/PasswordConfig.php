<?php
declare(strict_types=1);
namespace Webspine\Providers\SpineAnalytics;

/** Edit only literal analytics credentials; preserve the rest of executable config. */
final class PasswordConfig {
    public static function hashFile(string $path): bool {
        if (is_link($path) || !is_file($path)) throw new \RuntimeException('Create private config/local.php first.');
        $file = fopen($path, 'r');
        if (!$file || !flock($file, LOCK_EX)) throw new \RuntimeException('Cannot lock private configuration.');
        $temporary = null;
        try {
            $source = stream_get_contents($file);
            if ($source === false) throw new \RuntimeException('Cannot read private configuration.');
            $updated = self::convert($source);
            if ($updated === null) return false;
            $temporary = $path . '.tmp-' . bin2hex(random_bytes(8));
            $mask = umask(0077);
            try {
                if (file_put_contents($temporary, $updated, LOCK_EX) !== strlen($updated)) throw new \RuntimeException('Cannot write hashed configuration.');
                if (PHP_OS_FAMILY !== 'Windows' && !chmod($temporary, fileperms($path) & 0600)) throw new \RuntimeException('Cannot protect hashed configuration.');
            } finally { umask($mask); }
            // Windows cannot replace an open destination. Detect intervening edits.
            flock($file, LOCK_UN); fclose($file); $file = null;
            if (file_get_contents($path) !== $source) throw new \RuntimeException('Configuration changed; retry after finishing edits.');
            if (!rename($temporary, $path)) throw new \RuntimeException('Cannot replace private configuration.');
            $temporary = null;
            return true;
        } finally {
            if (is_resource($file)) { flock($file, LOCK_UN); fclose($file); }
            if ($temporary !== null && is_file($temporary)) unlink($temporary);
        }
    }

    private static function convert(#[\SensitiveParameter] string $source): ?string {
        try { $raw = token_get_all($source, TOKEN_PARSE); }
        catch (\ParseError) { throw new \RuntimeException('Private configuration contains invalid PHP.'); }
        $tokens = []; $offset = 0;
        foreach ($raw as $token) {
            $text = is_array($token) ? $token[1] : $token;
            if (!is_array($token) || !in_array($token[0], [T_WHITESPACE, T_COMMENT, T_DOC_COMMENT], true)) {
                $tokens[] = ['text'=>$text, 'id'=>is_array($token) ? $token[0] : null, 'start'=>$offset, 'end'=>$offset + strlen($text)];
            }
            $offset += strlen($text);
        }
        $sections = [];
        foreach ($tokens as $i => $token) {
            if ($token['id'] === T_CONSTANT_ENCAPSED_STRING && in_array($token['text'], ["'spine_analytics'", '"spine_analytics"'], true) && ($tokens[$i+1]['id'] ?? null) === T_DOUBLE_ARROW) {
                $open = $i + 2;
                if (($tokens[$open]['id'] ?? null) === T_ARRAY) $open++;
                if (!in_array($tokens[$open]['text'] ?? '', ['[', '('], true)) throw new \RuntimeException('Use a literal spine_analytics array in private configuration.');
                $sections[] = $open;
            }
        }
        if (count($sections) !== 1) throw new \RuntimeException('Expected exactly one literal spine_analytics configuration section.');
        $open = $sections[0]; $depth = 0; $entries = []; $entry = [];
        for ($i = $open + 1; $i < count($tokens); $i++) {
            $text = $tokens[$i]['text'];
            if ($depth === 0 && in_array($text, [',', ']', ')'], true)) {
                if ($entry) $entries[] = $entry;
                $entry = [];
                if ($text !== ',') break;
                continue;
            }
            $entry[] = $tokens[$i];
            if (in_array($text, ['[', '(', '{'], true)) $depth++;
            elseif (in_array($text, [']', ')', '}'], true)) $depth--;
        }
        $fields = [];
        foreach ($entries as $entry) {
            // Dynamic/spread entries could override the credentials after conversion.
            if (($entry[0]['id'] ?? null) !== T_CONSTANT_ENCAPSED_STRING || ($entry[1]['id'] ?? null) !== T_DOUBLE_ARROW) throw new \RuntimeException('Use explicit literal keys in spine_analytics configuration.');
            $key = self::literal($entry[0]);
            if (isset($fields[$key])) throw new \RuntimeException('Duplicate analytics configuration key.');
            $fields[$key] = array_slice($entry, 2);
        }
        $passwordTokens = $fields['password'] ?? [];
        if (!$passwordTokens || (count($passwordTokens) === 1 && strtolower($passwordTokens[0]['text']) === 'null')) {
            $hashTokens = $fields['password_hash'] ?? [];
            if (count($hashTokens) === 1 && $hashTokens[0]['id'] === T_CONSTANT_ENCAPSED_STRING && password_get_info(self::literal($hashTokens[0]))['algoName'] !== 'unknown') return null;
            throw new \RuntimeException('Set spine_analytics.password to a single-quoted password first.');
        }
        if (count($passwordTokens) !== 1) throw new \RuntimeException('Use a single-quoted literal for spine_analytics.password.');
        $hash = ReportAccess::hashPassword(self::literal($passwordTokens[0]));
        $edits = [[$passwordTokens[0]['start'], $passwordTokens[0]['end'], 'null']];
        if (isset($fields['password_hash'])) {
            $value = $fields['password_hash'];
            if (count($value) !== 1 || ($value[0]['id'] !== T_CONSTANT_ENCAPSED_STRING && strtolower($value[0]['text']) !== 'null')) throw new \RuntimeException('Use a literal or null for spine_analytics.password_hash.');
            $edits[] = [$value[0]['start'], $value[0]['end'], var_export($hash, true)];
        } else {
            $edits[] = [$tokens[$open]['end'], $tokens[$open]['end'], "\n        'password_hash' => " . var_export($hash, true) . ','];
        }
        usort($edits, static fn(array $a, array $b): int => $b[0] <=> $a[0]);
        foreach ($edits as [$start, $end, $replacement]) $source = substr_replace($source, $replacement, $start, $end - $start);
        return $source;
    }

    private static function literal(#[\SensitiveParameter] array $token): string {
        $text = $token['text'];
        if ($token['id'] !== T_CONSTANT_ENCAPSED_STRING || !str_starts_with($text, "'")) throw new \RuntimeException('Use single-quoted literal analytics keys and passwords.');
        return preg_replace_callback('/\\\\([\\\\\'])/', static fn(array $match): string => $match[1], substr($text, 1, -1));
    }
}
