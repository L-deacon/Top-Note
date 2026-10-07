using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using Top_Note.Models;

namespace Top_Note.Services
{
    public enum ReminderKind { Soon, Now, Snoozed }

    // A timed, unticked task in today's daily note, e.g. "☐ 14:00 Call Troy" or "☐ 2:30pm–3pm Review".
    public record TimedTask(int NoteId, string Text, DateTime Start);

    // Watches today's daily task note and pops up a reminder 5 minutes before each timed task and when it's due.
    public static class ReminderService
    {
        public static readonly TimeSpan Lead = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan SnoozeFor = TimeSpan.FromMinutes(5);
        // How late a reminder may still fire (e.g. Top Note was just started, or the PC woke from sleep).
        private static readonly TimeSpan Grace = TimeSpan.FromMinutes(2);

        // Time at the start of the task text: "9:30", "09.30", "9:30am", "9am", optionally a range after it.
        private static readonly Regex TimePrefix = new(
            @"^(?<h>\d{1,2})(?:[:.](?<m>\d{2}))?\s*(?<ap>am|pm)?(?=\s|–|-|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly HashSet<string> Fired = new();
        private static readonly List<(TimedTask task, DateTime at)> Snoozed = new();
        private static readonly List<ReminderWindow> Open = new();
        private static DispatcherTimer? timer;

        public static void Start()
        {
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            timer.Tick += (_, _) => Check();
            timer.Start();
            Check();
        }

        public static bool IsEnabled => SettingsService.Load().RemindersEnabled;

        private static void Check()
        {
            var tasks = TodaysTimedTasks();
            CloseFinished(tasks);
            if (!IsEnabled) return;

            var now = DateTime.Now;
            foreach (var task in tasks)
            {
                if (now >= task.Start && now < task.Start + Grace)
                {
                    Fire(task, ReminderKind.Now);
                }
                else if (now >= task.Start - Lead && now < task.Start)
                {
                    Fire(task, ReminderKind.Soon);
                }
            }

            foreach (var snoozed in Snoozed.Where(s => now >= s.at).ToList())
            {
                Snoozed.Remove(snoozed);
                // Only if it's still an unticked task.
                if (tasks.Any(t => t.NoteId == snoozed.task.NoteId && t.Text == snoozed.task.Text))
                {
                    Show(snoozed.task, ReminderKind.Snoozed);
                }
            }
        }

        private static void Fire(TimedTask task, ReminderKind kind)
        {
            string key = $"{task.Start:yyyy-MM-dd}|{task.NoteId}|{task.Text}|{kind}";
            if (!Fired.Add(key)) return;
            // Starting the app at 09:31 for a 09:30 task fires "Now" only; skip the stale "in 5 minutes" too.
            if (kind == ReminderKind.Now) Fired.Add($"{task.Start:yyyy-MM-dd}|{task.NoteId}|{task.Text}|{ReminderKind.Soon}");
            Show(task, kind);
        }

        private static void Show(TimedTask task, ReminderKind kind)
        {
            // One popup per task: the "now" reminder replaces the "in 5 minutes" one.
            foreach (var old in Open.Where(w => w.Task.NoteId == task.NoteId && w.Task.Text == task.Text).ToList())
            {
                old.Close();
            }

            var window = new ReminderWindow(task, kind);
            window.Closed += (_, _) =>
            {
                Open.Remove(window);
                Arrange();
            };
            Open.Add(window);
            window.Show();
            Arrange();
            System.Media.SystemSounds.Exclamation.Play();
        }

        // Stack popups up from the bottom-right corner of the main screen, newest at the bottom.
        private static void Arrange()
        {
            var area = System.Windows.SystemParameters.WorkArea;
            double bottom = area.Bottom - 12;
            for (int i = Open.Count - 1; i >= 0; i--)
            {
                var w = Open[i];
                double height = w.ActualHeight > 0 ? w.ActualHeight : 150;
                w.Left = area.Right - w.Width - 12;
                w.Top = bottom - height;
                bottom = w.Top - 8;
            }
        }

        public static void Rearrange() => Arrange();

        // A task ticked or deleted in the note no longer needs its popup.
        private static void CloseFinished(List<TimedTask> tasks)
        {
            foreach (var w in Open.Where(w => !tasks.Any(t => t.NoteId == w.Task.NoteId && t.Text == w.Task.Text)).ToList())
            {
                w.Close();
            }
        }

        public static void Snooze(TimedTask task) => Snoozed.Add((task, DateTime.Now + SnoozeFor));

        // Tick the task in its note (through the open window if there is one, so the two never disagree).
        public static void MarkDone(TimedTask task)
        {
            string from = NoteText.Unchecked + task.Text, to = NoteText.Checked + task.Text;
            if (NoteWindowManager.TryGetViewModel(task.NoteId, out var vm) && vm != null)
            {
                vm.Content = ReplaceLine(vm.Content, from, to);
                vm.Flush();
            }
            else if (SqliteDataAccess.GetNote(task.NoteId) is { DeletedUtc: null } note)
            {
                note.Content = ReplaceLine(note.Content, from, to);
                note.ModifiedUtc = SqliteDataAccess.Now();
                SqliteDataAccess.UpdateNote(note);
            }
            NoteActions.RaiseChanged();
        }

        private static string ReplaceLine(string content, string from, string to)
        {
            var lines = content.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimEnd('\r') == from)
                {
                    lines[i] = to + (lines[i].EndsWith('\r') ? "\r" : "");
                    break;
                }
            }
            return string.Join('\n', lines);
        }

        private static List<TimedTask> TodaysTimedTasks()
        {
            var settings = SettingsService.Load();
            if (!DailyNoteService.IsTodaysNote(settings.DailyNoteId ?? 0)) return new();
            int id = settings.DailyNoteId!.Value;

            // An open note may have edits that haven't been written to the database yet.
            string? content = NoteWindowManager.TryGetViewModel(id, out var vm) && vm != null
                ? vm.Content
                : SqliteDataAccess.GetNote(id) is { DeletedUtc: null } note ? note.Content : null;
            if (content == null) return new();

            var tasks = new List<TimedTask>();
            foreach (var raw in NoteText.Lines(content))
            {
                var line = raw.TrimEnd();
                if (!line.StartsWith(NoteText.Unchecked, StringComparison.Ordinal)) continue;
                var text = line[NoteText.Unchecked.Length..];
                if (TryParseTime(text) is { } start) tasks.Add(new TimedTask(id, text, start));
            }
            return tasks;
        }

        private static DateTime? TryParseTime(string text)
        {
            var m = TimePrefix.Match(text);
            if (!m.Success) return null;
            bool hasMinutes = m.Groups["m"].Success, hasAmPm = m.Groups["ap"].Success;
            // A bare number ("3 boxes") isn't a time.
            if (!hasMinutes && !hasAmPm) return null;

            int hour = int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture);
            int minute = hasMinutes ? int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
            if (hasAmPm)
            {
                if (hour is < 1 or > 12) return null;
                bool pm = m.Groups["ap"].Value.Equals("pm", StringComparison.OrdinalIgnoreCase);
                hour = hour % 12 + (pm ? 12 : 0);
            }
            if (hour > 23 || minute > 59) return null;
            return DateTime.Today.AddHours(hour).AddMinutes(minute);
        }
    }
}
