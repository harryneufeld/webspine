using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Webspine.Core.Composition;

// First upload format: a bounded PNG subset, 8-bit RGB/RGBA, non-interlaced, with no metadata.
// Decode the complete filtered pixel stream and strip ancillary chunks before storing immutable bytes.
public static class CompositionMedia
{
    public const int MaximumBytes = 2 * 1024 * 1024;
    public static (AssetContent Asset, ImmutableArray<byte> Bytes) Png(ReadOnlySpan<byte> input)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (input.Length > MaximumBytes || input.Length < 45 || !input[..8].SequenceEqual(signature)) Fail();
        using var stored = new MemoryStream(); stored.Write(signature);
        using var compressed = new MemoryStream();
        var position = 8; var width = 0; var height = 0; var channels = 0; var ended = false; var dataSeen = false; var dataEnded = false;
        while (position < input.Length)
        {
            if (input.Length - position < 12) Fail();
            var length = BinaryPrimitives.ReadUInt32BigEndian(input.Slice(position, 4));
            if (length > MaximumBytes || length > input.Length - position - 12) Fail();
            var chunk = input.Slice(position, (int)length + 12); var type = chunk.Slice(4, 4); var data = chunk.Slice(8, (int)length);
            foreach (var letter in type) if (letter is not (>= 65 and <= 90) and not (>= 97 and <= 122)) Fail();
            if ((type[2] & 32) != 0) Fail();
            if (Crc(chunk.Slice(4, (int)length + 4)) != BinaryPrimitives.ReadUInt32BigEndian(chunk[^4..])) Fail();
            if (type.SequenceEqual("IHDR"u8))
            {
                if (position != 8 || length != 13) Fail();
                var w = BinaryPrimitives.ReadUInt32BigEndian(data[..4]); var h = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(4, 4));
                if (w > 4096 || h > 4096) Fail();
                width = (int)w; height = (int)h;
                if (width < 1 || height < 1 || width > 4096 || height > 4096 || (long)width * height > 4_000_000 || data[8] != 8 || data[9] is not (2 or 6) || data[10] != 0 || data[11] != 0 || data[12] != 0) Fail();
                channels = data[9] == 2 ? 3 : 4; stored.Write(chunk);
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                if (channels == 0 || dataEnded) Fail(); dataSeen = true; compressed.Write(data); stored.Write(chunk);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                if (!dataSeen || length != 0 || position + chunk.Length != input.Length) Fail();
                stored.Write(chunk); ended = true;
            }
            else
            {
                // Unknown critical chunks (including animation/palette behavior) are refused.
                if ((type[0] & 32) == 0 || type.SequenceEqual("acTL"u8) || type.SequenceEqual("fcTL"u8) || type.SequenceEqual("fdAT"u8)) Fail();
                if (dataSeen) dataEnded = true;
            }
            position += chunk.Length;
        }
        if (!ended) Fail();
        if (compressed.Length < 6) Fail();
        compressed.Position = 0;
        try
        {
            using var pixels = new ZLibStream(compressed, CompressionMode.Decompress);
            var row = new byte[checked(width * channels + 1)];
            uint a = 1, b = 0;
            for (var y = 0; y < height; y++)
            {
                pixels.ReadExactly(row); if (row[0] > 4) Fail();
                foreach (var pixel in row) { a = (a + pixel) % 65521; b = (b + a) % 65521; }
            }
            if (pixels.ReadByte() != -1) Fail();
            // Streams can return EOF on a missing trailer; require the complete zlib checksum explicitly.
            if (BinaryPrimitives.ReadUInt32BigEndian(compressed.GetBuffer().AsSpan((int)compressed.Length - 4, 4)) != (b << 16 | a)) Fail();
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException) { Fail(); }
        var bytes = stored.ToArray().ToImmutableArray();
        var digest = Convert.ToHexString(SHA256.HashData(bytes.AsSpan())).ToLowerInvariant();
        return (new("media-" + digest[..58], "assets/media-" + digest + ".png", "image/png"), bytes);
    }
    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var b in bytes) { crc ^= b; for (var i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0); }
        return ~crc;
    }
    private static void Fail() => throw new ContentValidationException("Upload a valid non-interlaced 8-bit RGB/RGBA PNG, at most 2 MiB, 4096 pixels per side and 4 million pixels.");
}
