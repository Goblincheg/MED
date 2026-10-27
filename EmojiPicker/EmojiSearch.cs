using System;
using System.Collections.Generic;
using System.Linq;

namespace EmojiPicker
{
    internal sealed class RussianAnnotation
    {
        public string Name { get; set; } = string.Empty;
        public string[] Keywords { get; set; } = Array.Empty<string>();
    }

    internal static class EmojiSearch
    {
        internal static string Normalize(string text) =>
            string.Join(" ", text.ToLowerInvariant().Replace('ё', 'е')
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        internal static List<Emoji> Find(IReadOnlyList<Emoji> emojis, string query)
        {
            var search = Normalize(query);
            if (search.Length == 0)
            {
                return new List<Emoji>();
            }

            var scored = new List<(Emoji Emoji, int Tier, int Score, bool IsName, int Index)>();
            for (var i = 0; i < emojis.Count; i++)
            {
                var emoji = emojis[i];
                var names = new[] { Normalize(emoji.Name), Normalize(emoji.RussianName) };
                var tags = emoji.RussianKeywords.Select(Normalize).ToArray();
                var englishTags = Normalize(emoji.Keywords);
                var exactName = names.Contains(search, StringComparer.Ordinal);
                var exactTag = tags.Contains(search, StringComparer.Ordinal);
                var nameStart = names.Any(name => HasWordStartMatch(name, search));
                var tagStart = HasWordStartMatch(englishTags, search) || tags.Any(tag => HasWordStartMatch(tag, search));
                var nameContains = names.Any(name => name.Contains(search, StringComparison.Ordinal));
                var tagContains = englishTags.Contains(search, StringComparison.Ordinal) || tags.Any(tag => tag.Contains(search, StringComparison.Ordinal));
                if (!nameContains && !tagContains)
                {
                    continue;
                }

                // Keep the original word-start/substring and popularity ordering;
                // only complete names and complete Russian aliases get a new tier.
                var tier = exactName || exactTag ? 0 : nameStart || tagStart ? 1 : 2;
                var isName = tier == 0 ? exactName : tier == 1 ? nameStart : nameContains;
                scored.Add((emoji, tier, emoji.Popularity + (isName ? 0 : 1), isName, i));
            }

            return scored.OrderBy(match => match.Tier)
                .ThenBy(match => match.Score)
                .ThenByDescending(match => match.IsName)
                .ThenBy(match => match.Index)
                .Select(match => match.Emoji).ToList();
        }

        private static bool HasWordStartMatch(string text, string query)
        {
            var index = text.IndexOf(query, StringComparison.Ordinal);
            while (index >= 0)
            {
                if (index == 0 || text[index - 1] == ' ' || text[index - 1] == '-')
                {
                    return true;
                }
                index = text.IndexOf(query, index + 1, StringComparison.Ordinal);
            }
            return false;
        }
    }
}
