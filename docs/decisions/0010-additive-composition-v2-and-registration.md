# Additive composition v2 and registration

Legacy authoring/migration policy superseded by [decision 0017](0017-v2-only-management-authoring.md), 2 October 2026. This record describes the decision at its original implementation; native v2 is now the sole authoring workflow.


Selected 1 October 2026 for #14, implementing the first stage of decision 0009.

## Decision

Add `Webspine.Core.Composition` alongside existing v1 types. Explicit contract version 2 travels with the new snapshot. Do not change `ContentContract.Version`, SQLite histories, existing build semantics or `/api/v1`. Reserve future composition HTTP operations for `/api/v2`; no endpoints or storage migrations are introduced now.

Use immutable objects plus ordered placements, explicit owner/target kinds and separate shared definitions. Validate the graph independently of registered payload validators: owning-placement counts, unique IDs, references, cycles including disconnected/shared graphs, placement rules, and expanded depth/count bounds. Iterative cycle detection avoids recursion over untrusted graph size; expansion is bounded before rendering.

Register typed schemas, validators and renderers together using `BlockRegistration<T>`. Strict camelCase JSON and required members form the payload shape; validator code supplies bounds/reference rules. Keep one type version per registry, deterministic ordering and explicit module/schema/renderer identity. Payload changes require explicit version/migration decisions. This permits extension without a schema designer, executable CMS templates or module loader.

The opt-in build path accepts frozen composition/design/assets, captures CSS and registration provenance, and seals all bytes through the common artifact builder. Source builds require consistent-capture/type capabilities and reject source/design substitution. Capabilities require adapter conformance; they are not authorization. Compiled code is trusted. Assembly fingerprints cover payload/delegate implementations; transitive dependency packaging is later module work.

## Alternatives and consequences

Changing v1 in place would reinterpret drafts before migration exists. Closed type switches require engine changes for extensions. Both are rejected. Two temporary contracts require explicit migration/legacy reading in #15 and common editing operations in #16; this does not introduce two editable CMSs.

Storage substitution and optional business-data providers remain separate. No extra database or Record provider is added here.

## Evidence

Checks load published examples, build shared/nested content, register an independent Quote type and capture through an independent test source. They reject cycles/ownership errors, depth/count overflow, unsupported versions/operations/capture, unsafe links/paths and ambiguous fields; they verify escaping, deterministic registration, digests and immutable prior output. Existing management/restart/account checks remain the v1 regression suite. See VALIDATION.md and PR checks for OS results.
