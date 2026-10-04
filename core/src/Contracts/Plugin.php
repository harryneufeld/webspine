<?php
declare(strict_types=1);
namespace Webspine\Contracts;
use Webspine\App;
interface Plugin { public function register(App $app): void; }
