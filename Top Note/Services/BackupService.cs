using System.Globalization;
using System.IO;
using System.Text;
using Top_Note.Models;

namespace Top_Note.Services
{
    public static class BackupService
    {
        private const int KeepBackups = 7;

        public static string BackupFolder => Path.Combine(SqliteDataAccess.DataFolder, "Backups");

        // One backup per day, keeping the newest week. Never lets a backup problem stop the app.
        public static void RunDailyBackup()
        {
            try
            {
                Directory.CreateDirectory(BackupFolder);
                var today = Path.Combine(BackupFolder, $"NotesDB-{DateTime.Now:yyyyMMdd}.db");
                if (!File.Exists(today))
                {
                    SqliteDataAccess.BackupTo(today);
                }

                foreach (var old in new DirectoryInfo(BackupFolder).GetFiles("NotesDB-*.db")
                             .OrderByDescending(f => f.Name).Skip(KeepBackups))
                {
                    old.Delete();
                }
            }
            catch (Exception)
            {
                // A missed backup is not worth interrupting the user for.
            }
        }

        public static void ExportMarkdown(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Top Note export");
            sb.AppendLine($"_{DateTime.Now:d MMMM yyyy HH:mm}_");
            sb.AppendLine();
            foreach (var note in SqliteDataAccess.LoadNotes())
            {
                sb.AppendLine($"## {NoteText.Title(note.Content)}");
                var stamp = Describe(note);
                if (stamp.Length > 0) sb.AppendLine($"_{stamp}_");
                sb.AppendLine();
                sb.AppendLine((note.Content ?? string.Empty).Trim());
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private static string Describe(NoteModel note)
        {
            var parts = new List<string>();
            if (TryParse(note.CreatedUtc) is { } created) parts.Add($"Created {created:d MMM yyyy HH:mm}");
            if (TryParse(note.ModifiedUtc) is { } edited) parts.Add($"Edited {edited:d MMM yyyy HH:mm}");
            if (note.IsPinned) parts.Add("Pinned");
            return string.Join(" · ", parts);
        }

        public static DateTime? TryParse(string? utc) =>
            DateTime.TryParse(utc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)
                ? value.ToLocalTime()
                : null;
    }
}
