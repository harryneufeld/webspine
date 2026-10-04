<?php $copy = $site->content('home'); ?>
<section class="hero shell">
    <div class="hero-copy">
        <h1><?= e($copy['headline_line_1']) ?><br><?= e($copy['headline_line_2']) ?><br><span class="possibility"><?= e($copy['headline_line_3']) ?><svg viewBox="0 0 490 24" preserveAspectRatio="none" aria-hidden="true"><path d="M3 17 Q180 -3 484 12 M15 22 Q230 4 455 19"/></svg></span><span class="period">.</span></h1>
        <p class="hero-description"><?= e($copy['hero_description']) ?><br> <?= e($copy['hero_ownership']) ?></p>
        <div class="hero-actions"><a class="button button-ink" href="<?= e($copy['href_docs_quickstart']) ?>"><?= e($copy['start_building']) ?> <span aria-hidden="true">↗</span></a><a class="text-link" href="<?= e($copy['href_architecture']) ?>"><?= e($copy['explore_the_framework']) ?> <span aria-hidden="true">↓</span></a></div>
        <div class="hero-footnote"><span class="tiny-code" aria-hidden="true"><?= e($copy['symbol']) ?></span> <?= e($copy['plain_code_no_build_step_entirely_yours']) ?></div>
    </div>
    <div class="hero-art" aria-label="<?= e($copy['aria_label_illustration_of_a_website_built_from_an']) ?>" role="img">
        <div class="art-grid"></div><div class="art-orbit orbit-one"></div><div class="art-orbit orbit-two"></div>
        <div class="art-coordinate coordinate-top"><?= e($copy['fig_01_the_anatomy_of_a_website']) ?></div>
        <div class="spine-line"></div>
        <div class="structural-layer layer-theme"><span class="layer-icon" aria-hidden="true">✳</span><span><?= e($copy['your_expression']) ?></span><strong><?= e($copy['themes']) ?><span>/</span></strong><span class="layer-index">01</span></div>
        <div class="structural-layer layer-plugins"><span class="layer-icon" aria-hidden="true">+</span><span><?= e($copy['your_possibilities']) ?></span><strong><?= e($copy['plugins']) ?><span>/</span></strong><span class="layer-index">02</span></div>
        <div class="structural-layer layer-core"><span class="layer-icon" aria-hidden="true">≋</span><span><?= e($copy['your_foundation']) ?></span><strong><?= e($copy['core']) ?><span>/</span></strong><span class="layer-index">03</span></div>
        <div class="art-tag tag-one"><span aria-hidden="true">↖</span> <?= e($copy['make_it_yours']) ?></div><div class="art-tag tag-two"><span class="status-dot" aria-hidden="true"></span> <?= e($copy['solid_underneath']) ?></div>
        <div class="art-coordinate coordinate-bottom"><span class="crosshair" aria-hidden="true">+</span> <?= e($copy['separate_by_design_connected_by_contract']) ?></div>
    </div>
</section>
<div class="stack-strip shell"><span class="strip-label"><?= e($copy['a_familiar_stack']) ?><br><?= e($copy['a_fresh_starting_point']) ?></span><div class="stack-list"><span><?= e($copy['html']) ?></span><span><?= e($copy['css']) ?></span><span><?= e($copy['javascript']) ?></span><span><?= e($copy['php']) ?></span><span><?= e($copy['sqlite']) ?></span></div><span class="strip-end"><?= e($copy['no_npm']) ?><br><?= e($copy['no_composer']) ?></span></div>
<section class="intro-section shell" id="framework">
    <div class="section-number">01 <span><?= e($copy['the_idea']) ?></span></div>
    <div class="intro-layout"><h2><?= e($copy['less_machinery']) ?><br><?= e($copy['more']) ?> <em><?= e($copy['making']) ?></em></h2><div class="intro-prose"><p class="lead"><?= e($copy['your_website_your_ai_your_way']) ?></p><p><?= e($site->meta['brand_prefix']) ?><strong><?= e($site->meta['brand_bold']) ?></strong> <?= e($copy['is_a_small_readable_foundation_designed_for']) ?></p><p><?= e($copy['use_your_own_ai_app_with_access']) ?></p><a class="text-link" href="<?= e($copy['href_docs_principles']) ?>"><?= e($copy['meet_the_principles']) ?> <span aria-hidden="true">↗</span></a></div></div>
    <div class="principle-rows"><article><span class="principle-symbol" aria-hidden="true">{ }</span><h3><?= e($copy['code_your_ai_can_follow']) ?></h3><p><?= e($copy['plain_php_and_browser_native_code_clear']) ?></p></article><article><span class="principle-symbol" aria-hidden="true">↗</span><h3><?= e($copy['your_site_stays_yours']) ?></h3><p><?= e($copy['themes_plugins_and_content_live_outside_the']) ?></p></article><article><span class="principle-symbol" aria-hidden="true">⊕</span><h3><?= e($copy['small_with_room_to_grow']) ?></h3><p><?= e($copy['explicit_service_contracts_add_a_provider_or']) ?></p></article></div>
