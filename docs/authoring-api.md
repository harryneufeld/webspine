# External-app authoring API

Implemented first slice, 1 October 2026. This API is opt-in with local management, Development-only and loopback/localhost-restricted. It is not yet a hosted integration service or MCP server.

The owner or an operator creates a named credential under `/manage/integrations`, selects permissions and an expiry of 1–30 days, then copies the secret shown once. Keep it in the app's secret storage. Send it only in `Authorization: Bearer <credential>`; query-string tokens are not accepted. Browser cookies cannot authenticate this API, and API credentials cannot sign in to management. Credentials store only a SHA-256 digest of a cryptographically random secret. Individual revocation takes effect on the next request; expiry, owner lockout and changes to the owner's Identity security stamp also deny access. App credentials cannot issue more credentials.

## Operations

| Method and path | Required scope | Body / result |
| --- | --- | --- |
| GET `/api/v1/site` | `content:read` | Complete content snapshot, source identity and opaque `revision` |
| PUT `/api/v1/site` | `settings:write` | `expectedRevision`, `title`, `language`; returns saved snapshot |
| POST `/api/v1/pages` | `content:write` | `expectedRevision`, `title`, `path`, `description`; returns saved snapshot including new page ID |
| PUT `/api/v1/pages/{id}` | `content:write` | `expectedRevision`, `title`, `description`, `fields`; returns saved snapshot |
| POST `/api/v1/previews` | `preview:build` | `expectedRevision`; returns 201 with preview `id`, `digest`, `sourceRevision`, `url` |
| GET/HEAD `/api/v1/previews/{id}/{path}` | `preview:read` | Retained HTML/assets through the delivery pipeline |

Use `Content-Type: application/json` on writes. A page's description is currently its visible headline and description metadata. Website title is shared branding, browser title suffix and footer; language is a tag such as `en`, `de` or `en-GB`, not automatic translation. Changing settings requires its own scope: page-writing authority does not imply settings-writing authority.

Read the latest snapshot before editing. Every write uses its exact opaque revision; stale writes return 409 and do not alter history or content. After a conflict, read again and reconcile your proposed changes. All writes pass the same mandatory content validation and source transaction as the editor. Unknown JSON properties return 400; invalid content returns 422. An uninitialized site returns 409. Invalid/revoked/expired credentials return 401; missing permissions return 403. Content snapshots and previews use `Cache-Control: no-store`. Do not log request Authorization headers or credential issuance responses.

Example website-settings body:

```json
{
  "expectedRevision": "revision-from-the-latest-snapshot",
  "title": "My business",
  "language": "en"
}
```

Example page edit body, using a section ID from the snapshot:

```json
{
  "expectedRevision": "revision-from-the-latest-snapshot",
  "title": "Home",
  "description": "Welcome to my business",
  "fields": {
    "introduction.heading": "A fresh start",
    "introduction.text": "Our updated introduction."
  }
}
```

Approved `fields` keys start with `{sectionId}.`. Text accepts `heading`, `text`; Image accepts `alternativeText`; CTA accepts `heading`, `text`, `label`, `destination`; Cards accepts `heading` and `items.{zeroBasedIndex}.title`, `.description`, `.destination`. Omitted fields retain existing values. The page title and description are supplied in every page edit. Core owns this approved mapping, shared by UI and source validation. New pages start with a text section named `introduction`. Only canonical unique routes, approved links and existing media references are accepted.

Preview links and assets retain the API base URL of that build; the app must send its credential on each request. A plain browser URL does not carry a Bearer header. Human review uses previews built through management. Both build paths use the same operations and rendering pipeline, with captured input/output preserved after later edits.

## Boundaries and remaining work

`IWebsiteAuthoringSource` extends the common content contract with conditional metadata, page-creation and page-edit operations. `AuthoringOperations` is shared by portal and API. The first source is SQLite; external adapters and source switching remain unimplemented. Preview capture/storage still use the built-in SQLite implementation and will need their own replaceable contracts alongside release work.

Human accounts use fixed role profiles, and app scopes are installation-wide rather than restricted to individual pages. The v1 API does not support page deletion, route changes, adding/reordering sections, uploads or design/code changes; composition editing and PNG uploads are available in the separate [v2 API](composition-editing.md). Neither API supports design/code changes, token refresh, OAuth, MCP, OpenAPI generation, durable audit storage, email/invitation recovery, publication or rollback. Successful API writes produce attributable structured log entries containing account/credential IDs, without secrets; they are not a durable audit trail. Extend these operations and permissions as their owning workflows are implemented.
