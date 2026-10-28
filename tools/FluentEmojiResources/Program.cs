using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using EmojiAssetPreparation;
using EmojiPicker;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 3 || args[0] is not ("generate" or "verify"))
            {
                throw new ArgumentException("Usage: generate|verify <upstream-directory> <MED-directory>");
            }
            _ = new Application();
            Emoji.Wpf.EmojiData.Load();
            Generate(args[1], args[2], args[0] == "verify");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void Generate(string collectionPath, string projectPath, bool verify)
    {
        var collection = Path.GetFullPath(collectionPath);
        var project = Path.GetFullPath(projectPath);
        var output = Path.Combine(project, "EmojiPicker", "Resources", "Fluent64");
        if (output.StartsWith(collection + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) { throw new IOException("Output must not be inside upstream"); }
        var tones = new Dictionary<string, int> { ["Light"] = 0x1F3FB, ["Medium-Light"] = 0x1F3FC, ["Medium"] = 0x1F3FD, ["Medium-Dark"] = 0x1F3FE, ["Dark"] = 0x1F3FF };
        var index = new Dictionary<string, (string Image, string Metadata, string MetadataHash)>(StringComparer.Ordinal);
        using var sourceIndex = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var metadataFile in Directory.EnumerateFiles(Path.Combine(collection, "assets"), "metadata.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var metadataBytes = File.ReadAllBytes(metadataFile);
            var metadataHash = Hash(metadataBytes);
            sourceIndex.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(collection, metadataFile).Replace('\\', '/') + "\n" + metadataHash + "\n"));
            using var json = JsonDocument.Parse(metadataBytes);
            var metadata = json.RootElement;
            var baseText = Decode(metadata.GetProperty("unicode").GetString()!);
            var directory = Path.GetDirectoryName(metadataFile)!;
            foreach (var png in Directory.EnumerateFiles(directory, "*.png", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                var parts = Path.GetRelativePath(directory, png).Split(Path.DirectorySeparatorChar);
                if (!parts.Contains("3D", StringComparer.Ordinal)) { continue; }
                var text = baseText;
                if (tones.TryGetValue(parts[0], out var tone))
                {
                    text = metadata.GetProperty("unicodeSkintones").EnumerateArray().Select(value => Decode(value.GetString()!))
                        .Single(value => value.EnumerateRunes().Any(rune => rune.Value == tone));
                }
                else if (parts[0] != "3D" && parts[0] != "Default") { throw new InvalidDataException(png); }
                if (!index.TryAdd(text, (png, metadataFile, metadataHash))) { throw new InvalidDataException("Ambiguous Unicode: " + Code(text)); }
            }
        }

        var groups = new HashSet<string> { "Smileys & Emotion", "Animals & Nature", "People & Body", "Activities", "Objects", "Food & Drink", "Travel & Places", "Symbols" };
        var catalog = Emoji.Wpf.EmojiData.AllGroups.Where(group => groups.Contains(group.Name)).SelectMany(group => group.SubGroups).SelectMany(group => group.EmojiList)
            .OrderBy(emoji => emoji.Text, StringComparer.Ordinal).ToArray();
        if (catalog.Length != 1558 || catalog.Select(emoji => emoji.Text).Distinct(StringComparer.Ordinal).Count() != 1558) { throw new InvalidDataException("Unexpected MED catalog"); }
        var license = File.ReadAllBytes(Path.Combine(collection, "LICENSE"));
        var manifest = new FluentEmojiManifest { CatalogCount = catalog.Length, Generator = "Lanczos3-linear-premultiplied-v1+lossless-zlib; Emoji.Wpf 0.3.4", SourceIndexSha256 = Convert.ToHexString(sourceIndex.GetHashAndReset()), SourceLicenseSha256 = Hash(license) };
        Directory.CreateDirectory(output);
        foreach (var emoji in catalog)
        {
            if (!index.TryGetValue(emoji.Text, out var source)) { manifest.Missing.Add(new(emoji.Text, emoji.Name)); continue; }
            var input = File.ReadAllBytes(source.Image);
            var sourceHash = Hash(input);
            using var stream = new MemoryStream(input, writable: false);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var bitmap = LanczosResampler.Resize(frame, 64);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var memory = new MemoryStream();
            encoder.Save(memory);
            var png = Repack(memory.ToArray());
            var file = Code(emoji.Text) + ".png";
            Put(Path.Combine(output, file), png, verify);
            if (Hash(File.ReadAllBytes(source.Image)) != sourceHash) { throw new IOException("Upstream image changed while reading"); }
            manifest.Entries.Add(emoji.Text, new FluentEmojiAsset { File = file, Width = bitmap.PixelWidth, Height = bitmap.PixelHeight, Bytes = png.Length,
                Sha256 = Hash(png), Source = Path.GetRelativePath(collection, source.Image).Replace('\\', '/'), SourceSha256 = sourceHash, MetadataSha256 = source.MetadataHash });
            if (manifest.Entries.Count % 100 == 0) { Console.WriteLine($"{manifest.Entries.Count} prepared"); }
        }
        if (manifest.Entries.Count != 1545 || manifest.Missing.Count != 13) { throw new InvalidDataException("Coverage changed; re-audit upstream before updating expected counts"); }
        if (Directory.EnumerateFiles(output, "*.png").Count() != manifest.Entries.Count) { throw new InvalidDataException("Unexpected extra PNG in output"); }
        var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        Put(Path.Combine(output, "manifest.json"), Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest, options) + "\n"), verify);
        // Startup reads only this compact lookup, not all source paths/hashes.
        var runtimeIndex = new { manifest.SchemaVersion, manifest.PixelSize, manifest.CatalogCount,
            Entries = manifest.Entries.ToDictionary(pair => pair.Key, pair => new { pair.Value.File, pair.Value.Width, pair.Value.Height }, StringComparer.Ordinal) };
        Put(Path.Combine(output, "index.json"), Encoding.UTF8.GetBytes(JsonSerializer.Serialize(runtimeIndex,
            new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n"), verify);
        Put(Path.Combine(project, "licenses", "Microsoft-Fluent-Emoji-LICENSE.txt"), license, verify);
        Console.WriteLine($"{(verify ? "Verified" : "Generated")}: {manifest.Entries.Count} PNG, {manifest.Missing.Count} fallback, {manifest.Entries.Values.Sum(entry => entry.Bytes)} bytes");
    }

    private static void Put(string path, byte[] bytes, bool verify)
    {
        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) { return; }
        if (verify) { throw new InvalidDataException("Non-reproducible/missing output: " + path); }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }
    private static string Decode(string codes) => string.Concat(codes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(code => char.ConvertFromUtf32(Convert.ToInt32(code, 16))));
    private static string Code(string text) => string.Join("-", text.EnumerateRunes().Select(rune => rune.Value.ToString("x")));
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    // Recompress only the PNG IDAT stream. Pixel/filter data and all other chunks
    // stay unchanged, and the original encoding wins if it is already smaller.
    private static byte[] Repack(byte[] png)
    {
        using var compressed = new MemoryStream();
        var chunks = new List<(string Type, byte[] Raw)>();
        for (var offset = 8; offset < png.Length;)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
            var type = Encoding.ASCII.GetString(png, offset + 4, 4);
            if (type == "IDAT") { compressed.Write(png, offset + 8, length); }
            else { chunks.Add((type, png.AsSpan(offset, length + 12).ToArray())); }
            offset += length + 12;
        }
        compressed.Position = 0;
        using var unpacked = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionMode.Decompress, true)) { zlib.CopyTo(unpacked); }
        using var repacked = new MemoryStream();
        using (var zlib = new ZLibStream(repacked, CompressionLevel.SmallestSize, true)) { zlib.Write(unpacked.ToArray()); }
        using var result = new MemoryStream();
        result.Write(png, 0, 8);
        Span<byte> number = stackalloc byte[4];
        foreach (var (type, raw) in chunks)
        {
            if (type == "IEND")
            {
                var payload = repacked.ToArray();
                BinaryPrimitives.WriteInt32BigEndian(number, payload.Length); result.Write(number);
                var tag = Encoding.ASCII.GetBytes("IDAT"); result.Write(tag); result.Write(payload);
                var crc = 0xFFFFFFFFu;
                foreach (var b in tag.Concat(payload))
                {
                    crc ^= b;
                    for (var bit = 0; bit < 8; bit++) { crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0); }
                }
                BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); result.Write(number);
            }
            result.Write(raw);
        }
        return result.Length < png.Length ? result.ToArray() : png;
    }
}
