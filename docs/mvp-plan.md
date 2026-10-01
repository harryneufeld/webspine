# MVP plan and GitHub issue seed

Updated 30 September 2026. The first implementation issues are linked below; GitHub owns their status. The broader MVP-01–15 entries remain planning IDs, not GitHub issue numbers.

## First authoring slice

| GitHub issue | Scope |
| --- | --- |
| [#1](https://github.com/harryneufeld/webspine/issues/1) | Built-in content engine decision |
| [#2](https://github.com/harryneufeld/webspine/issues/2) | Versioned SQLite content and atomic edits |
| [#3](https://github.com/harryneufeld/webspine/issues/3) | Local blank/demo setup and page editor |
| [#4](https://github.com/harryneufeld/webspine/issues/4) | Retained previews from saved drafts |
| [#6](https://github.com/harryneufeld/webspine/issues/6) | First owner account and protected sessions |
| [#7](https://github.com/harryneufeld/webspine/issues/7) | Scoped external-app authoring and credentials |
| [#8](https://github.com/harryneufeld/webspine/issues/8) | Shared website metadata in UI and API |
| [#12](https://github.com/harryneufeld/webspine/issues/12) | Human roles, account administration and local recovery |

These slices provide local setup, editing, metadata, preview, export, owner sessions, scoped app access and restart persistence. They do not complete M1 or M2: hosted account configuration, durable jobs and publication retain their acceptance requirements. Fixed human roles and local operator recovery are now implemented; next build durable jobs and exact-artifact publication. MCP can later adapt the same authenticated operations.

## Release objective

An operator can deploy one customer website with the built-in CMS, give an editor and an external AI client limited editing access, review a complete website candidate, publish the exact reviewed output, and restore service and data through documented procedures. A supported external CMS can replace the built-in source through a validated migration.

This is a bounded pilot release. It does not promise compatibility with arbitrary CMSs or production readiness merely because the PoC tests pass.

## Milestones

| Milestone | Exit condition | Issues |
| --- | --- | --- |
| M0 — Scope and repository | Pilot and default content model defined, repo initialized, repeatable CI established | MVP-01–03 |
| M1 — Built-in content and identity | Built-in source edits and scoped callers work end to end | MVP-04–06 |
| M2 — Reviewable releases | Complete retained releases can be built, reviewed, promoted and restored | MVP-07–10 |
| M3 — Operable pilot and replaceable CMS | Containers, recovery, external source migration and actual user/AI journeys pass acceptance | MVP-11–14 |

Do not estimate a delivery date until default CMS scope, external integration discovery and available development capacity are understood. Build the default experience first; external connector discovery can occur alongside that work.

## Proposed issues

The foundation supplies a five-page optional demo, common source contract, content validation, modular prerendered builds and a three-OS CI workflow. The first authoring slice adds the built-in engine decision and local persistent editing/preview. Pilot customer requirements, hosted identity and the wider release workflow remain open. See `../VALIDATION.md` for implemented evidence.

### MVP-15 — Carry extension contracts through every workflow

Cross-cutting M0–M3. Priority P0. Dependencies: each owning service as implemented.

Acceptance:
- Keep content sources, component registration, validation, rendering, exports, preview and deployment behind documented replaceable contracts.
- Define module identity/version, compatibility, dependencies, capability declarations and deterministic registration before adding a module loader.
- Test hook ordering, failure, cancellation, path collisions and immutable artifact identity.
- Add server-scoped AI operations across the workflow without bypassing authorization or exact-artifact publication.
- Record module versions/configuration in retained release provenance. Treat installed compiled code as trusted; do not imply a sandbox.
- Document which extension points exist and which remain planned in `extensions.md`.

### MVP-01 — Define the pilot, default CMS scope and adapter contract

Milestone M0. Priority P0. Dependencies: none.

Select the pilot website, define the small built-in content model, and identify the common capabilities required from every source. Evaluate using an existing maintained content engine versus implementing a narrow storage adapter; record the maintenance and deployment trade-off before choosing.

Acceptance:
- Record pilot pages, fields, component set, language, users and intended hosting target.
- Define page/component editing, managed images, draft history and the initial source approval policy.
- Define reads, conditional draft writes, opaque revisions, permissions, asset access and snapshot consistency in the source contract.
- If a required operation is unsupported, record an explicit scope or architecture decision before proceeding.
- Record the default implementation decision and dependency/license implications. Identify a reference external CMS candidate; its capability spike belongs to MVP-14.

### MVP-02 — Establish the GitHub repository and contribution workflow

Milestone M0. Priority P0. Dependencies: repository owner/name/visibility decision.

Acceptance:
- Use `webspine/` as the repository root and `Webspine` for C# project names. Product prose uses lowercase `webspine`.
- Inspect the proposed initial file set; exclude `.local/`, credentials, output and verification copies.
- Preserve the existing license and carry forward planning documents. Keep the separate PoC application/runtime data out of this repository.
- Add issue and pull-request templates covering outcome, acceptance criteria, dependencies and validation.
- Create milestones and issues from this plan; link their numbers here.
- Document short feature branches and reviewed pull requests. Configure supported repository controls after CI is available.

### MVP-03 — Automate the MVP build and regression checks

Milestone M0. Priority P0. Dependencies: MVP-02.

Acceptance:
- CI builds `Webspine.slnx` and runs content and management checks on Windows, Linux and macOS with the declared SDK selection. The matrix workflow is included and starts on pushes to the private `harryneufeld/webspine` repository; verify its run results for acceptance.
- Establish a meaningful test/check project with the first implemented MVP behavior. Execute console checks with `dotnet run` or test-framework suites with `dotnet test`, according to the project type.
- Reimplement relevant regression coverage for permissions, stale writes and exact-artifact publication as those behaviors are added; PoC passes do not count as MVP evidence.
- Keep job data temporary and prevent credentials/runtime files appearing in uploaded artifacts.
- A clean checkout has documented commands producing the same result locally.

### MVP-04 — Persist platform state and define durable jobs

Milestone M1. Priority P0. Dependencies: MVP-01, MVP-03.

Acceptance:
- Record database/job-storage choice and apply versioned schema migrations.
- Persist users/permissions as appropriate, integration token metadata, release records, jobs and audit. Built-in CMS content is authoritative in default mode; external mode uses its selected upstream source.
- Define idempotency, concurrent-update handling and recovery for interrupted jobs and publication.
- Demonstrate state survives restart and conflicting operations cannot silently overwrite state.
- Start with fresh MVP data. Any future import from the PoC requires an explicit, validated import procedure; preserve the original data.

### MVP-05 — Add local accounts and scoped integration credentials

Milestone M1. Priority P0. Dependencies: MVP-04.

Acceptance:
- Select maintained authentication components; implement operator bootstrap, login/logout and an operator-assisted account recovery procedure.
- Separate editor/publisher/operator capabilities and integration credentials; enforce scope on the server.
- Add token issuance, digest storage, expiry and individual revocation; exercise negative cases.
- Protect browser sessions and state-changing requests; apply login/API abuse limits and audit sensitive actions without logging secrets.
- Demo tokens do not grant access in the deployed MVP configuration.

### MVP-06 — Implement the built-in CMS behind the common adapter

Milestone M1. Priority P0. Dependencies: MVP-01, MVP-04, MVP-05.

Acceptance:
- Store and edit pilot pages, approved component fields and managed media through the portal and authenticated API.
- Implement the common adapter contract, opaque revisions, conditional writes and capability reporting.
- Retain content revision history and test concurrent edits, stale writes and forbidden fields/users.
- Define which built-in drafts are eligible for review; saving a draft never publishes it.
- Provide a portable export of content/assets and record source identity in release records.
- Keep data access inside the adapter so external mode does not depend on built-in storage internals.

### MVP-07 — Capture complete content, assets and design inputs

Milestone M2. Priority P0. Dependencies: MVP-06.

Acceptance:
- A candidate captures all pilot pages and required assets with source references, contract version and explicit design revision.
- Snapshot validation rejects invalid fields, unsafe references and missing required assets.
- Record asset retrieval limits and permitted sources; avoid unrestricted server-side URL fetching.
- Later edits or asset replacement do not mutate retained input/output.
- Verify the agreed snapshot consistency and stale-candidate policy.

### MVP-08 — Build retained releases in a separate worker

Milestone M2. Priority P0. Dependencies: MVP-04, MVP-07.

Acceptance:
- Execute builds through durable jobs with bounded retries, timeouts and visible failure states.
- Worker uses frozen inputs, has no CMS/deployment secrets, and does not execute arbitrary supplied code.
- Generate complete static output with a manifest and digests.
- Demonstrate worker restart/retry does not publish partial output or duplicate side effects.
- Failed builds leave the live site unchanged.

### MVP-09 — Provide a usable review and release portal

Milestone M2. Priority P0. Dependencies: MVP-05, MVP-08.

Acceptance:
- Show connector status, current content revision, build status and release history.
- Preview all pilot pages privately at desktop/mobile widths.
- Summarize content/design changes and identify the exact candidate under review.
- Reflect caller permissions and show understandable conflict/failure recovery actions.
- Users can complete the editor-to-publisher handoff without reading logs or copying artifact digests manually.

### MVP-10 — Promote and restore exact artifacts independently of management

Milestone M2. Priority P0. Dependencies: MVP-08; portal integration with MVP-09.

Acceptance:
- Check publisher authority, reviewed digest, expected active release and agreed candidate eligibility.
- Promote atomically without rebuilding; audit and recover interrupted promotion to a known state.
- Rollback targets an eligible retained release and preserves current CMS content.
- Verify artifact tampering and concurrent/stale publication are rejected.
- Stop the actual management process/container and verify public delivery continues.
- Confirm delivery cannot access private account, draft or credential storage.

### MVP-11 — Package and operate one customer installation

Milestone M3. Priority P0. Dependencies: MVP-05, MVP-06, MVP-08, MVP-10.

Acceptance:
- Supply container build/deployment files and an example configuration without secrets.
- Document HTTPS, domains, ports, private previews, secret injection, volume ownership and supported host assumptions.
- Add health checks, bounded logs and observable failed jobs/releases.
- Deploy from a clean environment using the guide; management is exposed only as intentionally configured.
- Record which components must run together and how to stop/start them safely.

### MVP-12 — Demonstrate backup, restore and upgrade recovery

Milestone M3. Priority P0. Dependencies: MVP-11.

Acceptance:
- Define platform database, retained artifacts, design/configuration and secret recovery requirements; distinguish separate CMS backup ownership.
- Restore to a fresh installation and verify accounts, release history, public bytes and CMS connection.
- Establish retention rules that preserve the active release and intended rollback targets.
- Exercise an upgrade/migration and its documented recovery procedure on a copy of pilot data.
- Record achievable recovery expectations for the pilot.

### MVP-13 — Publish the API contract and complete pilot acceptance

Milestone M3. Priority P0. Dependencies: MVP-06, MVP-09–12, MVP-14.

Acceptance:
- Publish an accurate OpenAPI contract and one external AI-client walkthrough with scoped credentials.
- A real editor and publisher complete a source edit, private preview, exact publication, shared-design update and rollback.
- Demonstrate built-in manual editing and native external CMS editing enter the same validated release flow.
- Verify forbidden edits/publishing, revoked credentials, stale revisions, build failure and interrupted management.
- Confirm acceptable behavior on the pilot's representative pages, assets and browsers, including keyboard use and mobile layout.
- Record results, known limitations and operator sign-off before calling the release an MVP.

### MVP-14 — Prove an external connector and migration from the built-in CMS

Milestone M3. Priority P0. Dependencies: MVP-01 for discovery; MVP-05, MVP-06 and MVP-10 for full migration verification.

Acceptance:
- Select one external CMS/version from pilot needs and prove reads, draft/conditional writes, permissions, source workflow and snapshot consistency before implementation.
- Implement explicit field/media mappings using the common adapter; test stale writes, native CMS edits and forbidden operations.
- Produce a migration preview showing unmapped fields/assets and destination conflicts; require explicit handling before final transfer.
- Rehearse export/import, short edit freeze, destination validation and source switch on a copy of pilot data.
- After switching, exactly one source accepts content edits; built-in editing for that site is disabled. Unsupported external operations are clearly reported.
- Existing public output remains usable; old candidates cannot be normally published across the source switch; retained published releases can still be restored.
- Document recovery to the original source without silently losing destination edits; do not delete original data during migration.

## GitHub organization

Use a single repository, issues and the four milestones first. Suggested labels: `type:feature`, `type:bug`, `type:discovery`, `type:docs`; `area:cms`, `area:identity`, `area:releases`, `area:portal`, `area:ops`; and priorities `P0`/`P1`.

If a project board helps, keep its states simple: Backlog, Ready, In progress, Review, Done. An issue becomes Ready when its required decisions/dependencies and acceptance criteria are clear. Split the larger issue seeds into reviewable implementation tasks after discovery; avoid one pull request per entire milestone.

## Definition of MVP complete

All milestone exit conditions and acceptance scenarios have recorded evidence; one pilot installation is reproducible and recoverable; core workflows use the built-in CMS and maintained identity system; one external connector and source migration prove replaceability; permissions/conflict/artifact checks pass; and current documentation accurately states limitations. Feature count alone is insufficient.
