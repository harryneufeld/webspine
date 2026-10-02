# Versioned content Records and bounded Pattern inputs

Accepted, 2 October 2026; implements the bounded proof in #17 and the direction in decision 0009.

## Decision

Add typed, presentation-independent Records to the active v2 CMS snapshot. Register schemas with explicit versions and strict semantic/field validation, separately from Block rendering. Use the existing conditional site transaction plus individual Record revisions. Require shared-content authority and exact affected-page review for used Record edits; deletion is allowed only after references are removed.

Represent a Pattern instance as a typed leaf Block whose package registration declares Record inputs and override slots. The package's Razor component owns its fixed structure. This reuses existing ownership, placement, capability and editor operations without inventing a second page tree. Require schema-compatible references and forbid arbitrary descendants/fields. Studio supplies Product@1 and product-card@1; Fieldwork stays independent.

Freeze schema/Pattern definitions and defaults, Record values/revisions, media and executable provenance with each candidate. Fail mandatory validation for missing or incompatible data. Deliberately selected compatible package changes affect new candidates only. Breaking schema/input versions require an explicitly registered, authorized migration that validates and commits the complete result atomically. The trusted host migration seam has no arbitrary HTTP field-mapping endpoint.

## Alternatives and consequences

Copying Product values into each card would lose single-content reuse and independent references. Making every card a Shared Block would conflate reusable design with reusable instance content. Mutable latest schemas or runtime Record lookups would undermine capture identity. A user-defined schema/Pattern designer and ERP connector would exceed the bounded informational-site proof. These alternatives are excluded.

Older native v2 snapshots receive an empty Record collection when read; stored revisions/artifacts remain untouched. Source adapters must explicitly declare Record schemas and operations. External business providers remain planned and must separately declare identity, permissions, mutations and consistent capture evidence; they cannot become another CMS. See [patterns-records.md](../patterns-records.md) for operations, limits, migration deployment requirements and verification.
