using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EmojiAssetPreparation;

// Same linear-light, premultiplied-alpha Lanczos3 implementation as the approved prototype.
internal static class LanczosResampler
{
    internal static BitmapSource Resize(BitmapSource input, int maximumSide)
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
