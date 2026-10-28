using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EmojiPicker;

internal static class FluentGraphicsChecks
{
    internal static void Run(MainWindow window, IReadOnlyList<EmojiPicker.Emoji> displayed, Action<bool, string> check)
    {
        check(FluentEmojiImages.DecodeCount == 0, "startup reads manifest but decodes no PNG");
        var manifest = FluentEmojiImages.Manifest;
        check(manifest.Entries.Count == 1545 && manifest.Missing.Count == 13 && manifest.CatalogCount == 1558, "complete Fluent manifest");
        var catalog = (List<EmojiPicker.Emoji>)typeof(MainWindow).GetField("catalogEmojis", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        check(catalog.Count == 1558 && catalog.Select(e => e.Character).Distinct().Count() == 1558, "all original Unicode sequences retained in data");
        check(displayed.Count == 1552 && displayed.Count(e => manifest.Entries.ContainsKey(e.Character)) == 1545, "Fluent admits 147 formerly hidden entries and retains seven fallbacks");
        check(manifest.Entries.Keys.All(key => displayed.Any(e => e.Character == key)), "every exact Fluent mapping is displayable");
        check(catalog.All(e => manifest.Entries.ContainsKey(e.Character) || manifest.Missing.Any(m => m.Text == e.Character)), "no sequence lost or normalised during mapping");
        var groups = new HashSet<string> { "Smileys & Emotion", "Animals & Nature", "People & Body", "Activities", "Objects", "Food & Drink", "Travel & Places", "Symbols" };
        var original = Emoji.Wpf.EmojiData.AllGroups.Where(g => groups.Contains(g.Name)).SelectMany(g => g.SubGroups).SelectMany(g => g.EmojiList).Select(e => e.Text);
        check(catalog.Select(e => e.Character).SequenceEqual(original), "all outgoing Emoji.Character values remain byte-for-byte original and ordered");
        foreach (var (text, asset) in manifest.Entries)
        {
            using var stream = FluentEmojiImages.OpenAsset(asset.File)!;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var bytes = memory.ToArray();
            check(bytes.LongLength == asset.Bytes && Convert.ToHexString(SHA256.HashData(bytes)) == asset.Sha256, "PNG content matches manifest");
            memory.Position = 0;
            var bitmap = BitmapFrame.Create(memory, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            check(bitmap.PixelWidth == asset.Width && bitmap.PixelHeight == asset.Height && Math.Max(bitmap.PixelWidth, bitmap.PixelHeight) == 64, "64 px image, never a 256 px original");
            check(asset.File == string.Join("-", text.EnumerateRunes().Select(r => r.Value.ToString("x"))) + ".png", "resource key preserves exact Unicode scalars");
            var bgra = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            var rgba = new byte[bgra.PixelWidth * bgra.PixelHeight * 4];
            bgra.CopyPixels(rgba, bgra.PixelWidth * 4, 0);
            check(Enumerable.Range(0, rgba.Length / 4).Any(i => rgba[i * 4 + 3] > 0), "image has visible alpha content");
        }
        foreach (var missing in manifest.Missing)
        {
            check(FluentEmojiImage.SystemFallback(missing.Text) != null, "missing asset has a nonempty fallback/availability marker");
        }
        // Optional source-tree comparison with the user-approved prototype.
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository != null && !File.Exists(Path.Combine(repository.FullName, "global.json"))) { repository = repository.Parent; }
        if (repository != null)
        {
            foreach (var text in new[] { "🤣", "😂", "😀", "🥰", "🦆", "❤️", "🔥", "👩‍💻", "👍", "🎉", "👨‍🚒", "👩‍🦼" })
            {
                var asset = manifest.Entries[text];
                var prototype = Path.Combine(repository.FullName, "prototypes", "FluentEmojiComparison", "Assets", "64", asset.File);
                if (!File.Exists(prototype)) { continue; }
                using var embedded = FluentEmojiImages.OpenAsset(asset.File)!;
                var generated = BitmapFrame.Create(embedded, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var approved = BitmapFrame.Create(new Uri(prototype), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                check(Pixels(generated).SequenceEqual(Pixels(approved)), "64 px resampling pixels match approved prototype");
            }
        }

        var first = manifest.Entries.Keys.First();
        for (var i = 0; i < 200; i++)
        {
            var bitmap = FluentEmojiImages.LoadAsync(manifest.Entries.Keys.ElementAt(i), CancellationToken.None).GetAwaiter().GetResult();
            check(bitmap?.IsFrozen == true, "background-decoded image is frozen");
        }
        check(FluentEmojiImages.CachedCount == 128 && FluentEmojiImages.CachedPixelBytes <= 128L * 64 * 64 * 4, "LRU remains bounded to 128 / 2 MiB pixels");
        var last = manifest.Entries.Keys.ElementAt(199);
        var decodeCount = FluentEmojiImages.DecodeCount;
        for (var i = 0; i < 50; i++) { FluentEmojiImages.LoadAsync(last, CancellationToken.None).GetAwaiter().GetResult(); }
        check(FluentEmojiImages.DecodeCount == decodeCount, "cache hits do not decode again");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            FluentEmojiImages.LoadAsync(first, cancelled.Token).GetAwaiter().GetResult();
            throw new Exception("cancelled image request accepted");
        }
        catch (OperationCanceledException) { check(true, "cancelled image request rejected"); }
        FluentEmojiImages.Clear();
        check(FluentEmojiImages.CachedCount == 0 && FluentEmojiImages.CachedPixelBytes == 0, "cache clear releases decoded references");
        var pendingLoad = FluentEmojiImages.LoadAsync(first, CancellationToken.None);
        FluentEmojiImages.Clear();
        pendingLoad.GetAwaiter().GetResult();
        check(FluentEmojiImages.CachedCount == 0, "completed old-generation load cannot repopulate a cleared cache");

        var image = new Image { Width = 32, Height = 32 };
        var host = new Window { Content = image, Width = 100, Height = 100, ShowActivated = false, Topmost = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000 };
        FluentEmojiImage.SetSource(image, "😀");
        host.Show();
        FluentEmojiImage.SetSource(image, "🦆");
        Pump(host);
        check(FluentEmojiImages.TryGetCached("🦆", out var duck) && ReferenceEquals(image.Source, duck), "recycling cannot assign stale emoji after binding change");
        host.Hide(); Pump(host);
        check(image.Source == null, "hidden cell releases its ImageSource");
        host.Show(); Pump(host);
        check(image.Source != null, "reloaded cell reacquires image");
        host.Close(); Pump(window);
        check(image.Source == null, "unloaded cell releases source and pending request");

        window.ShowActivated = false; window.Topmost = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -32000; window.Top = -32000;
        window.Show(); window.UpdateLayout(); Pump(window);
        var grid = (ListBox)window.FindName("EmojiGrid");
        var load = typeof(MainWindow).GetMethod("LoadCategory", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var maxRealized = 0;
        foreach (var category in new[] { "Smileys", "People", "Celebrations", "Food", "Transport", "Symbols" })
        {
            load.Invoke(window, [category]); window.UpdateLayout(); Pump(window);
            foreach (var index in new[] { 0, grid.Items.Count / 2, grid.Items.Count - 1 })
            {
                grid.ScrollIntoView(grid.Items[index]); window.UpdateLayout(); Pump(window);
                maxRealized = Math.Max(maxRealized, Enumerable.Range(0, grid.Items.Count).Count(i => grid.ItemContainerGenerator.ContainerFromIndex(i) != null));
                check(Descendants<Image>(grid).Where(i => i.IsLoaded && i.IsVisible).All(i => i.Source != null), "no blank visible cell after scrolling");
            }
        }
        check(System.Windows.Controls.VirtualizingPanel.GetIsVirtualizing(grid)
            && Descendants<WpfToolkit.Controls.VirtualizingWrapPanel>(grid).Any() && maxRealized < 100, "recycling/VirtualizingWrapPanel retained; fewer than 100 containers");
        check(FluentEmojiImages.CachedCount <= 128, "category/scroll stress does not grow cache");
        // Reuse the same live resources on the existing dark theme.
        Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = new Uri("pack://application:,,,/MED;component/Theme/DarkTheme.xaml") };
        window.UpdateLayout(); Pump(window);
        check(Descendants<Image>(grid).Where(i => i.IsVisible).All(i => i.Source != null), "dark theme preserves images");
        Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = new Uri("pack://application:,,,/MED;component/Theme/LightTheme.xaml") };
        window.Hide(); Pump(window);
        FluentEmojiImages.Clear();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) { yield return typed; }
            foreach (var descendant in Descendants<T>(child)) { yield return descendant; }
        }
    }

    private static byte[] Pixels(BitmapSource bitmap)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var bytes = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(bytes, converted.PixelWidth * 4, 0);
        return bytes;
    }

    private static void Pump(Window window)
    {
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
        var task = Task.WhenAll(Descendants<Image>(window).Select(FluentEmojiImage.WhenLoadedAsync).Append(FluentEmojiImages.WhenIdleAsync()));
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            task.ContinueWith(_ => window.Dispatcher.BeginInvoke(new Action(() => frame.Continue = false)));
            Dispatcher.PushFrame(frame);
        }
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }
}
