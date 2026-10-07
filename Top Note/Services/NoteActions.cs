using System.Globalization;
using System.Windows;

namespace Top_Note.Services
{
    // Things that can be done to a note from either the list or the note itself.
    public static class NoteActions
    {
        // id, and whether the note's window was open (so Undo can reopen it).
        public static event Action<int, bool>? MovedToTrash;

        public static event EventHandler? Changed;

        public static void MoveToTrash(int id)
        {
            bool wasOpen = NoteWindowManager.IsOpen(id);
            NoteWindowManager.CloseDeleted(id);
            SqliteDataAccess.SoftDelete(id);
            MovedToTrash?.Invoke(id, wasOpen);
            Changed?.Invoke(null, EventArgs.Empty);
        }

        public static void Restore(int id, bool reopen)
        {
            SqliteDataAccess.Restore(id);
            if (reopen && SqliteDataAccess.GetNote(id) is { } note)
            {
                NoteWindowManager.Open(note);
            }
            Changed?.Invoke(null, EventArgs.Empty);
        }

        public static void RaiseChanged() => Changed?.Invoke(null, EventArgs.Empty);
    }

    public static class ClipboardHelper
    {
        public static void TrySetText(string? text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                Clipboard.SetText(text);
            }
            catch (Exception)
            {
                // Another app has the clipboard open; nothing useful to do.
            }
        }
    }

    public static class RelativeTime
    {
        public static string Format(DateTime local)
        {
            var span = DateTime.Now - local;
            if (span.TotalMinutes < 1) return "just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} min ago";
            if (span.TotalHours < 24 && local.Date == DateTime.Today) return $"{(int)span.TotalHours}h ago";
            if (local.Date == DateTime.Today.AddDays(-1)) return "yesterday";
            if (span.TotalDays < 7) return local.ToString("dddd", CultureInfo.CurrentCulture);
            return local.ToString(local.Year == DateTime.Now.Year ? "d MMM" : "d MMM yyyy", CultureInfo.CurrentCulture);
        }
    }
}
