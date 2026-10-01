# Local authoring workflow

Updated 1 October 2026. This is a Development-only workflow for one website, its original owner and role-limited users; hosted management remains planned.

From the repository root:

```text
dotnet run --project src/Webspine.Management -- --urls http://127.0.0.1:9087 --environment Development --Management:Enabled true
```

Open `/manage`. Create the owner account before site setup; existing installations keep their content and receive this account-bootstrap step. Sign in to access editing, exports and previews. Start blank creates one minimal Home page; Use demo explicitly copies Home, Services, Products, About and Contact, including their sample image. Neither application startup nor visiting the editor seeds content. Setup cannot replace an existing website. The separate `Demo:Enabled` read-only fixture remains independent.

Edit page names, descriptions and approved fields in text, image, CTA and card sections. Add page creates a canonical route with a starter text section. Saving creates a new opaque revision and never publishes. A stale form returns a conflict and preserves its submitted page values; reopen the latest form and reconcile your changes. Mandatory content validation rejects invalid links, duplicate routes and unapproved fields. This slice has no layout designer, section insertion/removal, image uploads or page deletion.

Build preview captures content and stored images, validates them and retains the complete generated artifact. The resulting `/manage/preview/{id}/` URL remains byte-for-byte unchanged after later edits or restart. A revision change during the build rejects the result. Previews use the existing sample design and delivery pipeline with no shared cache and `Cache-Control: no-store`. Builds run in the management request; durable background jobs and publication remain future work. Keep the preview URL to reopen it: a preview-history screen is not implemented.

Download content/images exports a ZIP containing the current content snapshot and referenced media. This is portable content export, not a complete installation backup. Import, full installation restore, retention cleanup and published-release rollback are not implemented. Offline content recovery on a copy is described in the spinecms storage guide.

Website settings edits the shared name/title and language through the same conditional authoring operation as the API. Updated values appear in the management overview and new page titles/header/footer/language attributes. Changing settings creates a revision and does not mutate existing previews.

## Storage and access

By default the host stores `webspine.db`, `accounts.db` and Data Protection keys in `.local/` beneath its content root (`src/Webspine.Management` with the command above). `--Management:DataDirectory /absolute/path` selects another directory; relative paths resolve from the process working directory. Prefer an absolute path when running outside development. Runtime directories and database files are ignored by Git.

Schema version 2 stores immutable JSON content revisions with explicit v1/v2 tags, the current head, copied media, retained artifacts and content-transition mappings. Initialization upgrades metadata without migrating site content. Writes compare and save in one SQLite transaction. The source adapter owns content access; the editor does not issue SQL. Preview storage currently uses this database; a replaceable artifact store remains release work. See [spinecms storage](spinecms-storage.md) for explicit composition migration/recovery on a copy. Normal management still requires a v1 head until #16.

Management requires an explicit opt-in and refuses startup outside Development. Every management request checks loopback IP and localhost Host; state-changing forms require ASP.NET Core antiforgery validation. All management and preview responses disable browser/proxy caching. Management now requires an owner Identity session and server permissions. Initial owner setup must be completed by the operator; local filesystem access still controls the databases and keys. Do not expose it through a proxy or tunnel. Data Protection keys are stored unencrypted and require local filesystem protection, as does the database.

ASP.NET Core Identity implements the owner account and cookie session. Connected apps creates separate scoped, expiring and revocable credentials for the [authoring API](authoring-api.md). People and access supports operator/editor/reviewer accounts and access changes. Change password provides authenticated self-service, while the [account guide](accounts.md) documents local operator-assisted recovery. Hosted access and identity-schema upgrade workflows remain unimplemented. Existing previews are development artifacts, not reviewed or published releases.

## Verification

`dotnet run --project tests/Webspine.ManagementChecks` checks persistence, transactions, competing writers, conflicts, retained output, explicit setup, exports, antiforgery, Host restrictions and Production rejection. It uses isolated temporary databases and stops its own hosts. `dotnet run --project tests/Webspine.Checks` verifies core content/build rules.
