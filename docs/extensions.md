# Modular platform and AI access

Baseline, 30 September 2026. Modularity is an MVP requirement. WordPress is a useful ambition for extensibility; plugin parity is not a claim about the current implementation.

| Stage | Current implementation | Next contract work |
| --- | --- | --- |
| Content sources | `IContentSource`, immutable snapshots, SQLite conditional writes and capability reporting | External adapters, migrations and scoped writes; general page/media authoring contract |
| Components | Four typed kinds and mandatory validation | Versioned component schema, validator and renderer registry |
| Validation | Ordered `IContentValidator` extensions | Operation-specific policies and installation compatibility |
| Rendering | Replaceable `IWebsiteRenderer` | Versioned design packages and component composition |
| Build/export | Ordered `IArtifactContributor` extensions; sample site-index contributor | Durable worker, artifact storage and portable export packaging |
| Preview | Retained SQLite artifacts and loopback Development delivery | Replaceable artifact store, account-protected preview provider and review hooks |
| Delivery/cache | Prerendered pipeline, artifact source, replaceable cache and per-response header hooks | Dynamic rendering, production policies, separate host and broader request hooks |
| Deployment | Planned | Deployment provider, promotion checks, rollback and status events |
| AI/API | Planned | Scoped reads, change proposals/writes, build/export/preview/release operations |

Current extensions are explicitly supplied to the pipeline in stable order. Duplicate or empty extension IDs fail. Contributor files enter the same artifact digest as renderer output. Duplicate paths fail, failures return no finalized artifact, and extensions cannot append files after finalization. Required core validation always runs; there is no hook that disables it. Future post-build observers receive immutable artifacts and cannot mutate the reviewed bytes.

Compiled in-process plugins execute trusted application code with the host's privileges. Interfaces are not a security sandbox. Install them through an operator-controlled build/deployment process. Runtime discovery, hot-loading, dependency resolution, marketplace distribution and plugin isolation are not implemented.

Before expanding installation, define a module manifest containing ID, version, supported platform/contract versions, dependencies and declared capabilities. Validate incompatible or conflicting registrations at startup. Capture installed module versions and configuration in release provenance. No manifest file is added until a loader consumes it.

AI clients should reach almost every workflow through typed management operations, using separate revocable credentials and server-enforced scopes. Content edits, design proposals, validation, build, export, preview, publish and rollback need distinct permissions. AI can propose design/module code for review through the development workflow; website credentials do not install executable plugins. Publication remains an explicit permission on an exact reviewed artifact.

Add hooks alongside their owning service rather than inventing empty interfaces now. Deployment hooks must preserve atomic promotion and expected-live-release checks. External notifications need durable, idempotent delivery with bounded retries and attributable audit. API operations and hooks must document ordering, cancellation, failure behavior and which data they may access.
