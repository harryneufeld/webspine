using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Webspine.Management;

internal sealed record ManagedAccount(string Id, string Name, string Role, bool Disabled, bool Owner, string Version);
internal sealed class AccountAdministration(AccountDatabase db, UserManager<IdentityUser> users)
{
    public const string OwnerClaim = "webspine:owner";
    public const string RoleClaim = "webspine:role";
    public static readonly string[] Roles = ["Editor", "Reviewer", "Operator"];
    public static string[] RolePermissions(string role) => role switch
    {
        "Operator" => Permissions.All,
        "Editor" => ["content:read", "content:write", "preview:read", "preview:build"],
        "Reviewer" => ["content:read", "preview:read"],
        _ => throw new AccountChangeException("Choose a supported role.")
    };
    public async Task UpgradeOwnerAsync()
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        if (await db.Bootstrap.AnyAsync())
        {
            var owners = await users.GetUsersForClaimAsync(new(OwnerClaim, "true"));
            if (owners.Count == 0)
            {
                var original = await users.GetUsersForClaimAsync(new(Permissions.Claim, "integrations:manage"));
                if (original.Count != 1) throw new InvalidOperationException("Cannot safely identify the original owner. Operator inspection is required.");
                await Ensure(users.AddClaimsAsync(original[0], [new(OwnerClaim, "true"), new(RoleClaim, "Owner"), new(Permissions.Claim, "accounts:manage")]));
            }
            else if (owners.Count != 1) throw new InvalidOperationException("Multiple original owners are invalid.");
        }
        // Named administrative profiles receive the new permission; stored app scopes never expand.
        foreach (var user in await users.Users.ToListAsync())
        {
            var claims = await users.GetClaimsAsync(user);
            if (claims.Any(c => c.Type == RoleClaim && c.Value is "Owner" or "Operator") &&
                !claims.Any(c => c.Type == Permissions.Claim && c.Value == "content:shared:write"))
            {
                await Ensure(users.AddClaimAsync(user, new(Permissions.Claim, "content:shared:write")));
                await Ensure(users.UpdateSecurityStampAsync(user));
            }
        }
        await transaction.CommitAsync();
    }
    public async Task<List<ManagedAccount>> ListAsync()
    {
        var rows = new List<ManagedAccount>();
        foreach (var user in await users.Users.OrderBy(u => u.UserName).ToListAsync())
        {
            var claims = await users.GetClaimsAsync(user);
            rows.Add(new(user.Id, user.UserName!, claims.FirstOrDefault(c => c.Type == RoleClaim)?.Value ?? "Unassigned", user.LockoutEnd == DateTimeOffset.MaxValue, claims.Any(c => c.Type == OwnerClaim), user.ConcurrencyStamp!));
        }
        return rows;
    }
    public async Task CreateAsync(string name, string password, string role)
    {
        var permissions = RolePermissions(role);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var user = new IdentityUser { UserName = name };
        await Ensure(users.CreateAsync(user, password));
        await Ensure(users.AddClaimsAsync(user, permissions.Select(p => new Claim(Permissions.Claim, p)).Append(new(RoleClaim, role))));
        await transaction.CommitAsync();
    }
    public async Task ChangeAsync(string actor, string id, string expectedStamp, string role, bool disabled)
    {
        var permissions = RolePermissions(role);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var user = await users.FindByIdAsync(id) ?? throw new AccountChangeException("Account does not exist.");
        if (user.ConcurrencyStamp != expectedStamp) throw new AccountChangeException("This account changed. Reload the account list before saving.");
        var claims = await users.GetClaimsAsync(user);
        if (claims.Any(c => c.Type == OwnerClaim) || id == actor) throw new AccountChangeException("The original owner and your own account cannot be changed here.");
        await Ensure(users.RemoveClaimsAsync(user, claims.Where(c => c.Type is Permissions.Claim or RoleClaim)));
        await Ensure(users.AddClaimsAsync(user, permissions.Select(p => new Claim(Permissions.Claim, p)).Append(new(RoleClaim, role))));
        await Ensure(users.SetLockoutEndDateAsync(user, disabled ? DateTimeOffset.MaxValue : null));
        await Ensure(users.ResetAccessFailedCountAsync(user));
        await Ensure(users.UpdateSecurityStampAsync(user));
        await transaction.CommitAsync();
    }
    public async Task RecoverAsync(string name, string password)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var user = await users.FindByNameAsync(name) ?? throw new AccountChangeException("Account does not exist. Recovery never creates accounts.");
        var token = await users.GeneratePasswordResetTokenAsync(user);
        await Ensure(users.ResetPasswordAsync(user, token, password));
        await Ensure(users.SetLockoutEndDateAsync(user, null));
        await Ensure(users.ResetAccessFailedCountAsync(user));
        await transaction.CommitAsync();
    }
    private static async Task Ensure(Task<IdentityResult> operation)
    {
        var result = await operation;
        if (!result.Succeeded) throw new AccountChangeException(string.Join(" ", result.Errors.Select(e => e.Description)));
    }
}
internal sealed class AccountChangeException(string message) : Exception(message);

internal static class AccountAdministrationEndpoints
{
    public static void MapAccountAdministration(this WebApplication app)
    {
        app.MapGet("/manage/users", async (AccountAdministration accounts, HttpContext context) => AccountUi.Users(context, await accounts.ListAsync()));
        app.MapPost("/manage/users", async (AccountAdministration accounts, HttpContext context) =>
        {
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            try { await accounts.CreateAsync(form["username"].ToString(), form["password"].ToString(), form["role"].ToString()); return Results.Redirect("/manage/users"); }
            catch (AccountChangeException error) { return AccountUi.Users(context, await accounts.ListAsync(), error.Message, 422); }
        });
        app.MapPost("/manage/users/{id}", async (string id, AccountAdministration accounts, HttpContext context) =>
        {
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            try { await accounts.ChangeAsync(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!, id, form["expectedStamp"].ToString(), form["role"].ToString(), form["disabled"] == "true"); return Results.Redirect("/manage/users"); }
            catch (AccountChangeException error) { return AccountUi.Users(context, await accounts.ListAsync(), error.Message, 409); }
        });
        app.MapGet("/manage/account/password", (HttpContext context) => AccountUi.Password(context));
        app.MapPost("/manage/account/password", async (UserManager<IdentityUser> users, SignInManager<IdentityUser> sessions, HttpContext context) =>
        {
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            if (form["password"] != form["confirm"]) return AccountUi.Password(context, "The new passwords do not match.", 422);
            var user = await users.GetUserAsync(context.User) ?? throw new InvalidOperationException("Account unavailable.");
            var changed = await users.ChangePasswordAsync(user, form["current"].ToString(), form["password"].ToString());
            if (!changed.Succeeded) return AccountUi.Password(context, "Password change failed. Check the current password and password requirements.", 422);
            await sessions.RefreshSignInAsync(user);
            return Results.Redirect("/manage");
        }).RequireRateLimiting("accounts");
    }
}
