using System.Collections.Immutable;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Components;
using Webspine.Core;
using Webspine.Core.Composition;

namespace Webspine.Rendering.Razor;

public static class ExecutableCapture
{
    // One immutable executable snapshot per process. Package installation/hot-loading is not supported.
    private static readonly Lazy<Task<ExecutableEnvironmentSnapshot>> Snapshot = new(CaptureInstalledAsync);
    public static async ValueTask<ExecutableEnvironmentSnapshot> ReadAsync(CancellationToken ct) =>
        await Snapshot.Value.WaitAsync(ct);

    private static async Task<ExecutableEnvironmentSnapshot> CaptureInstalledAsync()
    {
        var runtimeRoot = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var dotnetRoot = Directory.GetParent(runtimeRoot)!.Parent!.Parent!.FullName;
        var hostFxr = Path.Combine(dotnetRoot, "host", "fxr", Path.GetFileName(runtimeRoot));
        if (!Directory.Exists(hostFxr)) throw new ContentValidationException("The matching .NET host runtime must be installed for executable capture.");
        var roots = new[]
        {
            (Name: "application", Path: AppContext.BaseDirectory),
            (Name: "dotnet", Path: runtimeRoot),
            (Name: "aspnet", Path: Path.GetDirectoryName(typeof(ComponentBase).Assembly.Location)!),
            (Name: "host-fxr", Path: hostFxr)
        };
        var inventory = new SortedDictionary<string, string>(StringComparer.Ordinal);
        inventory.Add("host/" + (OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"),
            Path.Combine(dotnetRoot, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
        foreach (var root in roots)
        {
            var paths = root.Name == "application"
                ? Directory.EnumerateFiles(root.Path).Where(IsExecutable).Concat(
                    Directory.Exists(Path.Combine(root.Path, "runtimes"))
                        ? Directory.EnumerateFiles(Path.Combine(root.Path, "runtimes"), "*", SearchOption.AllDirectories).Where(IsExecutable) : [])
                : Directory.EnumerateFiles(root.Path, "*", SearchOption.AllDirectories);
            foreach (var file in paths)
            {
                var relative = Path.GetRelativePath(root.Path, file).Replace('\\', '/');
                if (relative.StartsWith("../", StringComparison.Ordinal) || File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint))
                    throw new ContentValidationException("Executable capture does not follow links outside installed runtime files.");
                inventory.Add(root.Name + "/" + relative, file);
            }
        }
        using var archiveBytes = new MemoryStream();
        var files = ImmutableArray.CreateBuilder<ExecutableFile>();
        using (var archive = new ZipArchive(archiveBytes, ZipArchiveMode.Create, true))
            foreach (var (path, file) in inventory)
            {
                var bytes = await File.ReadAllBytesAsync(file);
                var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
                entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                await using var output = entry.Open();
                await output.WriteAsync(bytes);
                files.Add(new(path, BuildPipeline.Hash(bytes), bytes.LongLength));
            }
        var captured = ImmutableArray.Create(archiveBytes.ToArray());
        return new(RuntimeInformation.FrameworkDescription, RuntimeInformation.RuntimeIdentifier, RuntimeInformation.OSDescription,
            files.ToImmutable(), captured, BuildPipeline.Hash(captured.AsSpan()));
    }

    private static bool IsExecutable(string file) => Path.GetExtension(file) is ".dll" or ".exe" or ".so" or ".dylib" ||
        file.EndsWith(".deps.json", StringComparison.Ordinal) || file.EndsWith(".runtimeconfig.json", StringComparison.Ordinal);
}
