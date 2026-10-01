# 0012 — Typed composition editing and shared authority

Status: accepted, 1 October 2026. Implements #16 following decisions 0009–0011.

The board and external clients must arrange content without replacing an arbitrary graph, bypassing design constraints or accidentally changing other pages. The selected CMS remains authoritative.

Core provides typed create/update/move/group/reference/promote/detach/delete and page/settings commands. A provider-independent application editor checks the revision, exact adapter capability, caller authority and shared-impact acknowledgement before validating and committing a complete proposed draft through the trusted persistence seam. The API exposes commands, never that persistence seam. Ownership remains explicit; ordinary moves preserve it.

Introduce `content:shared:write` independently of page editing. Owner/Operator profiles receive it; other profiles and existing app scopes do not. Upgrading an administrative profile updates its security stamp, conservatively invalidating its previous sessions and credentials. Shared mutations require an exact affected-page acknowledgement tied to the expected revision. The board displays this before saving; the API exposes an impact query and checks the acknowledgement server-side.

The first upload policy accepts a deliberately small PNG subset using bounded parsing and the maintained .NET compression implementation. Validate complete compressed pixel data and chunk CRCs; strip ancillary metadata, use content-addressed immutable paths and capture bytes consistently. No user-supplied filesystem paths, arbitrary remote fetches or uploaded executable formats are supported. Other formats need explicit validation policies.

Keep v1 installations unchanged until explicit migration. Once migrated, route humans to the composition board and refuse v1 writes. Previews retain their private path prefix and final bytes. Patterns/page blueprints, external provider configuration/conformance and publication stay with their owning later issues. See [composition editing](../composition-editing.md) for the implemented workflow and limits.
