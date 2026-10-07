using System.IO;
using System.Text.Json;

namespace Top_Note.Services
{
    public class AppSettings
    {
        public string Theme { get; set; } = ThemeManager.System;
        public string Sort { get; set; } = "Edited";
        public double? MainLeft { get; set; }
        public double? MainTop { get; set; }
        public double? MainWidth { get; set; }
        public double? MainHeight { get; set; }
        public bool DailyNoteEnabled { get; set; } = true;
        // yyyy-MM-dd of the last daily task note, and its note Id.
        public string? DailyNoteDate { get; set; }
        public int? DailyNoteId { get; set; }
    }

    public static class SettingsService
    {
        private static readonly string SettingsPath = Path.Combine(SqliteDataAccess.DataFolder, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
                if (!ThemeManager.IsKnownTheme(settings.Theme))
                {
                    settings.Theme = ThemeManager.System;
                }
                if (settings.Sort is not ("Edited" or "Created" or "Colour"))
                {
                    settings.Sort = "Edited";
                }
                return settings;
            }
            catch (Exception)
            {
                return new AppSettings();
            }
        }

        // Load, change, save, so one setting never wipes the others.
        public static void Update(Action<AppSettings> change)
        {
            var settings = Load();
            change(settings);
            Save(settings);
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                Directory.CreateDirectory(SqliteDataAccess.DataFolder);
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings));
            }
            catch (Exception)
            {
                // Settings are a convenience; failing to save them must never crash the app.
            }
        }
    }
}
