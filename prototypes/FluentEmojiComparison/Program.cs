using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FluentEmojiComparison;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var application = new Application();
            if (args.FirstOrDefault() == "--prepare")
            {
                PrepareImages.Generate(args[1], args[2]);
                return 0;
            }
            global::Emoji.Wpf.EmojiData.Load();
            var samples = LoadSamples();
            var window = new ComparisonWindow(samples);
            if (args.FirstOrDefault() == "--verify")
            {
                var checks = Verify(samples, window);
                var result = JsonSerializer.Serialize(new { success = true, samples = samples.Length, images = samples.Length * 3, checks });
                File.WriteAllText(args[1], result);
                Console.WriteLine(result);
                window.Close();
                return 0;
            }
            return application.Run(window);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            if (args.Length == 0)
            {
                MessageBox.Show(exception.Message, "Fluent Emoji — ошибка прототипа", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return 1;
        }
    }

    internal static Sample[] LoadSamples()
    {
        using var stream = Resource("Assets/samples.json");
        return JsonSerializer.Deserialize<Sample[]>(stream)!;
    }

    internal static Stream Resource(string path) => Application.GetResourceStream(
        new Uri("pack://application:,,,/FluentEmojiComparison;component/" + path))!.Stream;

    internal static BitmapImage LoadImage(Sample sample, int size)
    {
        using var stream = Resource($"Assets/{size}/{sample.Key}.png");
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static int Verify(Sample[] samples, ComparisonWindow window)
    {
        var checks = 0;
        void Check(bool result, string description)
        {
            if (!result) { throw new InvalidDataException(description); }
            checks++;
        }
        Check(samples.Length == 15 && samples.Select(s => s.Glyph).Distinct().Count() == 15, "15 distinct samples");
        Check(samples.Count(s => s.Glyph.Contains('\u200D')) >= 5, "Complex Unicode samples");
        foreach (var sample in samples)
        {
            foreach (var variant in sample.Variants)
            {
                using var stream = Resource($"Assets/{variant.Size}/{sample.Key}.png");
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                var bytes = memory.ToArray();
                Check(bytes.Length == variant.Bytes && Convert.ToHexString(SHA256.HashData(bytes)) == variant.Sha256, "Embedded PNG fidelity");
                var bitmap = LoadImage(sample, variant.Size);
                Check(bitmap.PixelWidth == variant.Width && bitmap.PixelHeight == variant.Height
                    && Math.Max(bitmap.PixelWidth, bitmap.PixelHeight) == variant.Size, "Image dimensions");
                var bgra = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                var pixels = new byte[bgra.PixelWidth * bgra.PixelHeight * 4];
                bgra.CopyPixels(pixels, bgra.PixelWidth * 4, 0);
                var alphas = Enumerable.Range(0, pixels.Length / 4).Select(i => pixels[i * 4 + 3]).ToArray();
                Check(alphas.Any(a => a == 0) && alphas.Any(a => a > 0), "Transparency and nonempty content");
            }
        }
        foreach (var actualDpi in new[] { 1.0, 1.25, 1.5, 2.0 })
        {
            foreach (var scale in new[] { 1.0, 1.25, 1.5 })
            {
                Check(Math.Abs(ComparisonWindow.PreviewDip(scale, actualDpi) * actualDpi - 32 * scale) < 1e-8, "DPI simulation physical size");
            }
        }
        window.Measure(new Size(1120, 850));
        window.Arrange(new Rect(0, 0, 1120, 850));
        window.UpdateLayout();
        Check(window.PreviewImages.Count == 120 && window.PreviewImages.All(image => image.Source != null), "All light/dark/system sources resolved");
        foreach (var scale in new[] { 1.0, 1.25, 1.5 })
        {
            window.SetScale(scale);
            Check(window.PreviewImages.Select(image => image.Width).Distinct().Count() == 1, "Equal display size across all source resolutions");
        }
        foreach (var theme in new[] { 0, 1, 2 })
        {
            window.SetBackgroundMode(theme);
            var expected = theme == 0 ? 120 : 60;
            Check(window.PreviewTiles.Count(tile => tile.Border.Visibility == Visibility.Visible) == expected, "Background switching");
        }
        return checks;
    }
}

