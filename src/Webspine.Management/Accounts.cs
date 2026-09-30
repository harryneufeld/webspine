using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Webspine.Management;

internal static class Permissions
{
    public const string Claim = "webspine:permission";
    public static readonly string[] All = ["content:read", "content:write", "settings:write", "preview:read", "preview:build", "integrations:manage"];
    public static bool Has(ClaimsPrincipal user, string permission) => user.HasClaim(Claim, permission);
    public static string Label(string permission) => permission switch
    {
        "content:read" => "Read pages and website settings", "content:write" => "Create and edit pages",
        "settings:write" => "Change shared website settings", "preview:read" => "View retained previews",
        "preview:build" => "Build previews", _ => permission
    };
}

internal sealed class AccountDatabase(DbContextOptions<AccountDatabase> options) : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<OwnerBootstrap> Bootstrap => Set<OwnerBootstrap>();
    public DbSet<IntegrationCredential> Credentials => Set<IntegrationCredential>();
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<OwnerBootstrap>().HasKey(x => x.Id);
        builder.Entity<IntegrationCredential>().HasKey(x => x.Id);
        builder.Entity<IntegrationCredential>().HasIndex(x => x.Digest).IsUnique();
        builder.Entity<IntegrationCredential>().HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
internal sealed class OwnerBootstrap { public int Id { get; set; } }
internal sealed class IntegrationCredential
{
    public string Id { get; set; } = "";
    public string Digest { get; set; } = "";
    public string Label { get; set; } = "";
    public string UserId { get; set; } = "";
    public string SecurityStamp { get; set; } = "";
    public string Scopes { get; set; } = "";
    public long ExpiresUtcTicks { get; set; }
    public bool Revoked { get; set; }
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

// Integration authentication never falls back to browser cookies.
internal sealed class IntegrationAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, AccountDatabase database, UserManager<IdentityUser> users)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Integration";
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();
        var token = header[7..];
        if (token.Length != 68 || !token.StartsWith("wsp_", StringComparison.Ordinal)) return AuthenticateResult.Fail("Invalid credential.");
        var digest = IntegrationCredential.Hash(token);
        var credential = await database.Credentials.AsNoTracking().SingleOrDefaultAsync(c => c.Digest == digest, Context.RequestAborted);
        if (credential is null || credential.Revoked || credential.ExpiresUtcTicks <= DateTimeOffset.UtcNow.UtcTicks)
            return AuthenticateResult.Fail("Expired or revoked credential.");
        var user = await users.FindByIdAsync(credential.UserId);
        if (user is null || user.SecurityStamp != credential.SecurityStamp || await users.IsLockedOutAsync(user)) return AuthenticateResult.Fail("Account unavailable.");
        var current = (await users.GetClaimsAsync(user)).Where(c => c.Type == Permissions.Claim).Select(c => c.Value).ToHashSet(StringComparer.Ordinal);
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user.Id), new(ClaimTypes.Name, credential.Label), new("webspine:credential", credential.Id) };
        claims.AddRange(credential.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(current.Contains).Select(scope => new Claim(Permissions.Claim, scope)));
        return AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName));
    }
}

