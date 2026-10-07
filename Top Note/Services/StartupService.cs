using System.IO;

namespace Top_Note.Services
{
    // "Start with Windows" for a ClickOnce app: copy the Start-menu .appref-ms shortcut into the
    // user's Startup folder. The exe path changes with every ClickOnce update, so it can't be used.
    public static class StartupService
    {
        private const string ShortcutName = "Top Note.appref-ms";

        private static string StartupShortcut =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), ShortcutName);

        private static string? StartMenuShortcut
        {
            get
            {
                try
                {
                    return Directory.EnumerateFiles(
                            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                            ShortcutName, SearchOption.AllDirectories)
                        .FirstOrDefault();
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        public static bool IsAvailable => StartMenuShortcut != null;

        public static bool IsEnabled => File.Exists(StartupShortcut);

        public static void SetEnabled(bool enabled)
        {
            if (enabled)
            {
                if (StartMenuShortcut is { } source)
                {
                    File.Copy(source, StartupShortcut, true);
                }
            }
            else if (File.Exists(StartupShortcut))
            {
                File.Delete(StartupShortcut);
            }
        }
    }
}
