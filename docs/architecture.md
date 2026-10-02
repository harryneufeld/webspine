# webspine MVP architecture

Status: planning baseline, 27 September 2026. Future components below are proposals, not claims about the current implementation.

## Current MVP foundation

`Webspine.Core` defines content/source/build contracts. `Webspine.Content.Sqlite` implements transactional versioned content, copied media and retained previews behind the source contract. The management host consumes installed starters from `Webspine.Examples`, with independent Studio and Fieldwork Razor packages for composition. The original renderer remains in `Webspine.Rendering.Legacy` for read-only fixtures; `Webspine.Demo` remains a separate verification/discovery fixture, without a management dependency. `Webspine.Delivery` and `Webspine.Caching.Memory` supply delivery and replaceable caching. Builds validate immutable snapshots, run ordered extensions and finalize files with digests. Owner Identity accounts, scoped app credentials, typed metadata-driven composition editing and conditional writes are implemented for local Development use. Fixed human roles, account administration, password changes and local operator recovery are implemented. Hosted configuration, durable workers and publication remain planned. See `management.md`, `../VALIDATION.md`, `content-contract.md` and `extensions.md` for current behavior and limits.

## Evidence from the separate PoC

The sibling `website-poc` experiment verified scoped content changes, stale-write rejection, retained artifact publication, rollback and browser review. Its demo source and runtime state are not imported here. Its checks establish evidence about that experiment only. The architecture below is the intended MVP design, not an inventory of implemented components.

## Proposed logical boundaries

```text
Human / external AI client
           |
   Authenticated management API + review portal
       |                  |                 |
   CMS adapter      Platform database   Build job queue
       |                                    |
 Built-in OR external CMS            Restricted build worker
                                            |
Versioned design + frozen content/assets -> retained artifact
                                            |
                                    Deployment/promoter
                                            |
                                  Public delivery pipeline
```

These are responsibilities, not a requirement for a microservice per box. Prefer one modular C# management application, a separately run worker, and separate public delivery. A durable database-backed job mechanism may be sufficient; select the storage design explicitly.

Delivery can serve prerendered bytes or, in future, render Dynamic pages from versioned release inputs. Browser interactivity is independent of either mode. The current demo uses the extracted `Webspine.Delivery` pipeline and separate memory-cache implementation within the management host; independent production delivery remains planned. See `delivery.md` for implemented cache/HTTP hooks and the limits of this slice. Exact-byte approval applies to prerendered output; dynamic release-bundle approval requires a separate policy before implementation.

Modularity is a product requirement. Each stage should depend on a replaceable contract, with documented hooks and compatibility rules. Extension code may add behavior but cannot weaken mandatory validation, authorization or artifact identity. AI integrations use attributable management operations; installing executable plugins is a separate operator responsibility. See `extensions.md` for the implemented seams and planned stages.

Reused UI elements share a layout/component contract within their design package. The demo's header, main and footer use the same `site-container`, maximum width and page gutters; title/prose widths are shared readable variants. Reserve scrollbar space so changing page height does not shift the shell. Future design packages and management components should centralize these rules rather than adding incidental per-route overrides.

| Data or capability | Owner |
| --- | --- |
| Business content and source editorial workflow | Active built-in or external CMS |
| Website schema, safe writable mappings and integration permissions | Platform and connector |
| Templates, styles, components and design assets | Versioned design repository |
| Human accounts, scoped token metadata, jobs, release records and audit | Private platform storage |
| Frozen content, referenced assets and generated output | Private release/artifact storage |
| Current live release | Deployment component |
| Public HTTP responses | Delivery service with read access only to published output |

## Content boundary

The built-in CMS implements the same source interface as external adapters. It stores website pages, permitted component values, media references and versioned drafts using the chosen durable storage. The portal is its human editor; external AI clients use the authenticated website API. Use maintained storage and identity components and keep the initial content model small.

Each site selects one active CMS: spinecms (the default) or an external CMS. When an external CMS is active, spinecms accepts no content writes. The webspine management board and API remain editing entry points for the operations supported by that connector. Unsupported operations are reported explicitly, never persisted through a hidden spinecms fallback. Frozen release snapshots stay independent of the source; they are never used as an editable synchronized copy.

Changing sources is an explicit migration: export content/assets, configure field mappings, import or reconcile with destination content, validate the resulting pages, rehearse a build, freeze edits briefly for final transfer, then switch the site source. Preserve original source data and previously published releases for recovery. Candidates from the prior source become ineligible for normal publication; rollback to retained published artifacts remains possible. Switching source does not publish a new website automatically.

Use opaque source revision references rather than assuming every CMS has an integer revision. Declare read, draft-write, conditional-write and workflow capabilities individually. Only fields with an explicit safe reverse mapping may be writable.

If the chosen CMS cannot enforce a required conditional update, this is a discovery blocker for automated writes. Decide on a supported source-side strategy or narrow the pilot to proposals/read-only operation; do not silently weaken the requirement.

CMS-native edits bypass the platform API. Validate every release snapshot against the website contract regardless of edit origin. Service-account connector credentials do not imply the initiating user's source permissions: define and test delegated access or an explicit permission mapping.

