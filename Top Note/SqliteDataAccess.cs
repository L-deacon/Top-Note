using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using Dapper;
using Top_Note.Models;

namespace Top_Note
{
    public class SqliteDataAccess
    {
        // Notes live in a fixed per-user folder so ClickOnce updates (which install each
        // version into a new folder) and the working directory can never lose them.
        // TOPNOTE_DATA_DIR lets a test run use a throwaway folder instead of the real notes.
        public static readonly string DataFolder =
            Environment.GetEnvironmentVariable("TOPNOTE_DATA_DIR") is { Length: > 0 } overrideDir
                ? overrideDir
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TopNote");

        public static readonly string DbPath = Path.Combine(DataFolder, "NotesDB.db");

        // Notes in the Trash are removed for good after this long.
        public static readonly TimeSpan TrashRetention = TimeSpan.FromDays(30);

        public static string Now() => DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

        public static void EnsureDatabase()
        {
            Directory.CreateDirectory(DataFolder);

            if (!File.Exists(DbPath))
            {
                var legacy = FindLegacyDatabase();
                if (legacy != null)
                {
                    File.Copy(legacy, DbPath);
                }
            }

            using (IDbConnection cnn = new SQLiteConnection(LoadConnectionString()))
            {
                cnn.Execute(@"CREATE TABLE IF NOT EXISTS Notes (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Content TEXT,
                    IsPinned INTEGER NOT NULL DEFAULT 0,
                    Color TEXT NOT NULL DEFAULT '#FFF7D1',
                    Left REAL,
                    Top REAL,
                    FontSize INTEGER DEFAULT 15,
                    Width INTEGER,
                    Height INTEGER)");

                // Columns added in later versions. Older databases get them on first run; nothing is renamed or dropped.
                var columns = cnn.Query<string>("SELECT name FROM pragma_table_info('Notes')")
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (!columns.Contains("CreatedUtc")) cnn.Execute("ALTER TABLE Notes ADD COLUMN CreatedUtc TEXT NULL");
                if (!columns.Contains("ModifiedUtc")) cnn.Execute("ALTER TABLE Notes ADD COLUMN ModifiedUtc TEXT NULL");
                if (!columns.Contains("DeletedUtc")) cnn.Execute("ALTER TABLE Notes ADD COLUMN DeletedUtc TEXT NULL");
                if (!columns.Contains("IsCollapsed")) cnn.Execute("ALTER TABLE Notes ADD COLUMN IsCollapsed INTEGER NOT NULL DEFAULT 0");

                var cutoff = (DateTime.UtcNow - TrashRetention).ToString("o", CultureInfo.InvariantCulture);
                cnn.Execute("DELETE FROM Notes WHERE DeletedUtc IS NOT NULL AND DeletedUtc < @cutoff", new { cutoff });
            }
        }

        // Older versions kept NotesDB.db next to the exe. Pick up the most recently used copy:
        // the current exe folder or any previous ClickOnce install folder.
        private static string? FindLegacyDatabase()
        {
            var candidates = new List<FileInfo>();

            var local = new FileInfo(Path.Combine(AppContext.BaseDirectory, "NotesDB.db"));
            if (local.Exists) candidates.Add(local);

            try
            {
                var clickOnceRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Apps", "2.0");
                if (Directory.Exists(clickOnceRoot))
                {
                    foreach (var path in Directory.EnumerateFiles(clickOnceRoot, "NotesDB.db", SearchOption.AllDirectories))
                    {
                        candidates.Add(new FileInfo(path));
                    }
                }
            }
            catch (Exception)
            {
                // Inaccessible folders just mean nothing to migrate from there.
            }

            return candidates
                .Where(HasNotesTable)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault()?.FullName;
        }

