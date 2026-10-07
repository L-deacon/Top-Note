using System.Windows;
using Microsoft.Win32;

namespace Top_Note.Services
{
    public static class ThemeManager
    {
        public const string System = "System";
        public const string Light = "ThemeLight";
        public const string Dark = "ThemeDark";

        private static bool listeningForSystemChanges;

        public static string Mode { get; private set; } = System;

        public static bool IsDark { get; private set; }

        public static event EventHandler? ThemeChanged;

        public static bool IsKnownTheme(string? themeName) => themeName is System or Light or Dark;

        public static void ApplyTheme(string? mode)
        {
            Mode = IsKnownTheme(mode) ? mode! : System;
            IsDark = Mode == Dark || (Mode == System && !WindowsUsesLightTheme());

            var dict = new ResourceDictionary
            {
                Source = new Uri($"Themes/{(IsDark ? Dark : Light)}.xaml", UriKind.Relative)
            };

            // Index 0 holds the colour tokens; index 1 (Controls.xaml) holds the styles and must stay.
            var merged = Application.Current.Resources.MergedDictionaries;
            if (merged.Count > 0)
            {
                merged[0] = dict;
            }
            else
            {
                merged.Add(dict);
            }

            if (!listeningForSystemChanges)
            {
                listeningForSystemChanges = true;
                SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            }

            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General || Mode != System) return;
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                bool dark = !WindowsUsesLightTheme();
                if (dark != IsDark) ApplyTheme(System);
            }));
        }

        private static bool WindowsUsesLightTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
            }
            catch (Exception)
            {
                return true;
            }
        }
    }
}
