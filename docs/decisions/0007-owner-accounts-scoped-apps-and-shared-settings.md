# 0007: Owner accounts, scoped apps and shared website settings

Accepted first implementation slice, 1 October 2026.

Use maintained ASP.NET Core Identity with its EF Core SQLite store for owner password hashing, lockout and cookie sessions. The existing content database remains owned by its source adapter; a separate `accounts.db` stores identity, one-time owner bootstrap and integration credential metadata. The first identity schema is created in a fresh database through EF Core EnsureCreated; subsequent identity schema changes require explicit upgrade migrations before deployment. There is no automatic reset or recreation of customer data.

Bootstrap uses a transaction and a singleton record so an existing owner cannot be replaced. Owner permissions are Identity claims. Browser sessions are HTTP-only, SameSite Strict, nonpersistent, limited to eight hours, with security-stamp validation on every request. Forms keep antiforgery validation. Login/owner-creation endpoints have a per-IP request limit and Identity locks an account after five failed logins. Recovery, additional users/roles and production hosting remain future requirements; these controls do not remove the current Development/loopback restrictions.

Give each app a random 256-bit credential with its own label, granted scopes, owner, security stamp, expiry and revocation flag. Store a SHA-256 digest rather than the raw secret. The secret is displayed once. Check credentials and the owner's current permissions/stamp/lockout on every API request. Keep Bearer authentication separate from browser sessions; credentials cannot manage other credentials. No OAuth provider or embedded AI service is introduced.

Portal and API use the same `AuthoringOperations` and `IWebsiteAuthoringSource`. Add page, edit page and edit shared website metadata use mandatory validation and atomic source revisions. Shared name/title and language remain content values, reused by rendering and management; do not introduce a second editable branding copy. Existing previews keep their captured settings. Website settings require a separate `settings:write` permission from `content:write`.

The HTTP API is the integration foundation; an eventual MCP adapter must call these same operations with the same permissions. Operation logs identify account and credential IDs without secrets. Durable audit, per-page scopes, full component/media authoring, source switching, protected hosted previews and publication remain explicit follow-up work.

References: [Identity package](https://www.nuget.org/packages/Microsoft.AspNetCore.Identity.EntityFrameworkCore/10.0.12), [Identity configuration](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-configuration), [ASP.NET Core antiforgery](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery). The selected Microsoft Identity and EF Core packages retain MIT licenses and run under the existing three-OS CI matrix.
