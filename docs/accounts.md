# Accounts, permissions and recovery

Implemented local slice, 1 October 2026. Management still requires explicit Development configuration and loopback/localhost access.

The first operator creates the original owner account. Under **People and access**, the owner or an operator creates additional accounts with an initial password and one of these roles:

| Role | Allowed operations |
| --- | --- |
| Owner / Operator | Pages, website settings, previews, content export, connected apps and account administration |
| Editor | Create/edit pages, build/read previews and read/export content |
| Reviewer | Read/export content and inspect shared preview URLs |

Publisher permissions will be introduced with publication. Reviewer does not imply publication authority. Only operators create app credentials, which remain scoped to the issuing account's current authority. No app credential can administer accounts or create other credentials.

Share initial passwords privately. Each person can use **Change password** after signing in. This requires the current password, refreshes their current session and invalidates their other sessions and existing app credentials. No password is redisplayed in the account list.

Operators can change another person's role, disable or re-enable access. Account forms carry an expected version: stale updates are rejected rather than overwriting newer changes. Role/access changes update the Identity security stamp, so the affected user's sessions and app credentials stop working on their next request. Re-enabling allows a new sign-in but does not restore old credentials. Accounts are retained; there is no account deletion action.

The original owner cannot be disabled or demoted through management. Operators cannot change their own role/access from their current session. This preserves an administrative recovery path and avoids accidentally locking out the installation. The original owner remains recoverable through the local operator command.

## Local recovery

The installation operator with access to the data directory can restore an existing account without a browser session. Stop the management host first. From the repository root, using the same data-directory configuration as that host:

```text
dotnet run --project src/Webspine.Management -- --environment Development --Management:Enabled true --Recovery:Username owner
```

If the host uses a custom directory, also supply `--Management:DataDirectory "<absolute-data-directory>"`, replacing the placeholder with its actual path. Do not point recovery at a fresh directory. The command uses the .NET CLI on Windows, Linux and macOS and requires no PowerShell script.

Enter the new password twice. Interactive input is hidden; redirected stdin is supported for controlled automation. Never supply the password as a command-line argument, configuration value or shell-history entry. Invalid passwords, mismatches and unknown accounts fail with a nonzero exit; recovery never creates a replacement account. Successful recovery resets the password through ASP.NET Core Identity, clears login lockout and invalidates previous sessions and app credentials. It does not listen on an HTTP port and does not reset content, accounts or previews. Restart the host and sign in with the new password; recreate needed app credentials.

Protect the data directory and Data Protection keys: filesystem access is operator authority. This is an operator-assisted procedure, not an emailed/public password-reset service. No reset tokens are sent to anyone or returned through the API.

## Existing installations

The previous one-owner installation stored owner permissions as claims. Startup upgrades that existing account by adding the original-owner marker, Owner role and account-management permission in a transaction, without changing the schema, password, content or retained artifacts. The upgrade is idempotent. If no owner marker exists and the legacy owner cannot be identified uniquely, startup fails and requires operator inspection rather than selecting an arbitrary user.

Roles are fixed application permission profiles stored as Identity claims. Arbitrary role editing, invitation/email delivery, MFA, SSO, per-page grants, durable account audit and hosted deployment are not implemented. The existing Identity database still needs explicit schema migrations when its schema changes; this claim upgrade is not a substitute for them.