## Accepted composition and data direction

[Decision 0013](decisions/0013-static-razor-design-package-proof.md) selected static Razor components after the bounded [#24 prototype](razor-prototype.md). [Decision 0014](decisions/0014-configured-static-design-packages.md) separates definitions/validation from package-owned markup/styles/assets and captures executable dependency bytes. Decision 0017 retires legacy authoring and migration tooling; all starters now create native v2 content. Retained artifacts remain independently readable. [Decision 0015](decisions/0015-data-only-content-editor-metadata.md) adds bounded data-only field metadata for board forms and authenticated API discovery, demonstrated by an independent FAQ definition. [Decision 0016](decisions/0016-independent-layouts-and-installed-starters.md) proves Studio and Fieldwork on the same pipeline, removes fixed Core Region names and the management Demo dependency, and supplies the [developer quickstart](package-quickstart.md). Production release/hosting remains separate work. If management needs replacement, redesign its navigation/editing around user tasks, clear hierarchy and human-readable labels, with keyboard/mobile review, rather than copying the current page structure.

[Decision 0009](decisions/0009-composition-records-and-substitutable-cms.md) defines Block trees, Groups, named Regions, layouts (Spines), site-owned Shared Blocks, Patterns and typed Records. The default [composition v2 model](composition.md) implements contracts/registration/validation/builds. The [spinecms store](spinecms-storage.md) supports atomic v2 persistence and retained history; the [composition board/API](composition-editing.md) provide typed editing, media and shared authority for every new website. Patterns/Records remain follow-ups. Decisions [0010](decisions/0010-additive-composition-v2-and-registration.md) and [0011](decisions/0011-spinecms-versioned-storage-and-explicit-migration.md) record historical compatibility/storage choices, superseded for legacy authoring by [decision 0017](decisions/0017-v2-only-management-authoring.md).

Design packages own layout/Region definitions, styles, Pattern structures and rendering. The active CMS owns page composition, Block values, shared instances, Pattern inputs, media references and content Records. Editors and AI use structured, authorized operations within registered type/placement rules. Alternative editing UIs use the same API guarantees as the default board. Application services enforce rules; spinecms persistence initially uses SQLite without exposing it as the common contract.

Ordinary Blocks have page ownership; Shared Blocks have site ownership. Stable object identity is separate from stable placement identity. Shared references initially have no local overrides. All edits retain site-wide conditional revisions; shared writes require additional authority and an affected-page summary. Resolve and validate references from frozen inputs before rendering.

External business-data providers may supply typed Records, with explicit provider identity, field mappings and capture evidence. They do not become a second CMS or receive implicit write authority. Orders, inventory rules and similar processes remain in their originating application. Prerendered candidates capture bound values/assets; runtime queries on Dynamic pages require a separate policy. First implement composition and shared content, then a bounded Pattern/Record example; defer a general schema designer and business-provider implementation.

## Release lifecycle

1. Authorize a specific operation, page/field and site.
2. Read source revisions and validate a proposed change.
3. Conditionally save to the CMS, honoring its editorial requirements.
4. Freeze eligible content, referenced assets, design revision, contract version and build inputs. Define a consistency strategy if the CMS cannot return an atomic snapshot.
5. Execute a durable build job; retain successful output and its digest.
6. Show an authenticated preview and content/design change summary.
7. On publication, recheck authority, candidate digest, expected current release and applicable source/design eligibility conditions.
8. Atomically promote the same artifact, with recoverable deployment status and audit.
9. Restore an eligible retained release through explicit rollback without changing CMS content.

The exact policy for a CMS edit after a snapshot is frozen must be decided and tested. The PoC rejects candidates if source or design has changed; preserve that behavior until an explicit decision replaces it.

## Runtime boundaries

Target Windows, Linux and macOS with Linux as the primary hosting platform. Use portable .NET APIs and dependencies, filesystem paths assembled with platform APIs, and shell-independent required verification. IIS and Windows services must not be prerequisites. Validate case-sensitive filenames, permissions, process startup/cleanup and dependency support on real Linux/macOS runners. Container packaging remains planned and does not replace native portability checks.

- Separate human sessions from integration bearer tokens. Store token digests; show raw integration tokens only at creation. Support expiry and individual revocation.
- Restrict preview access, avoid caching private content, and keep management/CMS credentials out of generated output.
- Build workers receive frozen inputs without production deployment or CMS credentials. Initially render trusted templates only.
- Public delivery can read published artifacts but cannot access account storage, source credentials, drafts or audit records.
- Define least-privilege container volumes, filesystem ownership, network exposure and secret injection during deployment work.
- Retain structured operational logs without tokens and provide release/job health information.

## Decisions to record

Define the built-in content model and common adapter contract first; select the reference external CMS before its connector work. Then record authentication/session design; platform database and queue/retry semantics; source permission/snapshot and migration strategies; design revision capture; artifact retention and promotion recovery; deployment topology; and backup/upgrade compatibility.

Each decision record should state the problem, chosen option, alternatives, consequences and verification needed. Do not write records that imply unresolved options are already accepted.
