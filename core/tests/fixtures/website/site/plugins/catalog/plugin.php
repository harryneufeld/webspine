<?php
declare(strict_types=1);
use Webspine\{App, Contracts\Plugin, Contracts\Entities};

// Optional example: enable "catalog" in private config, then run entities:install.
return new class implements Plugin {
    public function register(App $app): void {
        $app->services->get(Entities::class)->define('products', [
            'name' => ['type'=>'string', 'required'=>true, 'max_length'=>200],
            'price_cents' => ['type'=>'integer', 'required'=>true, 'min'=>0],
            'description' => ['type'=>'string', 'nullable'=>true],
            'active' => ['type'=>'boolean', 'default'=>true],
        ]);
    }
};
