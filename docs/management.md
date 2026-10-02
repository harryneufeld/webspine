# Local authoring workflow

Updated 2 October 2026. Composition v2 is the sole management authoring workflow. This remains an opt-in, Development-only, loopback/localhost-restricted workspace for one website; hosted management and publication remain planned.

```text
dotnet run --project src/Webspine.Management -- --urls http://127.0.0.1:9087 --environment Development --Management:Enabled true
```

Open `/manage`, create the owner account, and create the website. Both blank and example starters create native v2 content immediately. Studio is the default package; Fieldwork can be selected with `--Design:Package fieldwork --Design:Version 1`. Use a separate data directory for each installation. Startup and reads never seed content, convert old data or replace an existing website.

The composition board is the default `/manage` screen and is also available at `/manage/composition`. Choose a page, edit its details or content, and add/arrange elements within the selected design's areas. Forms use registered field metadata and readable labels. Shared content has separate editing authority and exact affected-page review. PNG upload, grouping, movement, detachment and removal are documented in [composition editing](composition-editing.md).

**Add a page** creates a canonical route and fills the design's required areas following the first page's required-area shape, using its existing shared references or registered defaults. It does not change shared content. Update the shared navigation separately to link to the new page. A design without safe required-area defaults returns an explicit unsupported-operation response. Page deletion and route changes remain planned.

Every save creates an opaque revision. Conflicting forms never overwrite newer content; reopen the latest form and reconcile changes. The board does not preserve unsaved field text after a conflict. Website settings change the shared name/title and language; new previews reuse those values and old previews keep their captured values.

**Build preview** freezes content, media, selected package definitions/components/styles/scripts and executable dependencies, then retains the generated artifact. `/manage/preview/{id}/` serves the captured bytes without rebuilding them, including after edits or restart. Builds run in the request and reject stale candidate saves. Keep preview links to reopen them; a preview-history screen is not implemented. Private `build-inputs/` archives are never served through HTTP.

**Download content** exports the current v2 snapshot and referenced media as a ZIP. This is content export, not a complete installation backup. Import, retention cleanup, full installation restore, durable background workers and public publication remain planned.

## Storage and access

The default private directory is `.local/` beneath the host content root. It contains `webspine.db`, `accounts.db`, Data Protection keys and private build inputs. `--Management:DataDirectory <absolute-directory>` selects another directory. Runtime data is ignored by Git.

SQLite stores immutable versioned snapshots, a current head, copied media and retained artifacts. Conditional writes are atomic and capture reads consistent source/media inputs. See [spinecms storage](spinecms-storage.md). Identity sessions authenticate human users; connected apps use separate scoped, expiring, revocable bearer credentials for [API v2](authoring-api.md). People and access manages fixed roles; password changes and [offline account recovery](accounts.md) remain available.

Management enforces local IP/Host restrictions, server permissions and antiforgery validation for forms. Preview/management responses disable browser/proxy caching. Data Protection keys are unencrypted and require private filesystem protection. Do not expose this development workspace through a proxy or tunnel.

## Older workspaces

The legacy editor, v1 write API and content inspect/migrate/restore tooling have been removed. There is no legacy authoring-data migration requirement or conversion path. A v1 head opens a clear unsupported-workspace explanation. Stop the host, preserve the original private directory, configure a separate empty data directory, restart and create a fresh v2 website. Do not delete or overwrite the original directory to perform setup.

Known old preview URLs remain readable in the original workspace with preview permissions. Retained output is independent of the selected design and editable content contract. Removed legacy UI/API operations return 410 after authentication/permission checks; API v2 source reads reject unsupported v1 authoring with 501. Obsolete offline content commands exit with an explanation before touching storage. Account recovery is separate and is still supported.

## Verification

`dotnet run --project tests/Webspine.ManagementChecks` verifies all four native starter combinations, v2 editing/API/account permissions, conflicts, page creation, atomic storage, exact retained output, retired operations and local-host restrictions. Checks use isolated temporary directories and stop their own hosts. Core and Razor checks verify mandatory contracts/builds and configured static rendering. CI runs on Windows, Linux and macOS.
