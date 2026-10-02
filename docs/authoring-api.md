# External-app authoring API

Updated 2 October 2026. Composition **v2** is the sole authoring API. This is opt-in with local management, Development-only and loopback/localhost-restricted; hosted integration and MCP remain planned.

An owner/operator creates a named credential under **Connected apps**, selects scopes and expiry (1â€“30 days), and copies the secret shown once. Keep it in the app's secret storage. Send `Authorization: Bearer <credential>`; query-string tokens and browser cookies cannot authenticate API requests. API credentials cannot sign in to management or issue more credentials. Only SHA-256 credential digests are stored. Revocation, expiry, disabled accounts, lockout and changed account security stamps deny access on subsequent requests.

Use `GET /api/v2/site` for the current composition and opaque revision, `GET /api/v2/schema` for registered field metadata/defaults/choices, and `POST /api/v2/changes` for typed conditional edits. Shared writes need separate scope and exact affected-page acknowledgement; website settings have their own scope. New page creation uses the selected design's areas and registered defaults and does not edit shared navigation. [Composition editing](composition-editing.md) documents complete requests, scopes, commands, uploads, preview routes and error responses.

Preview creation uses `POST /api/v2/previews` and returns an authenticated `/api/v2/previews/{id}/` URL. Send the credential on every page/asset request. Retained output stays unchanged after later edits or restart. Responses use `Cache-Control: no-store`; avoid logging Authorization headers or credential issuance responses.

The old `/api/v1/site`, page writes and preview creation are retired and return **410 Gone** after authentication and content-read checks. They never write, parse legacy commands or convert content. Invalid credentials still return 401 and insufficient scope returns 403. Existing `GET/HEAD /api/v1/previews/{id}/{path}` URLs are retained artifact retrieval only, require `preview:read`, and continue returning exact stored bytes. Legacy v1 authoring workspaces require [fresh v2 setup](management.md#older-workspaces), without a migration requirement.

## Patterns and content Records

[Patterns and Records](patterns-records.md) document the Studio Product/card workflow, registered schema/input versions, source capabilities, board/API conditional operations, affected-page authority, frozen values/media and explicit breaking-version migrations. This is implemented for native v2; external business providers remain planned.
