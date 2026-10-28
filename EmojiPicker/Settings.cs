using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EmojiPicker
{
    /// <summary>How the chosen emoji is delivered to the target app.</summary>
    internal enum EmojiInsertMode
    {
        /// <summary>Type simple emoji; paste joined ones (ZWJ/flag/skin-tone).</summary>
        Hybrid,

        /// <summary>Always paste via the clipboard (Ctrl+V).</summary>
        Paste,

        /// <summary>Always type via synthetic keystrokes; never touch the clipboard.</summary>
        Keystroke,
    }

    /// <summary>
    /// User settings persisted as JSON in the selected MED data directory,
    /// alongside recent.json and the debug log. Missing or unreadable settings fall
    /// back to defaults; a default file is written on first run so it is easy to find
    /// and edit by hand.
    /// </summary>
    internal sealed class Settings
    {
        private static string FilePath => AppDataPaths.FilePath("settings.json");

        public static Settings Current { get; private set; } = new Settings();

        /// <summary>
        /// How emoji are inserted: "hybrid" (default) types simple emoji and pastes
        /// joined ones that synthetic keystrokes split in some apps (ZWJ sequences,
        /// flags, skin-tone variants); "paste" always uses the clipboard; "keystroke"
        /// always types and never touches the clipboard.
        /// </summary>
        [JsonPropertyName("emojiInsertMode")]
        public string EmojiInsertMode { get; set; } = "hybrid";

        /// <summary>
        /// How long (ms) to wait after Ctrl+V before restoring the previous
        /// clipboard, when a joined emoji is pasted. The target reads the clipboard
        /// on its own schedule; a too-short wait can restore the old content before
        /// a slow/remote (RDP/Citrix) target has read the emoji, so it is
        /// configurable. Clamped to 50-5000 ms at use.
        /// </summary>
        [JsonPropertyName("pasteRestoreDelayMs")]
        public int PasteRestoreDelayMs { get; set; } = 250;

        [JsonPropertyName("showTrayIcon")]
        public bool ShowTrayIcon { get; set; } = false;

        internal static string? LoadWarning { get; private set; }
        private static bool writable = true;

        [JsonIgnore]
        public EmojiInsertMode InsertMode => EmojiInsertMode?.Trim().ToLowerInvariant() switch
        {
            "paste" => EmojiPicker.EmojiInsertMode.Paste,
            "keystroke" => EmojiPicker.EmojiInsertMode.Keystroke,
            _ => EmojiPicker.EmojiInsertMode.Hybrid,
        };

        /// <summary>Reads the settings file (writing defaults if absent). Call once at startup.</summary>
        public static void Load()
        {
            Current = new Settings();
            LoadWarning = null;
            writable = true;
            try
            {
                if (File.Exists(FilePath))
                {
                    var loaded = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath));
                    if (loaded != null)
                    {
                        Current = loaded;
                    }
                    else { throw new JsonException("Settings must be a JSON object"); }
                }
                else
                {
                    if (!Save()) { LoadWarning = $"Не удалось создать настройки в {FilePath}. Проверь права записи."; }
                }
            }
            catch (Exception ex)
            {
                Current = new Settings(); // any problem -> safe defaults
                writable = false;
                LoadWarning = $"Не удалось прочитать настройки: {FilePath}. Исходный файл не будет перезаписан. {ex.Message}";
            }

            Logger.Log($"Settings: emojiInsertMode={Current.InsertMode}");
        }

        internal static bool Save()
        {
            try
            {
                if (!writable) { return false; }
                AppDataPaths.WriteAtomic(FilePath,
                    JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
