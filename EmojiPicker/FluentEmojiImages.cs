using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace EmojiPicker
{
    internal static class FluentEmojiImages
    {
        internal const int Capacity = 128;
        private const string ResourcePrefix = "EmojiPicker.Fluent64.";
        private static readonly Lazy<FluentEmojiManifest> Data = new(() => ReadManifest("index.json"));
        private static readonly Lazy<FluentEmojiManifest> Provenance = new(() => ReadManifest("manifest.json"));
        private static readonly object Gate = new();
        private static readonly Dictionary<string, LinkedListNode<(string Key, BitmapImage Image)>> Cache = new(StringComparer.Ordinal);
        private static readonly LinkedList<(string Key, BitmapImage Image)> Lru = new();
        private static readonly HashSet<string> Failed = new(StringComparer.Ordinal);
        private static readonly SemaphoreSlim DecodeSlots = new(2);
        private static int pending;
        private static int generation;
        private static TaskCompletionSource idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private static long decoded;

        internal static FluentEmojiManifest Manifest => Provenance.Value;
        internal static int CachedCount { get { lock (Gate) { return Cache.Count; } } }
        internal static long CachedPixelBytes { get { lock (Gate) { return SumPixels(); } } }
        internal static long DecodeCount => Interlocked.Read(ref decoded);
        internal static bool HasImage(string text) => Data.Value.Entries.ContainsKey(text);
        internal static Stream? OpenAsset(string file) => typeof(FluentEmojiImages).Assembly.GetManifestResourceStream(ResourcePrefix + file);

        private static FluentEmojiManifest ReadManifest(string file)
        {
            using var stream = OpenAsset(file) ?? throw new InvalidDataException("Fluent manifest missing");
            var data = JsonSerializer.Deserialize<FluentEmojiManifest>(stream) ?? throw new InvalidDataException("Fluent manifest invalid");
            if (data.SchemaVersion != 1 || data.PixelSize != 64 || data.Entries.Count != 1545 || data.CatalogCount != 1558)
            {
                throw new InvalidDataException("Unexpected Fluent manifest schema/coverage");
            }
            foreach (var entry in data.Entries.Values)
            {
                if (!entry.File.EndsWith(".png", StringComparison.Ordinal) || entry.File.Contains('/') || entry.File.Contains('\\')
                    || entry.Width < 1 || entry.Height < 1 || Math.Max(entry.Width, entry.Height) != 64)
                {
                    throw new InvalidDataException("Invalid Fluent asset descriptor");
                }
            }
            return data;
        }

        internal static bool TryGetCached(string text, out BitmapImage? image)
        {
            lock (Gate)
            {
                if (Cache.TryGetValue(text, out var node))
                {
                    Lru.Remove(node); Lru.AddFirst(node); image = node.Value.Image; return true;
                }
                image = null;
                return false;
            }
        }

        internal static async Task<BitmapImage?> LoadAsync(string text, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!Data.Value.Entries.TryGetValue(text, out var asset)) { return null; }
            if (TryGetCached(text, out var cached)) { return cached; }
            int epoch;
            lock (Gate)
            {
                if (Failed.Contains(text)) { return null; }
                epoch = generation;
                if (pending++ == 0) { idle = new(TaskCreationOptions.RunContinuationsAsynchronously); }
            }
            var entered = false;
            try
            {
                await DecodeSlots.WaitAsync(cancellation).ConfigureAwait(false);
                entered = true;
                if (TryGetCached(text, out cached)) { return cached; }
                return await Task.Run(() =>
                {
                    cancellation.ThrowIfCancellationRequested();
                    using var stream = OpenAsset(asset.File) ?? throw new InvalidDataException("Fluent PNG missing: " + asset.File);
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    Interlocked.Increment(ref decoded);
                    lock (Gate)
                    {
                        if (!cancellation.IsCancellationRequested && epoch == generation)
                        {
                            if (Cache.TryGetValue(text, out var old)) { Lru.Remove(old); Cache.Remove(text); }
                            Cache.Add(text, Lru.AddFirst((text, bitmap)));
                            while (Cache.Count > Capacity)
                            {
                                var last = Lru.Last!; Cache.Remove(last.Value.Key); Lru.RemoveLast();
                            }
                        }
                    }
                    return bitmap;
                }, cancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error)
            {
                lock (Gate) { Failed.Add(text); }
                Logger.LogAlways($"Fluent asset load failed: {asset.File}; {error.GetType().Name}");
                return null;
            }
            finally
            {
                if (entered) { DecodeSlots.Release(); }
                lock (Gate) { if (--pending == 0) { idle.TrySetResult(); } }
            }
        }

        internal static Task WhenIdleAsync() { lock (Gate) { return pending == 0 ? Task.CompletedTask : idle.Task; } }
        internal static void Clear()
        {
            lock (Gate) { generation++; Cache.Clear(); Lru.Clear(); Failed.Clear(); }
        }
        private static long SumPixels()
        {
            long bytes = 0;
            foreach (var node in Cache.Values) { bytes += (long)node.Value.Image.PixelWidth * node.Value.Image.PixelHeight * 4; }
            return bytes;
        }
    }
}
