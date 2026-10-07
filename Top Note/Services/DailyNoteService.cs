using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Top_Note.Models;

namespace Top_Note.Services
{
    // Creates one task note per day, listing that day's Outlook meetings as a checklist,
    // and opens it pinned on top. Yesterday's daily note is unpinned and closed (not deleted).
    public static class DailyNoteService
    {
        private static DispatcherTimer? timer;
        private static bool running;

        public static void Start()
        {
            _ = EnsureTodayAsync();
            // Catches the day changing while Top Note stays open overnight.
            timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
            timer.Tick += (_, _) => _ = EnsureTodayAsync();
            timer.Start();
        }

        private static string Today => DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        public static async Task EnsureTodayAsync()
        {
            var settings = SettingsService.Load();
            if (!settings.DailyNoteEnabled || settings.DailyNoteDate == Today) return;
            await CreateAsync();
        }

        // Tray "Today's tasks": open today's note, creating it if there isn't one.
        public static async Task OpenTodayAsync()
        {
            var settings = SettingsService.Load();
            if (settings.DailyNoteDate == Today && settings.DailyNoteId is int id
                && SqliteDataAccess.GetNote(id) is { DeletedUtc: null } note)
            {
                NoteWindowManager.Open(note);
                return;
            }
            await CreateAsync(activate: true);
        }

        private static async Task CreateAsync(bool activate = false)
        {
            if (running) return;
            running = true;
            try
            {
                var day = DateTime.Today;
                // Outlook can take a few seconds to answer (or to start); keep the UI responsive.
                var events = await Task.Run(() => OutlookCalendar.GetEvents(day));

                var settings = SettingsService.Load();
                var previous = settings.DailyNoteId is int oldId ? SqliteDataAccess.GetNote(oldId) : null;
                var area = SystemParameters.WorkArea;

                var note = new NoteModel
                {
                    Content = BuildContent(day, events),
                    IsPinned = true,
                    Color = NotePalette.All.First(c => c.Key == "blue").BodyHex,
                    // Take yesterday's spot so the daily note always appears in the same place.
                    Width = previous?.Width > 0 ? previous.Width : 300,
                    Height = previous?.Height > 0 ? previous.Height : 340,
                    Left = previous?.Left ?? area.Right - 320,
                    Top = previous?.Top ?? area.Top + 20,
                };
                note.Id = SqliteDataAccess.SaveNote(note);

                if (previous is { DeletedUtc: null, IsPinned: true })
                {
                    NoteWindowManager.CloseNote(previous.Id);
                    SqliteDataAccess.SetPinned(previous.Id, false);
                }

                SettingsService.Update(s =>
                {
                    s.DailyNoteDate = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    s.DailyNoteId = note.Id;
                });

                NoteWindowManager.Open(note, activate);
                NoteActions.RaiseChanged();
            }
            finally
            {
                running = false;
            }
        }

        private static string BuildContent(DateTime day, List<CalendarEvent>? events)
        {
            var text = new StringBuilder();
            text.Append("Tasks for ").Append(day.ToString("dddd d MMMM", CultureInfo.CurrentCulture)).Append('\n');

            if (events == null)
            {
                text.Append("Couldn't read the Outlook calendar\n");
            }
            else
            {
                foreach (var e in events)
                {
                    var subject = e.Subject.Length > 0 ? e.Subject : "(no subject)";
                    var when = e.AllDay ? "All day" : $"{e.Start:HH:mm}–{e.End:HH:mm}";
                    text.Append(NoteText.Unchecked).Append(when).Append(' ').Append(subject).Append('\n');
                }
                if (events.Count == 0) text.Append("No meetings today\n");
            }

            return text.ToString().TrimEnd('\n');
        }
    }
}
