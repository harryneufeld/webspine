# Project charter

Status: MVP scope baseline, updated 30 September 2026. The selected product name is `webspine`, described as a modular web engine. Its wordmark is web**spine**, with “spine” bold; `webspine` is the repository folder. C# namespaces/projects use `Webspine` by convention.

## Purpose

Help a freelance developer or small agency maintain customer websites whose content can be edited manually or through the customer's chosen AI tool, while preserving approved design and control over publication.

The product is an open-source, self-hosted modular web engine for creating, managing, rendering and delivering websites. A small built-in CMS supplies content by default. A site can instead use a dedicated external CMS through the same content adapter boundary. External AI tools use the same authenticated website API as other integrations.

## First users and outcome

- Operator/developer: connects the CMS, manages accounts and design, deploys and restores the installation.
- Content editor: changes permitted content and reviews a preview.
- Publisher: reviews a specific candidate and promotes or restores a release. One person may hold several roles.
- Integration: acts through an individually scoped credential, with attributable actions.

MVP success means one pilot customer can run a small informational website using the built-in CMS, edit content manually and through one external AI tool, review changes, publish the exact reviewed artifact, and recover from a failed release or host loss using documented procedures. A reference external connector and a rehearsed source migration demonstrate that the built-in source can be replaced.

## Proposed MVP boundary

- One customer and one website per installation; several pages in one configured language.
- A small built-in CMS: pages, approved component fields, managed images, versioned drafts and conditional writes, editable through the portal and API.
- One reference external CMS connector with explicit field mapping and tested revision behavior; selecting it does not block building the default experience.
- One active content source per site, with an operator-guided migration from built-in content to the supported external CMS.
- A small approved component set: heading/text, image, CTA and cards; pages use developer-controlled layouts. The optional demo has five pages: Home, Services, Products, About and Contact. The final pilot schema remains a discovery deliverable.
- A modular core with documented extension contracts across content, rendering, builds, exports, preview and deployment. External AI clients use scoped operations across these stages as they are implemented.
- Local human accounts using maintained authentication components, plus separate revocable integration tokens.
- Scoped content editing, protected previews, release history, publication and rollback.
- Versioned design inputs and retained content/assets captured in each release.
- Durable platform state, recoverable build jobs, and public delivery that survives management downtime.
- Documented container deployment to one operator-managed host, HTTPS setup, backup and restore.
- One demonstrated external AI client using the API. MCP is optional unless the pilot client requires it.

## Deferred

Shared multi-tenant hosting; additional external CMS products; universal connector compatibility; a general-purpose CMS schema designer; embedded AI agents; a drag-and-drop site builder; arbitrary customer code execution; plugin marketplace and runtime hot-loading; SSO/Keycloak; multilingual publishing; commerce, forms and other dynamic features; distributed high-availability infrastructure; automatic fleet-wide design updates. Core modularity and extension contracts are part of the MVP.

## Product rules

Cross-platform operation is required: Windows and Linux, with macOS included in automated verification. Linux is the primary hosting target. Application services and required development/verification commands must avoid Windows-specific APIs, paths and shell dependencies. Future storage, media and CMS components must support the target platforms.

1. Keep one authoritative content source per site: built-in or external. Release snapshots are immutable records. External mode disables built-in editing for that site; no automatic two-way synchronization is implied.
2. Keep content separate from design. Content cannot supply arbitrary executable templates or CSS.
3. Reject stale writes; never claim conflict safety that the CMS cannot provide.
4. A draft or CMS publication does not automatically replace the public website.
5. Approval identifies exact artifact bytes; publishing does not rebuild them.
6. Failed builds and failed promotion leave the current public release usable.
7. Website rollback restores retained output without reversing CMS edits.
8. Credentials and permissions are explicit. AI-provider choice is independent of the platform.
9. Preserve operator data during upgrades; verify recovery rather than relying on backup creation alone.

## Decisions required before implementation

| Decision | Proposed starting point | When needed |
| --- | --- | --- |
| Pilot customer/site | One small informational website | First discovery task |
| Default CMS scope | Small website-focused content model behind the common adapter | Before content implementation |
| Reference external CMS and supported version | Choose from pilot needs; prove draft writes, permissions and revisions | Before external connector implementation |
| Repository owner/name/visibility | `harryneufeld/webspine`, initially private; selected 30 September 2026 | Decided |
| Hosting target | One operator-managed Linux host with containers | Before deployment design |
| Authentication library, database and job storage | Choose maintained components after default content and deployment discovery | Before persistent services |
| Source approval policy | Explicit built-in draft eligibility and supported external CMS workflow | Before release implementation |
| Initial AI client | One client that can reach the management API | Before integration acceptance |

C# is an established project choice. The MVP foundation targets .NET 10; implementation decisions must account for the selected deployment environment. Existing MIT licensing is retained; third-party components require a compatibility review when selected.

## Working agreements

`PROJECT.md` owns purpose and scope. `docs/architecture.md` owns boundaries and design decisions. `docs/mvp-plan.md` is the initial backlog seed. GitHub issues will own task status once created; link issue numbers back to the plan and stop maintaining duplicate status lists.

An implementation issue is complete when its acceptance criteria are demonstrated, relevant checks pass, and affected API/operation documentation is updated. Record significant decisions in short files under `docs/decisions/` when they are actually made.

No separate machine-readable project manifest is needed for the MVP. Add configuration files only when a build or deployment tool consumes them.
