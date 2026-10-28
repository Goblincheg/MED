using System.IO;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FluentEmojiComparison;

internal sealed record Variant(int Size, int Width, int Height, long Bytes, string Sha256);
internal sealed record Sample(string Key, string Glyph, string Label, string SourcePath, string SourceSha256,
    int SourceWidth, int SourceHeight, Variant[] Variants);

internal static class PrepareImages
{
    internal static readonly (string Glyph, string Label)[] Selection =
    [
        ("🤣", "Катается от смеха"), ("😂", "Слёзы радости"), ("😀", "Улыбка"),
        ("🥰", "Улыбка с сердечками"), ("🦆", "Утка"), ("❤️", "Красное сердце"),
        ("🔥", "Огонь"), ("👩‍💻", "Женщина за компьютером"), ("👍", "Палец вверх"),
        ("🎉", "Хлопушка"), ("👩🏽‍💻", "Программистка · средний тон"),
        ("🧑🏾‍🚀", "Космонавт · тёмный тон"), ("👨‍🚒", "Мужчина-пожарный"),
        ("👩‍🦼", "Женщина в электроколяске"), ("🏳️‍🌈", "Радужный флаг")
    ];

    internal static void Generate(string collection, string project)
    {
        collection = Path.GetFullPath(collection);
        project = Path.GetFullPath(project);
        var candidates = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var tones = new Dictionary<string, int>
        {
            ["Light"] = 0x1F3FB,
            ["Medium-Light"] = 0x1F3FC,
            ["Medium"] = 0x1F3FD,
            ["Medium-Dark"] = 0x1F3FE,
            ["Dark"] = 0x1F3FF
        };
        foreach (var metadataPath in Directory.EnumerateFiles(Path.Combine(collection, "assets"), "metadata.json", SearchOption.AllDirectories))
        {
            using var metadata = JsonDocument.Parse(File.ReadAllText(metadataPath));
            var root = metadata.RootElement;
            var directory = Path.GetDirectoryName(metadataPath)!;
            var baseText = Decode(root.GetProperty("unicode").GetString()!);
            foreach (var path in Directory.EnumerateFiles(directory, "*.png", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(directory, path).Split(Path.DirectorySeparatorChar);
                if (!relative.Contains("3D", StringComparer.Ordinal)) { continue; }
                var glyph = baseText;
                if (tones.TryGetValue(relative[0], out var tone))
                {
                    glyph = root.GetProperty("unicodeSkintones").EnumerateArray()
                        .Select(value => Decode(value.GetString()!)).Single(value => value.EnumerateRunes().Any(rune => rune.Value == tone));
                }
                else if (relative[0] != "3D" && relative[0] != "Default") { throw new InvalidDataException(path); }
                if (!candidates.TryGetValue(glyph, out var paths)) { candidates[glyph] = paths = new(); }
                paths.Add(path);
            }
        }

        var samples = new List<Sample>();
        foreach (var (glyph, label) in Selection)
        {
            var sourcePath = candidates[glyph].Single();
            var sourceHash = Hash(sourcePath);
            var frame = BitmapFrame.Create(new Uri(sourcePath), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var key = string.Join("-", glyph.EnumerateRunes().Select(rune => rune.Value.ToString("x")));
            var variants = new List<Variant>();
            foreach (var size in new[] { 64, 96, 128 })
            {
                var bitmap = Resize(frame, size);
                var path = Path.Combine(project, "Assets", size.ToString(), key + ".png");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var output = File.Create(path)) { encoder.Save(output); }
                variants.Add(new(size, bitmap.PixelWidth, bitmap.PixelHeight, new FileInfo(path).Length, Hash(path)));
            }
            if (Hash(sourcePath) != sourceHash) { throw new IOException("Original PNG changed"); }
            samples.Add(new(key, glyph, label, Path.GetRelativePath(collection, sourcePath).Replace('\\', '/'),
                sourceHash, frame.PixelWidth, frame.PixelHeight, variants.ToArray()));
        }
        File.WriteAllText(Path.Combine(project, "Assets", "samples.json"), JsonSerializer.Serialize(samples,
            new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        Directory.CreateDirectory(Path.Combine(project, "licenses"));
        File.Copy(Path.Combine(collection, "LICENSE"), Path.Combine(project, "licenses", "Microsoft-Fluent-Emoji-LICENSE.txt"), true);
        foreach (var size in new[] { 64, 96, 128 })
        {
            Console.WriteLine($"{size}px: {samples.Count} images, {samples.Sum(sample => sample.Variants.Single(v => v.Size == size).Bytes)} bytes");
        }
    }

    internal static string Decode(string codepoints) => string.Concat(codepoints.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(codepoint => char.ConvertFromUtf32(Convert.ToInt32(codepoint, 16))));
    internal static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    // Antialiased Lanczos3 in linear-light, premultiplied-alpha space. No matte,
    // post-sharpening or colour/contrast adjustment; only final gamut clamping.
    private static BitmapSource Resize(BitmapSource input, int maximumSide)
    {
        var source = new FormatConvertedBitmap(input, PixelFormats.Bgra32, null, 0);
        var width = source.PixelWidth;
        var height = source.PixelHeight;
        var factor = (double)maximumSide / Math.Max(width, height);
        var targetWidth = Math.Max(1, (int)Math.Round(width * factor));
        var targetHeight = Math.Max(1, (int)Math.Round(height * factor));
        var pixels = new byte[width * height * 4];
        source.CopyPixels(pixels, width * 4, 0);
        var linear = new double[pixels.Length];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var alpha = pixels[i + 3] / 255.0;
            for (var c = 0; c < 3; c++)
            {
                var s = pixels[i + c] / 255.0;
                linear[i + c] = (s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4)) * alpha;
            }
            linear[i + 3] = alpha;
        }
        var horizontal = new double[targetWidth * height * 4];
        var xWeights = Weights(width, targetWidth);
        var yWeights = Weights(height, targetHeight);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < targetWidth; x++)
            {
                foreach (var (index, weight) in xWeights[x])
                {
                    for (var c = 0; c < 4; c++) { horizontal[(y * targetWidth + x) * 4 + c] += linear[(y * width + index) * 4 + c] * weight; }
                }
            }
        }
        var result = new byte[targetWidth * targetHeight * 4];
        for (var y = 0; y < targetHeight; y++)
        {
            for (var x = 0; x < targetWidth; x++)
            {
                var channels = new double[4];
                foreach (var (index, weight) in yWeights[y])
                {
                    for (var c = 0; c < 4; c++) { channels[c] += horizontal[(index * targetWidth + x) * 4 + c] * weight; }
                }
                var offset = (y * targetWidth + x) * 4;
                var alpha = Math.Clamp(channels[3], 0, 1);
                result[offset + 3] = (byte)Math.Round(alpha * 255);
                for (var c = 0; c < 3; c++)
                {
                    var value = alpha > 1e-8 ? Math.Clamp(channels[c], 0, alpha) / alpha : 0;
                    var srgb = value <= 0.0031308 ? value * 12.92 : 1.055 * Math.Pow(value, 1.0 / 2.4) - 0.055;
                    result[offset + c] = (byte)Math.Round(Math.Clamp(srgb, 0, 1) * 255);
                }
            }
        }
        var resized = BitmapSource.Create(targetWidth, targetHeight, 96, 96, PixelFormats.Bgra32, null, result, targetWidth * 4);
        resized.Freeze();
        return resized;
    }

    private static (int Index, double Weight)[][] Weights(int source, int target)
    {
        var ratio = (double)source / target;
        var result = new (int, double)[target][];
        for (var x = 0; x < target; x++)
        {
            var center = (x + 0.5) * ratio - 0.5;
            var weights = new List<(int, double)>();
            var sum = 0.0;
            for (var i = Math.Max(0, (int)Math.Ceiling(center - 3 * ratio)); i <= Math.Min(source - 1, (int)Math.Floor(center + 3 * ratio)); i++)
            {
                var distance = (i - center) / ratio;
                var weight = Math.Abs(distance) < 1e-10 ? 1 : Math.Abs(distance) >= 3 ? 0
                    : Math.Sin(Math.PI * distance) * Math.Sin(Math.PI * distance / 3) / (Math.PI * Math.PI * distance * distance / 3);
                weights.Add((i, weight));
                sum += weight;
            }
            result[x] = weights.Select(value => (value.Item1, value.Item2 / sum)).ToArray();
        }
        return result;
    }
}
