# Deployment

Use PHP 8.3+, PDO SQLite, ZipArchive for releases, and OpenSSL for SMTP.
Serve public/ only. Never serve the repository root. Keep database files on a
local filesystem; network filesystems are unsupported. Supply HTTPS at the web
server or proxy. This bootstrap assumes deployment at the URL root.

Apache: public/.htaccess requires mod_rewrite and AllowOverride for routing and
Options. Disable indexes and ensure no alternative aliases expose private paths.

nginx example (adjust paths and PHP-FPM socket):

```nginx
server {
    listen 443 ssl;
    server_name example.com;
    root /srv/webspine/public;
    # Configure TLS certificates here.
    location / { rewrite ^ /index.php last; }
    location = /index.php {
        include fastcgi_params;
        fastcgi_param SCRIPT_FILENAME $document_root/index.php;
        fastcgi_pass unix:/run/php/php8.3-fpm.sock;
    }
    location ~ \.php$ { return 404; }
    location ~ /\. { deny all; }
}
```

The web worker needs read access to code/config and write access to storage/.
Keep configuration and SQLite private. Give only the maintenance operator write
access to framework files. A runtime user should not download/apply updates.

Install via CLI before serving. Make separate backups of config, themes,
plugins, and persistent data. Do not copy a live SQLite file while writers are
active; use SQLite's backup facilities or stop writers. No online backup command
is provided. Monitor /health (generic public status only); detailed CLI health
may contain filesystem errors and must remain private.

Drain PHP workers before updates and reset OPcache afterwards. Requests and CLI
commands cooperate with a filesystem lock, but cached bytecode can outlive a
file replacement. Test releases with your chosen site/plugins/theme on a clone of the
site before production. Run recover after an interrupted activation. Retain
private backup directories until successful deployment is verified.

SMTP is optional and uses TLS. Never commit SMTP credentials. No credentials
are in release packages. PHP's development server is local-only development
tooling, not a production server.
