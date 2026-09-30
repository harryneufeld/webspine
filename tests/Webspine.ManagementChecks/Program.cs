using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(2) };
await DeliveryChecks.RunAsync();

await using (var host = CheckHost.Start("Development", true))
{
    await host.WaitHealthyAsync(client);
    foreach (var route in new[] { "/demo", "/demo/", "/demo/services/", "/demo/products/", "/demo/about/", "/demo/contact/", "/demo/assets/site.css", "/demo/assets/studio.svg", "/demo/site-index.json" })
        await Status(host.Url + route, HttpStatusCode.OK);
    await Status(host.Url + "/demo/missing/", HttpStatusCode.NotFound);
    using var response = await client.GetAsync(host.Url + "/demo/");
    Require(response.Headers.CacheControl?.NoStore == true && response.Headers.Contains("Content-Security-Policy"), "Demo privacy headers missing.");
    using var status = JsonDocument.Parse(await client.GetStringAsync(host.Url + "/"));
    Require(status.RootElement.GetProperty("demoEnabled").GetBoolean(), "Enabled demo status incorrect.");
    using var cached = await client.GetAsync(host.Url + "/demo/");
    Require(cached.Headers.GetValues("X-Webspine-Cache").Single() == "hit" && cached.Headers.GetValues("X-Webspine-Page-Mode").Single() == "prerendered", "Demo did not use cache and delivery hook.");
    using var headRequest = new HttpRequestMessage(HttpMethod.Head, host.Url + "/demo/");
    using var head = await client.SendAsync(headRequest);
    Require(head.StatusCode == HttpStatusCode.OK && (await head.Content.ReadAsByteArrayAsync()).Length == 0, "HEAD returned a body.");
    using var conditionalRequest = new HttpRequestMessage(HttpMethod.Get, host.Url + "/demo/");
    conditionalRequest.Headers.IfNoneMatch.Add(cached.Headers.ETag!);
    using var conditional = await client.SendAsync(conditionalRequest);
    Require(conditional.StatusCode == HttpStatusCode.NotModified, "Conditional HTTP request failed.");
    Console.WriteLine("PASS: Development demo, five pages, assets, export, missing route and privacy headers.");
}

await using (var host = CheckHost.Start("Development", false))
{
    await host.WaitHealthyAsync(client);
    await Status(host.Url + "/demo/", HttpStatusCode.NotFound);
    using var status = JsonDocument.Parse(await client.GetStringAsync(host.Url + "/"));
    Require(!status.RootElement.GetProperty("demoEnabled").GetBoolean(), "Demo must be disabled by default.");
    Console.WriteLine("PASS: Demo disabled by default.");
}

await using (var host = CheckHost.Start("Development", true, cacheEnabled: false))
{
    await host.WaitHealthyAsync(client);
    using var first = await client.GetAsync(host.Url + "/demo/");
    using var second = await client.GetAsync(host.Url + "/demo/");
    Require(first.Headers.GetValues("X-Webspine-Cache").Single() == "miss" && second.Headers.GetValues("X-Webspine-Cache").Single() == "miss", "Disabled cache still returned a hit.");
    var firstBytes = await first.Content.ReadAsByteArrayAsync();
    var secondBytes = await second.Content.ReadAsByteArrayAsync();
    Require(firstBytes.SequenceEqual(secondBytes), "Disabling the cache changed output.");
    Console.WriteLine("PASS: Demo cache can be disabled through configuration without changing output.");
}

await using (var host = CheckHost.Start("Production", true))
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    await host.Process.WaitForExitAsync(timeout.Token);
    Require(host.Process.ExitCode != 0, "Production demo configuration must fail startup.");
    Require((await host.Errors).Contains("available only in Development", StringComparison.Ordinal), "Startup failed for an unexpected reason.");
    Console.WriteLine("PASS: Production demo configuration rejected.");
}

async Task Status(string url, HttpStatusCode expected)
{
    using var response = await client.GetAsync(url);
    Require(response.StatusCode == expected, $"Expected HTTP {(int)expected} for {url}, received {(int)response.StatusCode}.");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class CheckHost : IAsyncDisposable
{
    public Process Process { get; }
    public string Url { get; }
    public Task<string> Errors { get; }
    private readonly Task<string> output;

    private CheckHost(Process process, string url)
    {
        Process = process;
        Url = url;
        Errors = process.StandardError.ReadToEndAsync();
        output = process.StandardOutput.ReadToEndAsync();
    }

    public static CheckHost Start(string environment, bool enableDemo, bool cacheEnabled = true)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var url = $"http://127.0.0.1:{port}";
        var directory = AppContext.BaseDirectory;
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        // Use this check executable's runtime/dependency manifest to launch the referenced host.
        foreach (var argument in new[] { "exec", "--runtimeconfig", Path.Combine(directory, "Webspine.ManagementChecks.runtimeconfig.json"), "--depsfile", Path.Combine(directory, "Webspine.ManagementChecks.deps.json"), Path.Combine(directory, "Webspine.Management.dll"), "--urls", url, "--environment", environment, "--Demo:Enabled", enableDemo ? "true" : "false" })
            start.ArgumentList.Add(argument);
        start.ArgumentList.Add("--Demo:CacheEnabled");
        start.ArgumentList.Add(cacheEnabled ? "true" : "false");
        return new(Process.Start(start) ?? throw new InvalidOperationException("Could not start management host."), url);
    }

    public async Task WaitHealthyAsync(HttpClient client)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(20))
        {
            if (Process.HasExited) throw new InvalidOperationException($"Management exited before startup: {await Errors}");
            try
            {
                using var response = await client.GetAsync(Url + "/health");
                if (response.StatusCode == HttpStatusCode.OK) return;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            await Task.Delay(100);
        }
        throw new TimeoutException("Management did not become healthy.");
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!Process.HasExited)
            {
                Process.Kill(entireProcessTree: true);
                await Process.WaitForExitAsync();
            }
            await Task.WhenAll(output, Errors);
        }
        finally { Process.Dispose(); }
    }
}
