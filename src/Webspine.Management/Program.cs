using Webspine.Core;
using Webspine.Demo;
using Webspine.Delivery;
using Webspine.Caching.Memory;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddSingleton<IDeliveryCache>(services =>
    builder.Configuration.GetValue("Demo:CacheEnabled", true) ? new MemoryDeliveryCache() : new NoDeliveryCache());

var app = builder.Build();

var demoEnabled = app.Configuration.GetValue<bool>("Demo:Enabled");
if (demoEnabled && !app.Environment.IsDevelopment())
{
    Console.Error.WriteLine("The example website is available only in Development. It does not initialize CMS data.");
    Environment.ExitCode = 1;
    await app.DisposeAsync();
    return;
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
    stage = "Content and extension discovery",
    demoEnabled,
    demoPath = demoEnabled ? "/demo/" : null,
    message = "Five-page example and build contracts are available. Persistent CMS, identity and publishing remain planned."
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
