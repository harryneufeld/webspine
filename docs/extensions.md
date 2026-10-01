# Modular platform and AI access

Baseline, 30 September 2026. Modularity is an MVP requirement. WordPress is a useful ambition for extensibility; plugin parity is not a claim about the current implementation.

Accepted composition direction: [decision 0009](decisions/0009-composition-records-and-substitutable-cms.md). The opt-in [v2 composition library](composition.md) implements type/schema/validator/renderer registration and adapter capabilities; existing v1 management still uses four section types. CMS-managed composition and optional external business-data providers have separate authority. Provider and editing integrations remain planned.

| Stage | Current implementation | Next contract work |
| --- | --- | --- |
| Content sources | `IContentSource`, `IWebsiteAuthoringSource`, shared approved fields and SQLite conditional writes | External adapters, source migrations and full component/media authoring |
| Components | Four v1 typed kinds; opt-in v2 registry with five standard types, ownership/reference validation and custom-type checks | Storage migration and UI/API integration; module packaging/loader |
| Validation | Ordered `IContentValidator` extensions | Operation-specific policies and installation compatibility |
| Rendering | Replaceable `IWebsiteRenderer` | Versioned design packages and component composition |
| Build/export | Ordered `IArtifactContributor` extensions; sample site-index contributor | Durable worker, artifact storage and portable export packaging |
| Preview | Retained SQLite artifacts and loopback Development delivery | Replaceable artifact store, account-protected preview provider and review hooks |
| Delivery/cache | Prerendered pipeline, artifact source, replaceable cache and per-response header hooks | Dynamic rendering, production policies, separate host and broader request hooks |
| Deployment | Planned | Deployment provider, promotion checks, rollback and status events |
| AI/API | Bearer scopes, conditional page/settings operations and retained previews through shared authoring operations | MCP adapter, component/media operations, durable audit and release permissions |

Current extensions are explicitly supplied to the pipeline in stable order. Duplicate or empty extension IDs fail. Contributor files enter the same artifact digest as renderer output. Duplicate paths fail, failures return no finalized artifact, and extensions cannot append files after finalization. Required core validation always runs; there is no hook that disables it. Future post-build observers receive immutable artifacts and cannot mutate the reviewed bytes.

Compiled in-process plugins execute trusted application code with the host's privileges. Interfaces are not a security sandbox. Install them through an operator-controlled build/deployment process. Runtime discovery, hot-loading, dependency resolution, marketplace distribution and plugin isolation are not implemented.

Before expanding installation, define a module manifest containing ID, version, supported platform/contract versions, dependencies and declared capabilities. Validate incompatible or conflicting registrations at startup. Capture installed module versions and configuration in release provenance. No manifest file is added until a loader consumes it.

AI clients should reach almost every workflow through typed management operations, using separate revocable credentials and server-enforced scopes. Content edits, design proposals, validation, build, export, preview, publish and rollback need distinct permissions. AI can propose design/module code for review through the development workflow; website credentials do not install executable plugins. Publication remains an explicit permission on an exact reviewed artifact.

Add hooks alongside their owning service rather than inventing empty interfaces now. Deployment hooks must preserve atomic promotion and expected-live-release checks. External notifications need durable, idempotent delivery with bounded retries and attributable audit. API operations and hooks must document ordering, cancellation, failure behavior and which data they may access.