internal sealed class ComparisonWindow : Window
{
    internal List<Image> PreviewImages { get; } = new();
    internal List<(Border Border, bool Dark)> PreviewTiles { get; } = new();
    private readonly TextBlock status = new();
    private double simulation = 1;

    internal ComparisonWindow(Sample[] samples)
    {
        Title = "Fluent Emoji 3D — сравнение 64 / 96 / 128 px";
        Width = Math.Min(1120, SystemParameters.WorkArea.Width * 0.94);
        Height = Math.Min(900, SystemParameters.WorkArea.Height * 0.92);
        MinWidth = Math.Min(850, Width);
        MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        Background = Brushes.White;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        var layout = new DockPanel { Margin = new Thickness(18) };
        Content = layout;
        var heading = new StackPanel();
        DockPanel.SetDock(heading, Dock.Top);
        layout.Children.Add(heading);
        heading.Children.Add(Text("Fluent Emoji 3D · один размер отображения, три размера файла", 19, FontWeights.SemiBold));
        heading.Children.Add(Text("15 образцов · исходные PNG Microsoft · Lanczos3, прозрачность сохранена · без повышения резкости", 12));
        var controls = new WrapPanel { Margin = new Thickness(0, 14, 0, 8), VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(controls);
        controls.Children.Add(Text("Моделирование DPI: ", 13));
        var scale = new ComboBox { Width = 100, Margin = new Thickness(4, 0, 22, 0), ItemsSource = new[] { "100%", "125%", "150%" }, SelectedIndex = 0 };
        controls.Children.Add(scale);
        scale.SelectionChanged += (_, _) => SetScale(new[] { 1.0, 1.25, 1.5 }[scale.SelectedIndex]);
        controls.Children.Add(Text("Фон образцов: ", 13));
        var theme = new ComboBox { Width = 150, Margin = new Thickness(4, 0, 0, 0), ItemsSource = new[] { "Оба рядом", "Светлый", "Тёмный" }, SelectedIndex = 0 };
        controls.Children.Add(theme);
        theme.SelectionChanged += (_, _) => SetBackgroundMode(theme.SelectedIndex);
        heading.Children.Add(status);
        heading.Children.Add(Text("Все колонки показывают одинаковые 32 / 40 / 48 физических пикселей. Размеры файлов не меняются.", 12));

        var footer = Text("Отдельный прототип: нет горячих клавиш, вставки, трея и автозапуска. Изображения Microsoft — MIT; лицензия рядом с EXE.", 11);
        footer.Margin = new Thickness(0, 10, 0, 0);
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(footer);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 12, 0, 0) };
        layout.Children.Add(scroll);
        var rows = new StackPanel();
        scroll.Content = rows;
        var header = Row();
        rows.Children.Add(header);
        AddColumn(header, Text("Эмодзи / Unicode", 13, FontWeights.SemiBold), 0);
        foreach (var (caption, index) in new[] { ("Системный · как в MED", 1), ("PNG 64 px", 2), ("PNG 96 px", 3), ("PNG 128 px", 4) })
        {
            var text = Text(caption, 13, FontWeights.SemiBold);
            text.TextAlignment = TextAlignment.Center;
            AddColumn(header, text, index);
        }
        foreach (var sample in samples)
        {
            var grid = Row();
            grid.Margin = new Thickness(0, 8, 0, 4);
            rows.Children.Add(grid);
            var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            label.Children.Add(Text(sample.Label, 13, FontWeights.SemiBold));
            var codes = Text(string.Join(" ", sample.Glyph.EnumerateRunes().Select(r => r.Value.ToString("X"))), 10);
            codes.TextWrapping = TextWrapping.Wrap;
            codes.Foreground = Brushes.DimGray;
            label.Children.Add(codes);
            label.ToolTip = sample.SourcePath;
            AddColumn(grid, label, 0);
            AddColumn(grid, ImageCell(sample, null, "Emoji.Wpf / Segoe UI Emoji"), 1);
            foreach (var (size, column) in new[] { (64, 2), (96, 3), (128, 4) })
            {
                var variant = sample.Variants.Single(v => v.Size == size);
                var bitmap = Program.LoadImage(sample, size);
                AddColumn(grid, ImageCell(sample, bitmap, $"{variant.Width}×{variant.Height} · {variant.Bytes / 1024.0:F1} КиБ"), column);
            }
        }
        SourceInitialized += (_, _) => UpdateScale();
        DpiChanged += (_, e) => UpdateScale(e.NewDpi.DpiScaleX);
        UpdateScale();
    }

    private UIElement ImageCell(Sample sample, ImageSource? bitmap, string info)
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var tiles = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(tiles);
        foreach (var dark in new[] { false, true })
        {
            var image = new Image { Stretch = Stretch.Uniform, SnapsToDevicePixels = true, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            if (bitmap == null) { global::Emoji.Wpf.Image.SetSource(image, sample.Glyph); }
            else { image.Source = bitmap; }
            PreviewImages.Add(image);
            var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(image);
            var caption = Text(dark ? "тёмный" : "светлый", 10);
            caption.Foreground = dark ? Brushes.WhiteSmoke : Brushes.DimGray;
            caption.TextAlignment = TextAlignment.Center;
            caption.Margin = new Thickness(0, 4, 0, 0);
            content.Children.Add(caption);
            var tile = new Border
            {
                Width = 72,
                Height = 84,
                Margin = new Thickness(2),
                Padding = new Thickness(4),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                BorderBrush = dark ? Brushes.DimGray : Brushes.LightGray,
                Background = new SolidColorBrush(dark ? Color.FromRgb(36, 36, 36) : Color.FromRgb(247, 247, 247)),
                Child = content
            };
            tiles.Children.Add(tile);
            PreviewTiles.Add((tile, dark));
        }
        var description = Text(info, 10);
        description.TextAlignment = TextAlignment.Center;
        description.Foreground = Brushes.DimGray;
        panel.Children.Add(description);
        return panel;
    }

    private static Grid Row()
    {
        var grid = new Grid();
        foreach (var width in new[] { 206, 160, 160, 160, 160 }) { grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) }); }
        return grid;
    }
    private static void AddColumn(Grid row, UIElement child, int column) { Grid.SetColumn(child, column); row.Children.Add(child); }
    private static TextBlock Text(string value, double size, FontWeight? weight = null) =>
        new() { Text = value, FontSize = size, FontWeight = weight ?? FontWeights.Normal, Margin = new Thickness(0, 2, 0, 2) };

    internal static double PreviewDip(double simulation, double actualDpi) => 32 * simulation / actualDpi;
    internal void SetScale(double scale) { simulation = scale; UpdateScale(); }
    internal void SetBackgroundMode(int mode)
    {
        foreach (var tile in PreviewTiles)
        {
            tile.Border.Visibility = mode == 0 || (mode == 1 && !tile.Dark) || (mode == 2 && tile.Dark) ? Visibility.Visible : Visibility.Collapsed;
        }
    }
    private void UpdateScale(double? actualDpi = null)
    {
        var dpi = actualDpi ?? VisualTreeHelper.GetDpi(this).DpiScaleX;
        var size = PreviewDip(simulation, dpi);
        foreach (var image in PreviewImages) { image.Width = size; image.Height = size; }
        status.Text = $"Фактический масштаб окна Windows: {dpi * 100:F0}% · образец: {simulation * 100:F0}% = {32 * simulation:F0} px · 32 DIP при моделируемом DPI";
    }
}
