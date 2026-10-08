# Building and publishing framework releases

Maintainer tooling builds from trusted framework source, never a customized
website or an untrusted downloaded ZIP. Existing websites consume the core ZIP
through the updater; bootstrap ZIPs are only for creating new websites.

## Canonical source and archive checks

The repository's `.gitattributes` selects LF for project text. Upstream files
under `core/vendor/` retain exact bytes under `-text`; only repository-owned
`core/vendor/dependencies.json` has a targeted `text eol=lf` override. Do not
normalize upstream library files or change their recorded checksums.

Use a clean checkout of the exact reviewed commit, or a Git export for controlled
release source. The following commands work from PowerShell or a POSIX shell
(extract the export with your ZIP tool):

```sh
git archive --format=zip --output=.dist/release-source.zip HEAD
```

Extract into a new private directory, for example `.dist/release-source/`.
Git export avoids working-tree line-ending conversion and excludes untracked or
ignored private files. Check the export's commit/version before building; it is
trusted executable source. Exporting does not validate or authenticate source.
On Windows, check `git ls-files --eol` if a checkout has unexpected CRLF. A fresh
checkout after the `.gitattributes` change applies the targeted metadata rule.
Do not rewrite a customized site's content just to create a framework release.

```sh
php core/bin/release.php build .dist/release-source .dist/release-assets
php core/bin/release.php verify .dist/release-assets/webspine-core-0.1.18.zip
php core/bin/release.php verify-set .dist/release-assets 0.1.18
```

`build` produces core and bootstrap ZIPs and their `.sha256.json` sidecars.
`Release::package()` and CLI `package` reject CRLF or bare CR in project text
instead of silently transforming source. The policy covers PHP, Markdown, CSS,
JS, SVG, JSON, HTML, TXT, shell/PowerShell, YAML and CONF extensions (case
insensitive), plus LICENSE, Dockerfile and the shipped extensionless Git files.
New text formats must extend the explicit policy. Binary assets and excluded
upstream vendor files retain their original bytes. Source remains unchanged;
inventory hashes describe those exact bytes. ZIP timestamps/compression can
vary between builds; identical ZIP bytes across builds are not guaranteed.

Packaging checks the completed temporary archive before finalizing the ZIP.
Archive verification reads PHP as data: it checks bounded metadata/inventories,
required files, exact case-sensitive entries, portable paths, duplicate/link
rejection, size limits, file hashes, text endings and recorded vendor checksums.
Sidecar verification checks its filename and whole-ZIP hash. A verified pair
must have the requested version/types and identical managed core files. No
downloaded PHP is executed by archive verification. A hash does not authenticate
source, and these checks do not review trusted executable site/plugin behavior.

`verify-set` returns exactly four asset descriptors for the requested version;
unrelated old archives in the directory are ignored. An optional final trusted
source argument additionally compares inventories against that directory.
Never upload with a directory wildcard. A failed build or verification is a
stop condition; old files in an output directory are not evidence of success.
The updater's existing staging checks remain compatible with older releases;
the new canonical-text rule is a packaging/publication policy.

## Stable publication

Run framework, Starter and hosting CI on the PR; merge it, then wait for merged
commit hosting CI. Test fresh packaged installation/health, relevant features
and updater staging with disposable local data before publishing. Keep release
notes aligned with implemented behavior and compatibility/adoption limits.
See [verification](verification.md) and [deployment](deployment.md).

Framework CI runs the disposable archive tests on Linux. The publisher's HTTP
and Git boundaries are simulated with the real builder/verifier, testing exact
asset selection and rejection of bad digests, failed CI and existing tags:

```powershell
./core/tests/publish-release.ps1 -Php /path/to/php
```

PR CI runs this offline check too; it never connects to GitHub or publishes.

From a clean Git checkout of the current merged `main` commit, with no untracked
or ignored files in inventory paths, use PowerShell 7, Git and PHP with ZipArchive:

```powershell
./core/bin/publish-release.ps1 -Version 0.1.18 `
    -AssetsDirectory .dist/release-assets -VerifyOnly

# Supply GITHUB_TOKEN privately through the environment; never commit or print it.
./core/bin/publish-release.ps1 -Version 0.1.18 `
    -AssetsDirectory .dist/release-assets -Commit <full-merged-sha> `
    -NotesPath .dist/release-notes.md
```

Use `-Php /path/to/php` for a portable executable. `-VerifyOnly` performs local
pair verification and prints only the four selected descriptors, without
network access or a token. Publication verifies the clean checkout's commit,
committed version and source inventories, current official `main`, completed
CI (including `servers`), and absence of an existing tag/release. It creates a
draft, uploads only the selected four files, compares sizes and GitHub upload
SHA-256 digests, checks the exact draft asset set, then publishes as latest
stable and verifies the tag target. Changed files or failed uploads leave the
draft unpublished for operator review; existing drafts/tags are never replaced.

The publisher targets only `harryneufeld/webspine` and stable releases. It does
not merge PRs, perform production updates, run database migrations, or implement
signature verification. Git/PowerShell are maintainer-tool dependencies, not
requirements for running a webspine website. Prerelease publication remains a
separate operator workflow.
