using System.Collections.Generic;

namespace EmojiPicker
{
    internal sealed class FluentEmojiManifest
    {
        public int SchemaVersion { get; set; } = 1;
        public int PixelSize { get; set; } = 64;
        public int CatalogCount { get; set; }
        public string Generator { get; set; } = string.Empty;
        public string SourceIndexSha256 { get; set; } = string.Empty;
        public string SourceLicenseSha256 { get; set; } = string.Empty;
        public Dictionary<string, FluentEmojiAsset> Entries { get; set; } = new();
        public List<MissingFluentEmoji> Missing { get; set; } = new();
    }

    internal sealed class FluentEmojiAsset
    {
        public string File { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public long Bytes { get; set; }
        public string Sha256 { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string SourceSha256 { get; set; } = string.Empty;
        public string MetadataSha256 { get; set; } = string.Empty;
    }

    internal sealed record MissingFluentEmoji(string Text, string Name);
}
