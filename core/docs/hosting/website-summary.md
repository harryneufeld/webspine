### Simple PHP hosting

Deploy webspine on Apache, nginx, or CloudPanel with PHP 8.3+, PDO SQLite,
ZipArchive for updates, and OpenSSL for SMTP. Point the document root to `public/`
and let the PHP worker write to `storage/`; keep code and private configuration
outside public access. Deployment currently assumes the domain root.

Apache needs PHP, `mod_rewrite`, and the documented `.htaccess` permissions.
nginx needs PHP-FPM and routing to `index.php`. In CloudPanel, exclude `/assets/`
from the generic static-file rule so theme assets reach PHP. Back up before
updates and drain/restart PHP workers to clear OPcache.

[Copyable server configurations and deployment checks](https://github.com/harryneufeld/webspine/blob/main/core/docs/deployment.md).