</section>
<section class="architecture-section" id="architecture"><div class="shell architecture-inner">
    <div class="section-number">02 <span><?= e($copy['the_structure']) ?></span></div>
    <div class="architecture-heading"><h2><?= e($copy['everything_in']) ?><br><?= e($copy['its_own_place']) ?></h2><p><?= e($copy['a_stable_core_an_independent_surface']) ?><br><?= e($copy['a_clear_connection_between_the_two']) ?></p></div>
    <div class="architecture-workbench">
        <div class="architecture-tabs" role="tablist" aria-label="<?= e($copy['aria_label_framework_layers']) ?>" aria-orientation="vertical">
            <button type="button" id="tab-themes" role="tab" aria-selected="true" aria-controls="panel-themes" class="architecture-tab"><span>01</span><div><strong><?= e($copy['themes_2']) ?></strong><small><?= e($copy['the_look_the_feel_the_you']) ?></small></div><span aria-hidden="true">↗</span></button>
            <button type="button" id="tab-plugins" role="tab" aria-selected="false" aria-controls="panel-plugins" tabindex="-1" class="architecture-tab"><span>02</span><div><strong><?= e($copy['plugins_2']) ?></strong><small><?= e($copy['capabilities_on_your_terms']) ?></small></div><span aria-hidden="true">↗</span></button>
            <button type="button" id="tab-core" role="tab" aria-selected="false" aria-controls="panel-core" tabindex="-1" class="architecture-tab"><span>03</span><div><strong><?= e($copy['core_2']) ?></strong><small><?= e($copy['the_dependable_backbone']) ?></small></div><span aria-hidden="true">↗</span></button>
        </div>
        <div class="architecture-panels">
            <div id="panel-themes" role="tabpanel" aria-labelledby="tab-themes" tabindex="0" class="architecture-panel"><div class="code-topbar"><span><i></i><i></i><i></i></span><span><?= e($copy['themes_your_theme']) ?></span><span><?= e($copy['php']) ?></span></div><pre><code><span class="code-muted"><?= e($copy['your_design_in_familiar_files']) ?></span>
<?= e($copy['themes_your_theme_2'] . "\n") ?>
    <span class="code-green"><?= e($copy['theme_json']) ?></span>       <span class="code-muted"><?= e($copy['identity_api']) ?></span>
    <span class="code-green"><?= e($copy['layout_php']) ?></span>       <span class="code-muted"><?= e($copy['shared_structure']) ?></span>
    <?= e($copy['home_php_assets_style_css_app_js']) ?></code></pre><div class="panel-caption"><span aria-hidden="true">↳</span> <?= e($copy['design_without_asking_the_core_for_permission']) ?></div></div>
            <div id="panel-plugins" role="tabpanel" aria-labelledby="tab-plugins" tabindex="0" class="architecture-panel" hidden><div class="code-topbar"><span><i></i><i></i><i></i></span><span><?= e($copy['plugins_field_notes']) ?></span><span><?= e($copy['php']) ?></span></div><pre><code><span class="code-muted"><?= e($copy['small_extensions_explicit_connections']) ?></span>
<?= e($copy['plugins_sqlite']) ?>            <span class="code-muted"><?= e($copy['storage_provider']) ?></span>
  <?= e($copy['smtp']) ?>              <span class="code-muted"><?= e($copy['mail_provider']) ?></span>
  <?= e($copy['field_notes'] . "\n") ?>
    <span class="code-green"><?= e($copy['plugin_json']) ?></span>      <span class="code-muted"><?= e($copy['dependencies']) ?></span>
    <span class="code-green"><?= e($copy['plugin_php']) ?></span>       <span class="code-muted"><?= e($copy['routes_hooks']) ?></span>