internal static class AccountEndpoints
{
    public static async Task InitializeAccountsAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AccountDatabase>();
        // Separate fresh identity database. Upgrade migrations are required before altering this schema.
        await database.Database.EnsureCreatedAsync();
    }

    public static void MapAccounts(this WebApplication app)
    {
        app.MapGet("/manage/account/start", async (AccountDatabase db, HttpContext context) =>
            await db.Bootstrap.AnyAsync(context.RequestAborted) ? Results.Redirect("/manage/account/login") : ManagementUi.AccountForm(context, true));
        app.MapPost("/manage/account/start", async (AccountDatabase db, UserManager<IdentityUser> users, SignInManager<IdentityUser> sessions, HttpContext context) =>
        {
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            await using var transaction = await db.Database.BeginTransactionAsync(context.RequestAborted);
            if (await db.Bootstrap.AnyAsync(context.RequestAborted)) return ManagementUi.Problem("The owner account already exists. Sign in instead.", 409);
            var user = new IdentityUser { UserName = form["username"].ToString() };
            var created = await users.CreateAsync(user, form["password"].ToString());
            if (!created.Succeeded) return ManagementUi.AccountForm(context, true, string.Join(" ", created.Errors.Select(e => e.Description)), 422);
            var granted = await users.AddClaimsAsync(user, Permissions.All.Select(p => new Claim(Permissions.Claim, p)));
            if (!granted.Succeeded) throw new InvalidOperationException("Could not grant owner permissions.");
            db.Bootstrap.Add(new() { Id = 1 });
            await db.SaveChangesAsync(context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);
            await sessions.SignInAsync(user, isPersistent: false);
            return Results.Redirect("/manage");
        }).RequireRateLimiting("accounts");
        app.MapGet("/manage/account/login", async (AccountDatabase db, HttpContext context) =>
            await db.Bootstrap.AnyAsync(context.RequestAborted) ? ManagementUi.AccountForm(context, false) : Results.Redirect("/manage/account/start"));
        app.MapPost("/manage/account/login", async (SignInManager<IdentityUser> sessions, HttpContext context) =>
        {
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            var result = await sessions.PasswordSignInAsync(form["username"].ToString(), form["password"].ToString(), isPersistent: false, lockoutOnFailure: true);
            return result.Succeeded ? Results.Redirect("/manage") : ManagementUi.AccountForm(context, false, "Sign-in failed. Check your details or try again later.", 401);
        }).RequireRateLimiting("accounts");
        app.MapPost("/manage/account/logout", async (SignInManager<IdentityUser> sessions) => { await sessions.SignOutAsync(); return Results.Redirect("/manage/account/login"); });
        app.MapGet("/manage/integrations", async (AccountDatabase db, HttpContext context) => ManagementUi.Integrations(context,
            await db.Credentials.AsNoTracking().Where(c => c.UserId == context.User.FindFirstValue(ClaimTypes.NameIdentifier)).OrderBy(c => c.Label).ToListAsync(context.RequestAborted)));
        app.MapPost("/manage/integrations", async (AccountDatabase db, UserManager<IdentityUser> users, HttpContext context) =>
        {
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            var scopes = form["scope"].Distinct(StringComparer.Ordinal).ToArray();
            var label = form["label"].ToString();
            if (string.IsNullOrWhiteSpace(label) || label.Length > 80 || scopes.Length == 0 || scopes.Any(s => s is null || s == "integrations:manage" || !Permissions.Has(context.User, s)) || !int.TryParse(form["days"], out var days) || days is < 1 or > 30)
                return ManagementUi.Problem("Choose a name, permitted scopes and an expiry of 1–30 days.", 422);
            var user = await users.GetUserAsync(context.User) ?? throw new InvalidOperationException("Account unavailable.");
            var raw = "wsp_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            db.Credentials.Add(new() { Id = Guid.NewGuid().ToString("N"), Digest = IntegrationCredential.Hash(raw), Label = label, UserId = user.Id, SecurityStamp = user.SecurityStamp!, Scopes = string.Join(' ', scopes), ExpiresUtcTicks = DateTimeOffset.UtcNow.AddDays(days).UtcTicks });
            await db.SaveChangesAsync(context.RequestAborted);
            return ManagementUi.Integrations(context, await db.Credentials.AsNoTracking().Where(c => c.UserId == user.Id).ToListAsync(context.RequestAborted), raw);
        });
        app.MapPost("/manage/integrations/{id}/revoke", async (string id, AccountDatabase db, HttpContext context) =>
        {
            var credential = await db.Credentials.SingleOrDefaultAsync(c => c.Id == id && c.UserId == context.User.FindFirstValue(ClaimTypes.NameIdentifier), context.RequestAborted);
            if (credential is null) return Results.NotFound();
            credential.Revoked = true; await db.SaveChangesAsync(context.RequestAborted); return Results.Redirect("/manage/integrations");
        });
    }
}
