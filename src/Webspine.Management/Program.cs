using Webspine.Core;
using Webspine.Demo;
using Webspine.Delivery;
using Webspine.Caching.Memory;
using Webspine.Management;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddHealthChecks();
builder.Services.AddAntiforgery(options => options.Cookie.SameSite = SameSiteMode.Strict);
if (builder.Configuration.GetValue<bool>("Management:Enabled") && builder.Environment.IsDevelopment())
{
    var dataDirectory = builder.Configuration["Management:DataDirectory"] ?? Path.Combine(builder.Environment.ContentRootPath, ".local");
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys"))).SetApplicationName("webspine-local");
}
builder.Services.AddSingleton<IDeliveryCache>(services =>
    builder.Configuration.GetValue("Demo:CacheEnabled", true) ? new MemoryDeliveryCache() : new NoDeliveryCache());

var app = builder.Build();

var demoEnabled = app.Configuration.GetValue<bool>("Demo:Enabled");
var managementEnabled = app.Configuration.GetValue<bool>("Management:Enabled");
if ((demoEnabled || managementEnabled) && !app.Environment.IsDevelopment())
{
    Console.Error.WriteLine("The example website and local management are available only in Development.");
    Environment.ExitCode = 1;
    await app.DisposeAsync();
    return;
}

if (managementEnabled) await app.MapManagementAsync();

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
