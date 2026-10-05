<?php
declare(strict_types=1);
use Webspine\{App, Request, Response, Router, HttpError};
return static function (App $app): void {
    check('PHP-FPM empty content length is absent; invalid/oversized lengths fail', static function (): bool {
        $saved = $_SERVER;
        try {
            $_SERVER = ['REQUEST_METHOD'=>'GET','REQUEST_URI'=>'/','CONTENT_LENGTH'=>''];
            $request = Request::fromGlobals();
            if ($request->body !== '' || $request->header('Content-Length') !== null) return false;
            foreach (['invalid'=>400,'65537'=>413] as $length=>$status) {
                $_SERVER['CONTENT_LENGTH'] = (string) $length;
                try { Request::fromGlobals(); return false; } catch (HttpError $e) { if ($e->status !== $status) return false; }
            }
            return true;
        } finally { $_SERVER = $saved; }
    });
    check('Request preserves URI, decodes path/query, and normalizes headers', static function (): bool {
        $r = new Request('get', '/search%2Dpage?q=a%20b&tag[]=one&tag[]=two&q=last', ['X-Example'=>'value']);
        return $r->method === 'GET' && $r->path === '/search-page' && $r->uri === '/search%2Dpage?q=a%20b&tag[]=one&tag[]=two&q=last' && $r->query('q') === 'last' && $r->query('tag') === ['one','two'] && $r->query('missing','fallback') === 'fallback' && $r->header('x-example') === 'value';
    });
    check('Form and JSON parsing are explicit and bounded', static function (): bool {
        $form = new Request('POST','/', ['Content-Type'=>'application/x-www-form-urlencoded; charset=UTF-8'], 'name=A%26B&items[]=1&items[]=2');
        $json = new Request('POST','/', ['Content-Type'=>'application/json'], '{"name":"A&B","count":2}');
        return $form->form() === ['name'=>'A&B','items'=>['1','2']] && $json->json() === ['name'=>'A&B','count'=>2];
    });
    check('Malformed query/body/header inputs fail without silent truncation', static function (): bool {
        foreach ([fn()=>new Request('GET','/?a=%xy'), fn()=>new Request('GET','/?'.str_repeat('x=1&',101)), fn()=>new Request('GET','/',["Bad\nHeader"=>'x']), fn()=>new Request('GET','/',['X-Test'=>"x\r\ny"]), fn()=>new Request('GET','//example.com/'), fn()=>new Request('GET','/%00')] as $bad) if (!expectError($bad)) return false;
        return expectError(fn()=>(new Request('POST','/',['Content-Type'=>'application/x-www-form-urlencoded'],'a=%xx'))->form());
    });
    check('Unsupported media types and invalid JSON produce explicit client errors', static function (): bool {
        try { (new Request('POST','/',['Content-Type'=>'multipart/form-data'],'body'))->form(); return false; } catch (HttpError $e) { if ($e->status !== 415) return false; }
        foreach (['bad','[]','null','{"x":'] as $body) { try { (new Request('POST','/',['Content-Type'=>'application/json'],$body))->json(); return false; } catch (HttpError $e) { if ($e->status !== 400) return false; } }
        return true;
    });
    check('Request limits reject oversized bodies and queries', static function (): bool {
        try { new Request('POST','/',[],str_repeat('x',Request::MAX_BODY+1)); return false; } catch (HttpError $e) { if ($e->status !== 413) return false; }
        try { new Request('GET','/?x='.str_repeat('x',8193)); return false; } catch (HttpError $e) { return $e->status === 414; }
    });
    $router = new Router();
    $router->get('/both', static fn(Request $r)=>Response::json(['method'=>$r->method]));
    $router->post('/both', static fn(Request $r)=>Response::json($r->form()));
    $router->post('/post-only', static fn()=>new Response('created',201));
    $router->get('/legacy', static fn()=>new Response('legacy'));
    $router->getPattern('~^/old/([0-9]+)$~D', static fn(array $m)=>new Response($m[1]));
    $router->postPattern('~^/new/([0-9]+)$~D', static fn(array $m,Request $r)=>Response::json(['id'=>$m[1],'value'=>$r->json()['value']]));
    check('Explicit GET and POST handlers coexist and receive their request', static function () use ($router): bool {
        $r = new Request('POST','/both',['Content-Type'=>'application/x-www-form-urlencoded'],'name=value');
        return $router->dispatch('GET','/both')->body === '{"method":"GET"}' && $router->dispatch('HEAD','/both')->body === '{"method":"HEAD"}' && $router->dispatch('POST','/both',$r)->body === '{"name":"value"}';
    });
    check('Existing zero-argument and pattern handlers retain their signatures', fn()=>$router->dispatch('GET','/legacy')->body === 'legacy' && $router->dispatch('GET','/old/42')->body === '42');
    check('POST pattern handlers receive captures before request context', static function () use ($router): bool {
        $r = new Request('POST','/new/42',['Content-Type'=>'application/json'],'{"value":"ok"}');
        return $router->dispatch('POST',$r->path,$r)->body === '{"id":"42","value":"ok"}';
    });
    check('Wrong methods return accurate Allow including implicit HEAD', static function () use ($router): bool {
        $getOnly = $router->dispatch('POST','/legacy'); $postOnly = $router->dispatch('HEAD','/post-only'); $both = $router->dispatch('PUT','/both');
        return $getOnly->status === 405 && $getOnly->headers['Allow'] === 'GET, HEAD' && $postOnly->status === 405 && $postOnly->headers['Allow'] === 'POST' && $both->headers['Allow'] === 'GET, HEAD, POST' && $router->dispatch('POST','/missing') === null;
    });
    check('Duplicate method routes and invalid patterns fail explicitly', fn()=>expectError(fn()=>$router->post('/both',fn()=>new Response())) && expectError(fn()=>$router->postPattern('bad',fn()=>new Response())));
    $app->router->post('/request-test', static fn(Request $r)=>Response::json($r->form()));
    $app->router->get('/legacy-query', static function (Request $r): Response {
        $id = $r->query('page_id');
        return new Response('',302,['Location'=>is_string($id) && $id === '41' ? '/docs' : '/']);
    });
    check('App accepts opt-in POST and maps parser errors to HTTP responses', static function () use ($app): bool {
        $valid = new Request('POST','/request-test',['Content-Type'=>'application/x-www-form-urlencoded'],'name=ok');
        $unsupported = new Request('POST','/request-test',['Content-Type'=>'text/plain'],'body');
        return $app->handle($valid)->body === '{"name":"ok"}' && $app->handle($unsupported)->status === 415 && $app->handle('POST','/missing-post')->status === 404 && $app->handle('GET','/%00')->status === 400;
    });
    check('Legacy query redirects use explicit request input and fixed targets', fn()=>$app->handle(new Request('GET','/legacy-query?page_id=41'))->headers['Location'] === '/docs' && $app->handle(new Request('GET','/legacy-query?page_id[]=41'))->headers['Location'] === '/');
    check('Asset and public health endpoints remain read-only', fn()=>$app->handle('POST','/assets/theme/test-theme/app.js')->status === 405 && $app->handle('POST','/health')->headers['Allow'] === 'GET, HEAD');
};
