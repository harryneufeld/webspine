<?php
declare(strict_types=1);

// Shared HTTP assertions for cli-server, Apache and real PHP-FPM fixture servers.
return static function (callable $request, callable $check, bool $fastcgi): void {
    $observe = static function (string $id, int $count) use ($request): array {
        for ($i = 0; $i < 40; $i++) {
            $rows = json_decode($request('/lifecycle-events?id=' . $id, 'GET')['body'], true, flags: JSON_THROW_ON_ERROR);
            if (count($rows) >= $count) return $rows;
            usleep(25000);
        }
        return $rows;
    };
    foreach (['GET', 'HEAD'] as $method) {
        $check($method . ' fires response.sent once after emission with immutable metadata', static function () use ($request, $observe, $method, $fastcgi): bool {
            $id = bin2hex(random_bytes(6)); $r = $request('/lifecycle-normal?id=' . $id, $method); $rows = $observe($id, 2);
            return $r['status'] === 201 && $r['body'] === ($method === 'HEAD' ? '' : 'Lifecycle body')
                && ($r['headers']['x-lifecycle'] ?? '') === 'original' && count($rows) === 2
                && array_column($rows, 'stage') === ['first','second'] && $rows[0]['method'] === $method
                && $rows[0]['status'] === 201 && $rows[0]['head'] === ($method === 'HEAD') && $rows[0]['fastcgi'] === $fastcgi
                && $rows[0]['headers']['X-Content-Type-Options'] === 'nosniff' && $rows[0]['update_locked'];
        });
    }
    foreach (['output','throw','warning','header','reentrant','readonly'] as $mode) {
        $check($mode . ' listener cannot corrupt the response or prevent later listeners', static function () use ($request, $observe, $mode): bool {
            $id = bin2hex(random_bytes(6)); $r = $request('/lifecycle-' . $mode . '?id=' . $id, 'GET'); $rows = $observe($id, 2);
            return $r['status'] === 201 && $r['body'] === 'Lifecycle body' && !isset($r['headers']['x-hook-leak'])
                && ($r['headers']['x-lifecycle'] ?? '') === 'original' && count($rows) === 2 && $rows[1]['status'] === 201;
        });
    }
    $check('Sessions close before hooks while the framework update lock remains held', static function () use ($request, $observe): bool {
        $id = bin2hex(random_bytes(6)); $r = $request('/lifecycle-session?id=' . $id, 'GET'); $rows = $observe($id, 2);
        return $r['status'] === 201 && count($rows) === 2 && !$rows[0]['session_active'] && $rows[0]['update_locked'];
    });
    foreach (['client-error'=>400, 'failure'=>503, 'missing'=>404] as $mode => $status) {
        $check($mode . ' response invokes hooks once for GET and HEAD without private diagnostics', static function () use ($request, $observe, $mode, $status): bool {
            foreach (['GET','HEAD'] as $method) {
                $id = bin2hex(random_bytes(6)); $r = $request('/lifecycle-' . $mode . '?id=' . $id, $method); $rows = $observe($id, 2);
                if ($r['status'] !== $status || ($method === 'HEAD' && $r['body'] !== '') || str_contains($r['body'], 'PRIVATE_')
                    || count($rows) !== 2 || $rows[0]['status'] !== $status || $rows[0]['head'] !== ($method === 'HEAD')) return false;
            }
            return true;
        });
    }
    $check('The soft hook deadline skips subsequent callbacks without changing the response', static function () use ($request, $observe): bool {
        $id = bin2hex(random_bytes(6)); $r = $request('/lifecycle-budget?id=' . $id, 'GET');
        $observe($id, 1); usleep(200000); $rows = $observe($id, 1);
        return $r['status'] === 201 && $r['body'] === 'Lifecycle body' && count($rows) === 1 && $rows[0]['stage'] === 'first';
    });
    foreach (['exit'=>'Early exit','direct'=>'Direct body'] as $mode => $body) {
        $check($mode . ' bypass does not claim a managed response was emitted', static function () use ($request, $mode, $body): bool {
            $id = bin2hex(random_bytes(6)); $r = $request('/lifecycle-' . $mode . '?id=' . $id, 'GET');
            $rows = json_decode($request('/lifecycle-events?id=' . $id, 'GET')['body'], true, flags: JSON_THROW_ON_ERROR);
            return $r['status'] === 200 && $r['body'] === $body && $rows === [];
        });
    }
};