        private static bool HasNotesTable(FileInfo file)
        {
            try
            {
                using (IDbConnection cnn = new SQLiteConnection($"Data Source={file.FullName};Read Only=True;"))
                {
                    return cnn.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Notes'") > 0;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static List<NoteModel> LoadNotes()
        {
            using (IDbConnection cnn = new SQLiteConnection(LoadConnectionString()))
            {
                var output = cnn.Query<NoteModel>(
                    "SELECT * FROM Notes WHERE DeletedUtc IS NULL ORDER BY IsPinned DESC, COALESCE(ModifiedUtc,'') DESC, Id DESC");
                return output.ToList();
            }
        }

        public static List<NoteModel> LoadDeleted()
        {
            using (IDbConnection cnn = new SQLiteConnection(LoadConnectionString()))
            {
                return cnn.Query<NoteModel>("SELECT * FROM Notes WHERE DeletedUtc IS NOT NULL ORDER BY DeletedUtc DESC").ToList();
            }
        }

        public static NoteModel? GetNote(int id)
        {
            using (IDbConnection cnn = new SQLiteConnection(LoadConnectionString()))
            {
                return cnn.QueryFirstOrDefault<NoteModel>("SELECT * FROM Notes WHERE Id = @id", new { id });
            }
        }

        public static int SaveNote(NoteModel note)
        {
            note.CreatedUtc ??= Now();
            note.ModifiedUtc ??= note.CreatedUtc;
            using (IDbConnection cnn = new SQLiteConnection(LoadConnectionString()))
            {
                return cnn.ExecuteScalar<int>(@"INSERT into Notes (Content, IsPinned, Color, Left, Top, FontSize, Width, Height, IsCollapsed, CreatedUtc, ModifiedUtc)
                    values (@Content, @IsPinned, @Color, @Left, @Top, @FontSize, @Width, @Height, @IsCollapsed, @CreatedUtc, @ModifiedUtc);
                    SELECT last_insert_rowid();", note);
            }
        }

        // ModifiedUtc = null keeps the stored "edited" time (moves and resizes aren't edits).
        public static void UpdateNote(NoteModel note)
        {
            using (IDbConnection cnn = new SQLiteConnection(LoadConnectionString()))
            {
                cnn.Execute(@"UPDATE Notes SET Content = @Content, IsPinned = @IsPinned, Color = @Color, Left = @Left, Top = @Top,
                    FontSize = @FontSize, Width = @Width, Height = @Height, IsCollapsed = @IsCollapsed,
                    ModifiedUtc = COALESCE(@ModifiedUtc, ModifiedUtc)
                    WHERE Id = @Id", note);
            }
        }

        public static void SetPinned(int id, bool pinned)
        {
            using (IDbConnection cnn = new SQLiteConnection(LoadConnectionString()))
            {
                cnn.Execute("UPDATE Notes SET IsPinned = @pinned, ModifiedUtc = @now WHERE Id = @id", new { id, pinned, now = Now() });
            }
        }

        public static void SetColor(int id, string color)
        {
            using (IDbConnection cnn = new SQLiteConnection(LoadConnectionString()))
            {
                cnn.Execute("UPDATE Notes SET Color = @color, ModifiedUtc = @now WHERE Id = @id", new { id, color, now = Now() });
            }
        }

        public static int DuplicateNote(NoteModel note)
        {
            var copy = new NoteModel
            {
                Content = note.Content,
                Color = note.Color,
                Left = note.Left + 24,
                Top = note.Top + 24,
                FontSize = note.FontSize,
                Width = note.Width,
                Height = note.Height,
                IsPinned = false,
            };
            return SaveNote(copy);
        }

        // Moves a note to the Trash. It can be restored for 30 days.
        public static void SoftDelete(int id)
        {
            using (IDbConnection cnn = new SQLiteConnection(LoadConnectionString()))
            {
                cnn.Execute("UPDATE Notes SET DeletedUtc = @now WHERE Id = @id", new { id, now = Now() });
            }
        }

        public static void Restore(int id)
        {
            using (IDbConnection cnn = new SQLiteConnection(LoadConnectionString()))
            {
                cnn.Execute("UPDATE Notes SET DeletedUtc = NULL WHERE Id = @id", new { id });
            }
        }

        public static void DeleteNote(int id)
        {
            using (IDbConnection cnn = new SQLiteConnection(LoadConnectionString()))
            {
                cnn.Execute("DELETE FROM Notes WHERE Id = @Id", new { Id = id });
            }
        }

        public static void EmptyTrash()
        {
            using (IDbConnection cnn = new SQLiteConnection(LoadConnectionString()))
            {
                cnn.Execute("DELETE FROM Notes WHERE DeletedUtc IS NOT NULL");
            }
        }

        // Online copy of the database, safe while it's in use.
        public static void BackupTo(string path)
        {
            using var source = new SQLiteConnection(LoadConnectionString());
            using var destination = new SQLiteConnection($"Data Source={path};Version=3;");
            source.Open();
            destination.Open();
            source.BackupDatabase(destination, "main", "main", -1, null, 0);
        }

        private static string LoadConnectionString() => $"Data Source={DbPath};Version=3;";
    }
}
