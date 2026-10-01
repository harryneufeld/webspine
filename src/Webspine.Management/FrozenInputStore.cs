using System.IO.Compression;
using System.Text.Json;
using Webspine.Core;
using Webspine.Core.Composition;

namespace Webspine.Management;

// Private, content-addressed executable bundles and per-candidate inputs. Never served by HTTP.
internal sealed class FrozenInputStore(string directory)
{
    public async Task SaveAsync(string id, CapturedComposition content, FrozenDesignPackage design, CancellationToken ct)
        => await SaveContentAsync(id, content.Content, content.AssetFiles, design, ct);
    public async Task SaveLegacyAsync(string id, ContentSnapshot content,
        System.Collections.Immutable.ImmutableDictionary<string, System.Collections.Immutable.ImmutableArray<byte>> assets,
        FrozenDesignPackage design, CancellationToken ct) => await SaveContentAsync(id, content, assets, design, ct);
    private async Task SaveContentAsync(string id, object content,
        System.Collections.Immutable.ImmutableDictionary<string, System.Collections.Immutable.ImmutableArray<byte>> assets,
        FrozenDesignPackage design, CancellationToken ct)
    {
        await SaveBundleAsync(design, ct);
        Directory.CreateDirectory(directory);
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        {
            async Task Add(string name, byte[] value)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
                await using var stream = entry.Open(); await stream.WriteAsync(value, ct);
            }
            await Add("content.json", JsonSerializer.SerializeToUtf8Bytes(content, content.GetType(), CompositionJson.Options));
            await Add("design.json", JsonSerializer.SerializeToUtf8Bytes(new
            {
                design.Descriptor, design.Design, design.Components, design.ContentTypes, design.Digest,
                executableDigest = design.Executable.Digest, design.Executable.Files,
                design.Executable.Runtime, design.Executable.RuntimeIdentifier, design.Executable.OperatingSystem
            }, CompositionJson.Options));
            foreach (var (name, value) in design.Assets.Concat(assets)) await Add(name, value.ToArray());
        }
        await WriteOnceAsync(Path.Combine(directory, id + ".zip"), bytes.ToArray(), ct);
    }
    public async Task SaveBundleAsync(FrozenDesignPackage design, CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        await WriteOnceAsync(Path.Combine(directory, design.Executable.Digest + ".runtime.zip"), design.Executable.Archive.ToArray(), ct);
    }
    private static async Task WriteOnceAsync(string path, byte[] bytes, CancellationToken ct)
    {
        if (File.Exists(path))
        {
            if (BuildPipeline.Hash(await File.ReadAllBytesAsync(path, ct)) != BuildPipeline.Hash(bytes))
                throw new ContentValidationException("Retained build input failed its integrity check.");
            return;
        }
        var staging = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(staging, bytes, ct);
            ct.ThrowIfCancellationRequested();
            try { File.Move(staging, path); }
            catch (IOException) when (File.Exists(path))
            {
                if (BuildPipeline.Hash(await File.ReadAllBytesAsync(path, ct)) != BuildPipeline.Hash(bytes)) throw;
            }
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }
}
