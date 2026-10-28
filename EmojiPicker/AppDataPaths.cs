using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace EmojiPicker
{
    internal static class AppDataPaths
    {
        internal const string PortableMarker = "portable.txt";
        internal static string DirectoryPath { get; private set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MED");
        internal static bool IsPortable { get; private set; }
        internal static string? Warning { get; private set; }
        internal static string FilePath(string name) => Path.Combine(DirectoryPath, name);

        // Tests supply isolated roots; production calls this once, before any data is read.
        internal static void Initialize(string baseDirectory, string roamingDirectory)
        {
            Warning = null;
            IsPortable = File.Exists(Path.Combine(baseDirectory, PortableMarker));
            var installed = Path.Combine(roamingDirectory, "MED");
            var preferred = IsPortable ? Path.Combine(baseDirectory, "Data") : installed;
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(baseDirectory).ToUpperInvariant())))[..16];
            var fallback = Path.Combine(installed, "Portable", key);
            // Continue using an existing fallback so returning write permissions cannot
            // silently resurrect older local settings/history. Moving data back is explicit.
            var priorFallback = IsPortable && File.Exists(Path.Combine(fallback, "portable-fallback.txt"));
            if (priorFallback || !CanWrite(preferred))
            {
                if (!IsPortable || !CanWrite(fallback))
                {
                    throw new IOException("Нет доступного для записи каталога данных MED. Исходные данные сохранены.");
                }
                DirectoryPath = fallback;
                CopyMissing(preferred, fallback, ["settings.json", "recent.json", "debug.enabled"]);
                System.IO.File.WriteAllText(Path.Combine(fallback, "portable-fallback.txt"), baseDirectory);
                Warning = $"Portable-каталог Data недоступен для записи или ранее использовался резервный каталог. " +
                    $"Данные сохраняются здесь: {fallback}\nДля переноса скопируй эти данные в Data после выхода из MED. " +
                    "Чтобы вернуться к Data, переименуй portable-fallback.txt в резервном каталоге.";
            }
            else { DirectoryPath = preferred; }

            var migrationMarker = FilePath("legacy-imported.txt");
            if (!File.Exists(migrationMarker))
            {
                CopyMissing(Path.Combine(roamingDirectory, "ClassicEmojiPicker"), DirectoryPath,
                    ["settings.json", "recent.json", "debug.enabled"]);
                WriteAtomic(migrationMarker, "Existing files preserved; original ClassicEmojiPicker data not deleted.");
            }
        }

        private static bool CanWrite(string directory)
        {
            try
            {
                Directory.CreateDirectory(directory);
                var probe = Path.Combine(directory, ".write-test-" + Guid.NewGuid().ToString("N"));
                using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
                return true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
        }

        private static void CopyMissing(string source, string destination, string[] files)
        {
            foreach (var name in files)
            {
                var from = Path.Combine(source, name);
                var to = Path.Combine(destination, name);
                if (File.Exists(from) && !File.Exists(to))
                {
                    // Fail visibly on unreadable user data instead of replacing it with defaults.
                    var temporary = to + ".import-" + Guid.NewGuid().ToString("N");
                    File.Copy(from, temporary, overwrite: false);
                    File.Move(temporary, to, overwrite: false);
                }
            }
        }

        internal static void WriteAtomic(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, text);
            if (File.Exists(path)) { File.Replace(temporary, path, path + ".bak"); }
            else { File.Move(temporary, path); }
        }
    }
}
