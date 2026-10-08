using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;

namespace Top_Note.Services
{
    // Pushes reminders to the user's phone through ntfy (https://ntfy.sh): the ntfy app subscribes to a
    // private channel name, and Top Note posts to it. No account needed; the long random channel name
    // is what keeps it private, so it's generated here rather than chosen by the user.
    public static class PhoneNotifier
    {
        private const string Server = "https://ntfy.sh/";
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

        public static bool IsEnabled
        {
            get
            {
                var s = SettingsService.Load();
                return s.PhoneNotifyEnabled && !string.IsNullOrEmpty(s.PhoneNotifyTopic);
            }
        }

        // The channel name, created the first time it's needed.
        public static string Topic
        {
            get
            {
                var topic = SettingsService.Load().PhoneNotifyTopic;
                if (!string.IsNullOrEmpty(topic)) return topic;
                const string chars = "abcdefghijkmnpqrstuvwxyz23456789"; // no look-alikes (l/1, o/0) to mistype on a phone
                topic = "topnote-" + RandomNumberGenerator.GetString(chars, 16);
                SettingsService.Update(s => s.PhoneNotifyTopic = topic);
                return topic;
            }
        }

        public static void Remind(TimedTask task, ReminderKind kind)
        {
            if (!IsEnabled) return;
            string when = kind switch
            {
                ReminderKind.Now => "Due now",
                ReminderKind.Snoozed => "Snoozed reminder",
                _ => $"In {Math.Max(1, (int)Math.Ceiling((task.Start - DateTime.Now).TotalMinutes))} min",
            };
            string body = SettingsService.Load().PhoneShowTaskText ? task.Text : $"Task at {task.Start:HH:mm}";
            _ = SendAsync($"{when} · {task.Start:HH:mm}", body, kind == ReminderKind.Now ? 4 : 3);
        }

        public static void SetEnabled(bool enabled)
        {
            SettingsService.Update(s => s.PhoneNotifyEnabled = enabled);
            if (enabled) ShowSetup();
        }

        public static void ShowSetup()
        {
            var topic = Topic;
            ClipboardHelper.TrySetText(topic);
            System.Windows.MessageBox.Show(System.Windows.Application.Current.MainWindow!,
                "To get Top Note reminders on your iPhone:\n\n" +
                "1. Install \"ntfy\" from the App Store (free, no account needed).\n" +
                "2. Open it, tap +, and subscribe to this topic:\n\n" +
                $"        {topic}\n\n" +
                "    (It's been copied to your clipboard.) Leave the server as ntfy.sh.\n" +
                "3. Allow notifications when the app asks.\n" +
                "4. In Top Note, choose ··· → Phone notifications → Send a test notification.\n\n" +
                "Keep the topic name private: anyone who knows it can read your reminders. " +
                "Reminders are only sent while this PC is on and Top Note is running.",
                "Phone notifications", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }

        public static async void SendTest()
        {
            if (!await SendAsync("Top Note", "Test notification: phone reminders are working."))
            {
                System.Windows.MessageBox.Show(System.Windows.Application.Current.MainWindow!,
                    "Couldn't send the test notification. Check your internet connection.",
                    "Top Note", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        // Fire-and-forget: no internet just means no phone notification; the popup on the PC still shows.
        public static async Task<bool> SendAsync(string title, string message, int priority = 3)
        {
            try
            {
                // JSON publishing keeps non-ASCII text (–, ☐, names) intact, unlike HTTP headers.
                var response = await Http.PostAsJsonAsync(Server, new
                {
                    topic = Topic,
                    title,
                    message,
                    priority,
                    tags = new[] { "alarm_clock" },
                });
                return response.IsSuccessStatusCode;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
