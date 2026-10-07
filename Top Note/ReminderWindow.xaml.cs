using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Top_Note.Services;
using static Top_Note.NativeMethods;

namespace Top_Note
{
    // A reminder that stays on top of every window until the user acts on it.
    public partial class ReminderWindow : Window
    {
        private readonly DispatcherTimer topmostTimer;
        private IntPtr hwnd;

        public TimedTask Task { get; }

        public ReminderWindow(TimedTask task, ReminderKind kind)
        {
            InitializeComponent();
            Task = task;
            TaskText.Text = task.Text;
            WhenText.Text = kind switch
            {
                ReminderKind.Now => $"Due now · {task.Start:HH:mm}",
                ReminderKind.Snoozed => $"Snoozed reminder · {task.Start:HH:mm}",
                _ => $"In {Math.Max(1, (int)Math.Ceiling((task.Start - DateTime.Now).TotalMinutes))} min · {task.Start:HH:mm}",
            };
            if (kind == ReminderKind.Now) Strip.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "Danger");

            // Like pinned notes: win back the top spot if the taskbar or another always-on-top app takes it.
            topmostTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            topmostTimer.Tick += (_, _) =>
            {
                if (hwnd != IntPtr.Zero && !IsActive && (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TOPMOST) == 0)
                {
                    SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
                }
            };
            SizeChanged += (_, _) => ReminderService.Rearrange();
            Closed += (_, _) => topmostTimer.Stop();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            hwnd = new WindowInteropHelper(this).Handle;
            topmostTimer.Start();
        }

        private void Done_Click(object sender, RoutedEventArgs e)
        {
            ReminderService.MarkDone(Task);
            Close();
        }

        private void Snooze_Click(object sender, RoutedEventArgs e)
        {
            ReminderService.Snooze(Task);
            Close();
        }

        private void Dismiss_Click(object sender, RoutedEventArgs e) => Close();
    }
}
