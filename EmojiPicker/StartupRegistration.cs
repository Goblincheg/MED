using System;
using System.IO;
using Microsoft.Win32;

namespace EmojiPicker
{
    internal interface IStartupStore
    {
        string? Read();
        void Write(string command);
        void Delete();
    }

    internal sealed class RegistryStartupStore : IStartupStore
    {
        private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public string? Read() { using var key = Registry.CurrentUser.OpenSubKey(KeyPath); return key?.GetValue("MED") as string; }
        public void Write(string command) { using var key = Registry.CurrentUser.CreateSubKey(KeyPath); key.SetValue("MED", command); }
        public void Delete() { using var key = Registry.CurrentUser.OpenSubKey(KeyPath, true); key?.DeleteValue("MED", false); }
    }

    internal sealed class StartupRegistration(IStartupStore store, string executable)
    {
        internal string Command => BuildCommand(executable);
        internal static string BuildCommand(string path) => $"\"{Path.GetFullPath(path)}\" --background";
        internal bool Enabled => string.Equals(store.Read(), Command, StringComparison.OrdinalIgnoreCase);
        internal string Status
        {
            get
            {
                var registered = store.Read();
                return registered == null ? "Автозагрузка выключена." : Enabled ? "Автозагрузка указывает на эту копию MED." :
                    "Автозагрузка указывает на другую или перемещённую копию MED. Включи её здесь, чтобы обновить путь; кнопка удаления убирает старую запись.";
            }
        }
        internal void Enable() => store.Write(Command);
        internal void DisableOwned() { if (Enabled) { store.Delete(); } }
        internal void RemoveRegistration() => store.Delete();
    }
}