<span class="code-green"><?= e($copy['app_services_get_pages_class']) ?></span></code></pre><div class="panel-caption"><span aria-hidden="true">↳</span> <?= e($copy['consume_a_contract_choose_your_provider']) ?></div></div>
            <div id="panel-core" role="tabpanel" aria-labelledby="tab-core" tabindex="0" class="architecture-panel" hidden><div class="code-topbar"><span><i></i><i></i><i></i></span><span><?= e($copy['core_src']) ?></span><span><?= e($copy['php']) ?></span></div><pre><code><span class="code-muted"><?= e($copy['just_enough_to_hold_it_all_together']) ?></span>
<?= e($copy['core_bootstrap_php_src'] . "\n") ?>
    <span class="code-green"><?= e($copy['contracts']) ?></span>       <span class="code-muted"><?= e($copy['service_interfaces']) ?></span>
    <?= e($copy['router_php_registry_php_theme_php_updater']) ?></code></pre><div class="panel-caption"><span aria-hidden="true">↳</span> <?= e($copy['update_the_core_preserve_everything_around_it']) ?></div></div>
        </div>
    </div>
    <div class="architecture-note"><span class="status-dot" aria-hidden="true"></span><p><?= e($copy['separate_folders_shared_contracts']) ?> <strong><?= e($copy['no_tangled_dependencies']) ?></strong></p><a href="<?= e($copy['href_docs_architecture']) ?>"><?= e($copy['explore_the_architecture']) ?> <span aria-hidden="true">↗</span></a></div>
</div></section>
<section class="quickstart-section shell" id="start">
    <div class="section-number">03 <span><?= e($copy['your_next_website']) ?></span></div>
    <div class="quickstart-layout"><div><h2><?= e($copy['from_zero']) ?><br><?= e($copy['to']) ?> <em><?= e($copy['your_thing']) ?></em></h2><p><?= e($copy['download_install_start_making']) ?><br><?= e($copy['there_s_no_toolchain_to_get_in']) ?></p><a class="button button-ink" href="<?= e($copy['href_download']) ?>"><?= e($copy['get']) ?> <?= e($site->meta['brand_prefix']) ?><strong><?= e($site->meta['brand_bold']) ?></strong> <span aria-hidden="true">↓</span></a><span class="download-note"><?= e($copy['v']) ?><?= e($app->version['version']) ?> <?= e($copy['mit_license_zip_download']) ?></span></div><div class="terminal"><div class="terminal-header"><span><i></i><i></i><i></i></span><span><?= e($copy['your_terminal']) ?></span><button class="copy-button" type="button" data-copy="quickstart-code" aria-label="<?= e($copy['aria_label_copy_installation_commands']) ?>"><?= e($copy['copy']) ?> <span aria-hidden="true">⧉</span></button></div><pre id="quickstart-code"><code><span class="code-muted"><?= e($copy['in_your_extracted_webspine_folder']) ?></span>
<span class="terminal-prompt" aria-hidden="true">$ </span><?= e($copy['php_bin_console_php_install'] . "\n") ?>

<span class="code-muted"><?= e($copy['start_your_local_development_server']) ?></span>
<span class="terminal-prompt" aria-hidden="true">$ </span><?= e($copy['php_s_localhost_8080_t_public_public']) ?></code></pre><div class="terminal-result"><span class="status-dot" aria-hidden="true"></span> <?= e($copy['open_localhost_8080_make_something_good']) ?></div></div></div>
</section>
<section class="roadmap-section shell"><div class="roadmap-top"><span class="section-number">04 <span><?= e($copy['honestly_where_we_are']) ?></span></span><span class="bootstrap-label"><?= e($copy['the_bootstrap_not_a_complete_cms']) ?></span></div><div class="roadmap-layout"><h2><?= e($copy['a_foundation']) ?><br><?= e($copy['with_a_future']) ?></h2><div><p><?= e($copy['the_essentials_are_here_routing_sqlite_persistence']) ?></p><p><?= e($copy['content_editing_authentication_more_database_providers_and']) ?></p><a class="text-link" href="<?= e($copy['href_docs_roadmap']) ?>"><?= e($copy['see_what_s_here_what_s_next']) ?> <span aria-hidden="true">↗</span></a></div></div></section>
<section class="closing-section shell"><span class="closing-asterisk" aria-hidden="true">✳</span><h2><?= e($copy['good_websites']) ?><br><?= e($copy['start_with_a']) ?> <em><?= e($copy['solid_spine']) ?></em></h2><a class="button button-ink" href="<?= e($copy['href_docs_quickstart']) ?>"><?= e($copy['build_your_next_idea']) ?> <span aria-hidden="true">↗</span></a></section>
