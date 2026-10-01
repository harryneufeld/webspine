# 0008: Human permissions and local account recovery

Accepted local implementation, 1 October 2026. Extends decision 0007 while retaining Development/loopback restrictions.

Use fixed Owner, Operator, Editor and Reviewer permission profiles through the existing Identity claims and server checks. Editor writes pages and builds previews; Reviewer reads content and previews; Operator manages settings, apps and people. Defer Publisher until exact-artifact publication exists. UI visibility reflects permissions but does not replace server authorization.

Preserve the original owner with an immutable claim marker. Account administration cannot demote/disable that owner or the caller's own account. Other changes are transactional and compare an expected concurrency stamp. Update the affected account's security stamp to invalidate sessions and app credentials. Retain disabled accounts rather than adding deletion now.

Use maintained Identity ChangePassword/ResetPassword and Data Protection token providers. Signed-in users need their current password to change it. Operator recovery runs before HTTP startup, reads hidden console input or stdin, clears lockout and invalidates old access. It requires filesystem authority and never accepts a password command-line argument. Do not introduce a public recovery endpoint or email dependencies in this local slice.

No Identity schema change is needed: profiles, owner markers and permissions use existing claim tables. Upgrade the previous single owner transactionally and idempotently; ambiguous identification fails closed. Future schema changes still need explicit migrations. Website content stays inside its existing adapter and is never reset by account operations.

Verification covers role denials, conditional account changes, owner protection, password/session behavior, disabled login, credential invalidation, claim upgrade and the actual recovery subprocess preserving content/previews. References: [Identity configuration](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-configuration), [Identity token-provider source](https://source.dot.net/Microsoft.AspNetCore.Identity/IdentityBuilderExtensions.cs.html).
