using System;
using System.Windows;
using System.Windows.Input;

namespace EmojiPicker
{
    public partial class SettingsWindow : Window
    {
        private readonly StartupRegistration startup;
        private readonly Action<bool> changeTray;
        private readonly Action exit;

        internal SettingsWindow(StartupRegistration startup, Action<bool> changeTray, Action exit)
        {
            this.startup = startup;
            this.changeTray = changeTray;
            this.exit = exit;
            InitializeComponent();
            TrayToggle.IsChecked = Settings.Current.ShowTrayIcon;
            LoggingToggle.IsChecked = Logger.Enabled;
            DataDirectory.Text = AppDataPaths.DirectoryPath;
            RefreshStartup();
        }

        private void RefreshStartup()
        {
            try { StartupToggle.IsChecked = startup.Enabled; StartupStatus.Text = startup.Status; }
            catch (Exception error) { StartupStatus.Text = "Не удалось прочитать автозагрузку: " + error.Message; }
        }

        private void TrayToggle_Click(object sender, RoutedEventArgs e)
        {
            var previous = Settings.Current.ShowTrayIcon;
            Settings.Current.ShowTrayIcon = TrayToggle.IsChecked == true;
            if (Settings.Save()) { changeTray(Settings.Current.ShowTrayIcon); }
            else
            {
                Settings.Current.ShowTrayIcon = previous;
                TrayToggle.IsChecked = previous;
                MessageBox.Show(this, "Не удалось сохранить настройку. Проверь права записи и файл settings.json. Исходные данные сохранены.", "MED");
            }
        }

        private void StartupToggle_Click(object sender, RoutedEventArgs e)
        {
            try { if (StartupToggle.IsChecked == true) { startup.Enable(); } else { startup.DisableOwned(); } }
            catch (Exception error) { MessageBox.Show(this, "Не удалось изменить автозагрузку: " + error.Message, "MED"); }
            RefreshStartup();
        }

        private void RemoveStartup_Click(object sender, RoutedEventArgs e)
        {
            try { startup.RemoveRegistration(); }
            catch (Exception error) { MessageBox.Show(this, "Не удалось удалить запись: " + error.Message, "MED"); }
            RefreshStartup();
        }

        private void LoggingToggle_Click(object sender, RoutedEventArgs e) => LoggingToggle.IsChecked = Logger.Toggle();
        private void Exit_Click(object sender, RoutedEventArgs e) => exit();
        private void Close_Click(object sender, RoutedEventArgs e) => Close();
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { e.Handled = true; Close(); } }
    }
}
