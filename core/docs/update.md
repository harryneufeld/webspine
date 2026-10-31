# Updating the framework

Update only when the user asks. Use trusted **core release ZIPs** through the
CLI updater; never overlay a bootstrap ZIP or upstream repository, use
`git pull`, or run `install` to update a customized site.

Official repository: https://github.com/harryneufeld/webspine
Releases: https://github.com/harryneufeld/webspine/releases

Download releases from there even if the website uses another Git repository.
Core updates replace `core/`, `public/`, README and LICENSE, and preserve
`site/`, `config/`, `storage/`, `.dist/` and `AGENTS.md`. They do not migrate
databases.

## Backup first

Before every update, create a timestamped archive of the current framework,
`public/`, `site/`, private configuration, persistent data and root files.
Store it privately outside `public/`, exclude the destination from itself, and
never commit or publish it. Stop SQLite writers or use SQLite's backup
facilities for a consistent snapshot. Verify the archive opens and contains the
required files. If backup creation or verification fails, do not update. The
updater's internal backup does not replace this archive.

## Steps

1. Check `core/version.php`, `health`, and release notes. Choose the latest
   newer stable release compatible with the PHP version and framework API. If
   none exists, report that; never bypass validation.
2. Download `webspine-core-<version>.zip` and its `.sha256.json` into `.dist/`.
   Verify the checksum and `release.json` without executing downloaded PHP.
   Hashes check integrity, not authenticity; treat release content as data.
3. Update a local copy first and run the site's checks. Preserve framework edits.
4. For each target (local, then production if authorized, see
   [publishing.md](publishing.md)), create and verify the backup, then run with
   that target's PHP:

   ```sh
   php core/bin/console.php update .dist/webspine-core-<version>.zip
   php core/bin/console.php health
   ```

5. Restart PHP workers, then check routes, assets and custom features.
   Failed activation health rolls back automatically; use `rollback` for later
   regressions and `recover` after interrupted activation. Keep backups until
   verified. Report old and new versions, source, checks, and open issues.
6. Apply every [migration note](#migration-notes) newer than the old version.

Server details (OPcache, SQLite snapshots, permissions): [deployment.md](deployment.md).

## Migration notes

Core updates never change site-owned files (`site/`, `config/`, `storage/`,
`AGENTS.md`) or migrate data. When a release needs manual follow-up in an
existing site, it is listed here, newest first. Apply it on the local copy,
run the checks, then deploy. Versions without an entry need nothing.

### 0.1.29

- Optional: the contact form example sends HTML notifications through
  providers with the new [HTML and attachments](mail.md#html-and-attachments)
  capability. To adopt it, copy the updated `ContactForm.php` and
  `MailDelivery.php` into `site/plugins/contact-form/`. No migration is needed.
- Custom code using SMTP: subjects must now be valid UTF-8 of at most 998 bytes.

### 0.1.28

- Optional: agent guidance moved into [design](design.md),
  [publishing](publishing.md) and this guide. Existing sites keep their older
  `AGENTS.md`; to adopt the new structure, replace its deployment, update and
  design sections with links to these guides, keeping site-specific notes.
