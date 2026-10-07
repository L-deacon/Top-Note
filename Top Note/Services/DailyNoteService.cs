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
            if (AdoptExistingTodayNote()) return;
            await CreateAsync();
        }

        // Settings can lose track of today's note (e.g. an older Top Note rewrote settings.json);
        // recognise it by its "Tasks for <today>" first line rather than making a second one.
        private static bool AdoptExistingTodayNote()
        {
            var header = HeaderFor(DateTime.Today);
            var existing = SqliteDataAccess.LoadNotes()
                .FirstOrDefault(n => NoteText.Lines(n.Content).FirstOrDefault() == header);
            if (existing == null) return false;

            SettingsService.Update(s =>
            {
                s.DailyNoteDate = Today;
                s.DailyNoteId = existing.Id;
            });
            return true;
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
            if (AdoptExistingTodayNote() && SettingsService.Load().DailyNoteId is int adopted
                && SqliteDataAccess.GetNote(adopted) is { } found)
            {
                NoteWindowManager.Open(found);
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

        public static bool IsTodaysNote(int id)
        {
            var settings = SettingsService.Load();
            return id > 0 && settings.DailyNoteId == id && settings.DailyNoteDate == Today;
        }

        private const string NoMeetings = "No meetings today";
        private const string CalendarUnreadable = "Couldn't read the Outlook calendar";

        // A meeting line as written by this service: "☐ 09:30–09:45 Subject" or "☐ All day Subject".
        private static readonly System.Text.RegularExpressions.Regex MeetingLine =
            new(@"^[☐☑] (All day|\d{2}:\d{2}–\d{2}:\d{2}) ");

        // Re-reads today's meetings into an existing daily note. Meetings keep their ticks, new ones are
        // added in time order, unticked meetings that have gone from the calendar are dropped, and
        // anything the user typed is kept below. Null when Outlook can't be read (the note is left alone).
        public static async Task<string?> RefreshContentAsync(string current)
        {
            var events = await Task.Run(() => OutlookCalendar.GetEvents(DateTime.Today));
            if (events == null) return null;

            var lines = NoteText.Lines(current).ToList();
            string header = lines.Count > 0 && lines[0].StartsWith("Tasks for ", StringComparison.Ordinal)
                ? lines[0]
                : HeaderFor(DateTime.Today);

            var oldMeetings = new Dictionary<string, bool>();
            var userLines = new List<string>();
            foreach (var line in lines.Skip(lines.Count > 0 && lines[0] == header ? 1 : 0))
            {
                if (MeetingLine.IsMatch(line)) oldMeetings[line[2..]] = line.StartsWith(NoteText.Checked, StringComparison.Ordinal);
                else if (line.Trim() is not (NoMeetings or CalendarUnreadable)) userLines.Add(line);
            }

            var meetings = events.Select(e => (text: FormatEvent(e), allDay: e.AllDay)).ToList();
            // Ticked meetings stay even if they've left the calendar, so finished work isn't lost.
            foreach (var (text, done) in oldMeetings)
            {
                if (done && meetings.All(m => m.text != text)) meetings.Add((text, text.StartsWith("All day", StringComparison.Ordinal)));
            }

            var result = new StringBuilder(header).Append('\n');
            foreach (var (text, _) in meetings.OrderBy(m => !m.allDay).ThenBy(m => m.text, StringComparer.Ordinal))
            {
                bool done = oldMeetings.TryGetValue(text, out var d) && d;
                result.Append(done ? NoteText.Checked : NoteText.Unchecked).Append(text).Append('\n');
            }
            if (meetings.Count == 0) result.Append(NoMeetings).Append('\n');
            foreach (var line in userLines) result.Append(line).Append('\n');

            return result.ToString().TrimEnd('\n', '\r', ' ');
        }

        // Refresh a daily note from the notes list. Goes through the open window if there is one, so the
        // window and the database never disagree. False when Outlook can't be read.
        public static async Task<bool> RefreshNoteAsync(int id)
        {
            if (NoteWindowManager.TryGetViewModel(id, out var vm) && vm != null)
            {
                vm.Flush();
                var before = vm.Content;
                var updated = await RefreshContentAsync(before);
                if (updated == null) return false;
                // Skip if the user typed while Outlook was answering, rather than overwrite their edit.
                if (vm.Content == before && updated != before) vm.Content = updated;
                return true;
            }

            if (SqliteDataAccess.GetNote(id) is not { DeletedUtc: null } note) return true;
            var content = await RefreshContentAsync(note.Content);
            if (content == null) return false;
            if (content != note.Content)
            {
                note.Content = content;
                note.ModifiedUtc = SqliteDataAccess.Now();
                SqliteDataAccess.UpdateNote(note);
                NoteActions.RaiseChanged();
            }
            return true;
        }

        private static string HeaderFor(DateTime day) => "Tasks for " + day.ToString("dddd d MMMM", CultureInfo.CurrentCulture);

        private static string FormatEvent(CalendarEvent e)
        {
            var subject = e.Subject.Length > 0 ? e.Subject : "(no subject)";
            var when = e.AllDay ? "All day" : $"{e.Start:HH:mm}–{e.End:HH:mm}";
            return when + " " + subject;
        }

        private static string BuildContent(DateTime day, List<CalendarEvent>? events)
        {
            var text = new StringBuilder();
            text.Append(HeaderFor(day)).Append('\n');

            if (events == null)
            {
                text.Append(CalendarUnreadable).Append('\n');
            }
            else
            {
                foreach (var e in events)
                {
                    text.Append(NoteText.Unchecked).Append(FormatEvent(e)).Append('\n');
                }
                if (events.Count == 0) text.Append(NoMeetings).Append('\n');
            }

            return text.ToString().TrimEnd('\n');
        }
    }
}
