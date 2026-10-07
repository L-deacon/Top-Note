using System.Windows;
using Top_Note.Models;
using Top_Note.ViewModels;

namespace Top_Note
{
    // Keeps exactly one window per saved note, so the same note is never edited in two places.
    public static class NoteWindowManager
    {
        private static readonly Dictionary<int, NoteWindow> OpenWindows = new();

        // Raised when a note window opens or closes, so the list can show which notes are open.
        public static event EventHandler? OpenWindowsChanged;

        public static bool IsOpen(int id) => OpenWindows.ContainsKey(id);

        public static bool TryGetViewModel(int id, out NoteWindowViewModel? vm)
        {
            vm = OpenWindows.TryGetValue(id, out var window) ? window.DataContext as NoteWindowViewModel : null;
            return vm != null;
        }

        public static void OpenNew()
        {
            var window = new NoteWindow();
            window.Show();
        }

        public static void Open(NoteModel note, bool activate = true)
        {
            if (note.Id > 0 && OpenWindows.TryGetValue(note.Id, out var existing))
            {
                existing.BringToFront();
                return;
            }

            var window = new NoteWindow(note) { ShowActivated = activate };
            if (note.Id > 0)
            {
                OpenWindows[note.Id] = window;
            }
            window.Show();
            OpenWindowsChanged?.Invoke(null, EventArgs.Empty);
        }

        // Called when a brand-new note gets its database Id on first save.
        public static void Register(NoteWindowViewModel vm)
        {
            var window = Application.Current.Windows.OfType<NoteWindow>().FirstOrDefault(w => w.DataContext == vm);
            if (window != null && vm.NoteId > 0)
            {
                OpenWindows[vm.NoteId] = window;
                OpenWindowsChanged?.Invoke(null, EventArgs.Empty);
            }
        }

        public static void Unregister(NoteWindow window)
        {
            foreach (var pair in OpenWindows.Where(p => p.Value == window).ToList())
            {
                OpenWindows.Remove(pair.Key);
            }
            OpenWindowsChanged?.Invoke(null, EventArgs.Empty);
        }

        // Close the window of a note that was deleted, without writing it back.
        public static void CloseDeleted(int id)
        {
            if (OpenWindows.TryGetValue(id, out var window))
            {
                // Save the last keystrokes first so Undo / Restore brings back the latest text.
                var vm = window.DataContext as NoteWindowViewModel;
                vm?.Flush();
                vm?.MarkDeleted();
                window.Close();
            }
        }

        // Close a note's window, saving it first.
        public static void CloseNote(int id)
        {
            if (OpenWindows.TryGetValue(id, out var window))
            {
                (window.DataContext as NoteWindowViewModel)?.Flush();
                window.Close();
            }
        }

        public static void ShowAll()
        {
            foreach (var window in Application.Current.Windows.OfType<NoteWindow>().ToList())
            {
                window.BringToFront();
            }

            var openIds = OpenWindows.Keys.ToHashSet();
            foreach (var note in SqliteDataAccess.LoadNotes().Where(n => !openIds.Contains(n.Id)))
            {
                Open(note);
            }
        }

        // Tidy the screen: close every note window (they're saved first). Pinned notes come back with Show all or on restart.
        public static void HideAll()
        {
            foreach (var window in Application.Current.Windows.OfType<NoteWindow>().ToList())
            {
                window.Close();
            }
        }

        public static void OpenPinnedNotes()
        {
            // Restored at startup without taking focus from whatever the user is doing.
            foreach (var note in SqliteDataAccess.LoadNotes().Where(n => n.IsPinned))
            {
                Open(note, activate: false);
            }
        }

        public static void SaveAll()
        {
            foreach (var window in Application.Current.Windows.OfType<NoteWindow>().ToList())
            {
                (window.DataContext as NoteWindowViewModel)?.Flush();
            }
        }
    }
}
