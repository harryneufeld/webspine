using Webspine.Core;
using Webspine.Demo;
using Webspine.Delivery;
using Webspine.Caching.Memory;
using Webspine.Management;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using System.Text.Json.Serialization;
using Webspine.Content.Sqlite;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddHealthChecks();
builder.Services.AddAntiforgery(options => options.Cookie.SameSite = SameSiteMode.Strict);
if (builder.Configuration.GetValue<bool>("Management:Enabled") && builder.Environment.IsDevelopment())
{
    var dataDirectory = builder.Configuration["Management:DataDirectory"] ?? Path.Combine(builder.Environment.ContentRootPath, ".local");
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys"))).SetApplicationName("webspine-local");
    Directory.CreateDirectory(dataDirectory);
    builder.Services.AddDbContext<AccountDatabase>(options => options.UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = Path.Combine(Path.GetFullPath(dataDirectory), "accounts.db"), Pooling = false }.ToString()));
    builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 12;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    }).AddEntityFrameworkStores<AccountDatabase>().AddDefaultTokenProviders();
    builder.Services.AddScoped<AccountAdministration>();
    builder.Services.ConfigureApplicationCookie(options =>
    {
        options.Cookie.Name = "webspine.session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = false;
    });
    builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);
    builder.Services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, IntegrationAuthentication>(IntegrationAuthentication.SchemeName, _ => { });
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = 429;
        options.AddPolicy("accounts", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 12, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    });
    builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
    builder.Services.AddSingleton(new SqliteContentSource(Path.Combine(dataDirectory, "webspine.db")));
    builder.Services.AddSingleton<IWebsiteAuthoringSource>(services => services.GetRequiredService<SqliteContentSource>());
    builder.Services.AddSingleton<AuthoringOperations>();
}
builder.Services.AddSingleton<IDeliveryCache>(services =>
    builder.Configuration.GetValue("Demo:CacheEnabled", true) ? new MemoryDeliveryCache() : new NoDeliveryCache());

var app = builder.Build();

var demoEnabled = app.Configuration.GetValue<bool>("Demo:Enabled");
var managementEnabled = app.Configuration.GetValue<bool>("Management:Enabled");
if (app.Configuration["Recovery:Username"] is not null && !managementEnabled)
{
    Console.Error.WriteLine("Account recovery requires explicit local management configuration.");
    Environment.ExitCode = 1;
    await app.DisposeAsync();
    return;
}
if ((demoEnabled || managementEnabled) && !app.Environment.IsDevelopment())
{
    Console.Error.WriteLine("The example website and local management are available only in Development.");
    Environment.ExitCode = 1;
    await app.DisposeAsync();
    return;
}

if (managementEnabled)
{
    app.UseAuthentication();
    app.UseRateLimiter();
    await app.InitializeAccountsAsync();
    if (app.Configuration["Recovery:Username"] is { } recoveryUsername)
    {
        using var scope = app.Services.CreateScope();
        Environment.ExitCode = await AccountRecovery.RunAsync(scope.ServiceProvider.GetRequiredService<AccountAdministration>(), recoveryUsername);
        await app.DisposeAsync();
        return;
    }
    await app.MapManagementAsync();
    app.MapAccounts();
    app.MapAccountAdministration();
    app.MapAuthoringApi();
}

if (demoEnabled)
{
    var artifact = await DemoSite.BuildAsync(DemoSite.FixtureDirectory, "/demo");
    var delivery = new PrerenderedDelivery(new FixedArtifactSource(artifact), app.Services.GetRequiredService<IDeliveryCache>(),
        [new DemoDeliveryHeaders()], diagnostics: true);
    app.MapMethods("/demo/{**path}", ["GET", "HEAD"], async (string? path, HttpContext context) =>
    {
        if (context.Connection.RemoteIpAddress is not { } remote || !System.Net.IPAddress.IsLoopback(remote))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        await delivery.DeliverAsync(context, path);
    });
}

app.MapGet("/", () => Results.Ok(new
{
    name = "webspine",
    stage = "Local authoring workflow",
    demoEnabled,
    demoPath = demoEnabled ? "/demo/" : null,
    managementEnabled,
    managementPath = managementEnabled ? "/manage" : null,
    message = "Local setup, persistent content and retained previews are available. Production accounts and publishing remain planned."
}));

app.MapHealthChecks("/health");

app.Run();

sealed class DemoDeliveryHeaders : IDeliveryHeaders
{
    public string Id => "demo-page-mode";
    public ValueTask<System.Collections.Immutable.ImmutableDictionary<string, string>> GetAsync(DeliveryRepresentation representation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(System.Collections.Immutable.ImmutableDictionary<string, string>.Empty.Add("X-Webspine-Page-Mode", "prerendered"));
    }
}
