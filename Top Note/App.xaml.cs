using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Top_Note.Services;
using Forms = System.Windows.Forms;

namespace Top_Note
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private const int HotkeyId = 0x544E; // "TN"
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_NOREPEAT = 0x4000;
        private const uint VK_N = 0x4E;

        private Forms.NotifyIcon? tray;
        private HwndSource? hotkeyWindow;
        private Mutex? singleInstance;
        private EventWaitHandle? showSignal;
        private const string ShowSignalName = @"Local\TopNote.ShowMainWindow";

        public static bool IsExiting { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Only one copy runs; a second launch just shows the existing one.
            singleInstance = new Mutex(true, @"Local\TopNote.SingleInstance", out bool isFirst);
            if (!isFirst)
            {
                singleInstance = null;
                if (EventWaitHandle.TryOpenExisting(ShowSignalName, out var signal))
                {
                    // This process was just launched so it may take the foreground; let the running copy have it.
                    AllowSetForegroundWindow(ASFW_ANY);
                    signal.Set();
                }
                Shutdown();
                return;
            }
            ListenForSecondLaunch();

            try
            {
                SqliteDataAccess.EnsureDatabase();
                BackupService.RunDailyBackup();
                ThemeManager.ApplyTheme(SettingsService.Load().Theme);

                MainWindow = new MainWindow();
                MainWindow.Show();

                CreateTrayIcon();
                RegisterNewNoteHotkey();
            }
            catch (Exception ex)
            {
                // Without this the process would linger invisibly, holding the single-instance lock.
                MessageBox.Show(ex.Message, "Top Note could not start", MessageBoxButton.OK, MessageBoxImage.Error);
                IsExiting = true;
                Shutdown(1);
                return;
            }

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            NoteWindowManager.OpenPinnedNotes();
            DailyNoteService.Start();
            ReminderService.Start();
        }

        private void ListenForSecondLaunch()
        {
            showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
            var listener = new Thread(() =>
            {
                while (showSignal.WaitOne())
                {
                    if (IsExiting) return;
                    Dispatcher.BeginInvoke(new Action(ShowMainWindow));
                }
            })
            { IsBackground = true };
            listener.Start();
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(e.Exception.Message, "Top Note error", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        }

        #region Tray icon

        private void CreateTrayIcon()
        {
            var menu = new Forms.ContextMenuStrip
            {
                Font = new System.Drawing.Font("Segoe UI", 9f),
                ShowImageMargin = false,
                ShowCheckMargin = true,
                Renderer = new TrayMenuRenderer(),
            };
            // Rebuilt every time it opens so recent notes and the theme are current.
            menu.Opening += (_, _) => BuildTrayMenu(menu);
            BuildTrayMenu(menu);
            ThemeManager.ThemeChanged += (_, _) => menu.Renderer = new TrayMenuRenderer();

            tray = new Forms.NotifyIcon
            {
                Icon = LoadTrayIcon(),
                Text = "Top Note — Ctrl+Alt+N for a new note",
                Visible = true,
                ContextMenuStrip = menu
            };
            tray.MouseClick += (_, args) =>
            {
                if (args.Button == Forms.MouseButtons.Left) ShowMainWindow();
            };
        }

        private void BuildTrayMenu(Forms.ContextMenuStrip menu)
        {
            menu.Items.Clear();
            Forms.ToolStripMenuItem Item(string text, Action onClick)
            {
                var item = new Forms.ToolStripMenuItem(text) { Padding = new Forms.Padding(4, 3, 4, 3) };
                item.Click += (_, _) => onClick();
                return item;
            }

            try
            {
                var recent = SqliteDataAccess.LoadNotes().Take(6).ToList();
                if (recent.Count > 0)
                {
                    menu.Items.Add(new Forms.ToolStripLabel("Recent notes") { ForeColor = System.Drawing.Color.Gray });
                    foreach (var note in recent)
                    {
                        var title = Models.NoteText.Title(note.Content);
                        if (title.Length > 40) title = title[..40] + "…";
                        menu.Items.Add(Item((note.IsPinned ? "📌 " : "") + title, () => NoteWindowManager.Open(note)));
                    }
                    menu.Items.Add(new Forms.ToolStripSeparator());
                }
            }
            catch (Exception)
            {
                // The menu still works without the recent list.
            }

            menu.Items.Add(Item("New note	Ctrl+Alt+N", NoteWindowManager.OpenNew));
            menu.Items.Add(Item("Today's tasks", () => _ = DailyNoteService.OpenTodayAsync()));
            menu.Items.Add(Item("Show notes list", ShowMainWindow));
            menu.Items.Add(Item("Show all notes", NoteWindowManager.ShowAll));
            menu.Items.Add(Item("Hide all notes", NoteWindowManager.HideAll));
            menu.Items.Add(new Forms.ToolStripSeparator());

            var startWithWindows = new Forms.ToolStripMenuItem("Start with Windows")
            {
                Padding = new Forms.Padding(4, 3, 4, 3),
                CheckOnClick = true,
                Checked = StartupService.IsEnabled,
                Enabled = StartupService.IsAvailable || StartupService.IsEnabled,
                ToolTipText = StartupService.IsAvailable ? null : "Available once Top Note is installed"
            };
            startWithWindows.CheckedChanged += (_, _) => StartupService.SetEnabled(startWithWindows.Checked);
            menu.Items.Add(startWithWindows);

            var dailyNote = new Forms.ToolStripMenuItem("Daily task note from Outlook")
            {
                Padding = new Forms.Padding(4, 3, 4, 3),
                CheckOnClick = true,
                Checked = SettingsService.Load().DailyNoteEnabled,
                ToolTipText = "Each day, open a pinned checklist of today's Outlook meetings"
            };
            dailyNote.CheckedChanged += (_, _) =>
            {
                SettingsService.Update(s => s.DailyNoteEnabled = dailyNote.Checked);
                if (dailyNote.Checked) _ = DailyNoteService.EnsureTodayAsync();
            };
            menu.Items.Add(dailyNote);

            var reminders = new Forms.ToolStripMenuItem("Task reminders")
            {
                Padding = new Forms.Padding(4, 3, 4, 3),
                CheckOnClick = true,
                Checked = ReminderService.IsEnabled,
                ToolTipText = "Pop up 5 minutes before, and at the time of, timed tasks in today's task note"
            };
            reminders.CheckedChanged += (_, _) => SettingsService.Update(s => s.RemindersEnabled = reminders.Checked);
            menu.Items.Add(reminders);

            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(Item("Exit", ExitApp));
        }

        private static System.Drawing.Icon LoadTrayIcon()
        {
            try
            {
                if (Environment.ProcessPath is { } exe)
                {
                    return System.Drawing.Icon.ExtractAssociatedIcon(exe) ?? System.Drawing.SystemIcons.Application;
                }
            }
            catch (Exception)
            {
                // Fall back to the default icon below.
            }
            return System.Drawing.SystemIcons.Application;
        }

        public void ShowMainWindow()
        {
            if (MainWindow == null) return;
            MainWindow.Show();
            if (MainWindow.WindowState == WindowState.Minimized)
            {
                MainWindow.WindowState = WindowState.Normal;
            }
            MainWindow.Activate();
        }

        public void ExitApp()
        {
            IsExiting = true;
            (MainWindow as MainWindow)?.SaveBounds();
            NoteWindowManager.SaveAll();
            Shutdown();
        }

        #endregion

        #region Global hotkey (Ctrl+Alt+N = new note from anywhere)

        private void RegisterNewNoteHotkey()
        {
            var parameters = new HwndSourceParameters("TopNoteHotkey") { Width = 0, Height = 0, WindowStyle = 0 };
            hotkeyWindow = new HwndSource(parameters);
            hotkeyWindow.AddHook(HotkeyHook);
            // If another app already owns the shortcut this fails quietly; the tray menu still works.
            RegisterHotKey(hotkeyWindow.Handle, HotkeyId, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_N);
        }

        private IntPtr HotkeyHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
            {
                NoteWindowManager.OpenNew();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private const int ASFW_ANY = -1;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int dwProcessId);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        #endregion

        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            // Signing out / shutting down: make sure the last few keystrokes are saved.
            NoteWindowManager.SaveAll();
            base.OnSessionEnding(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (hotkeyWindow != null)
            {
                UnregisterHotKey(hotkeyWindow.Handle, HotkeyId);
                hotkeyWindow.Dispose();
            }
            if (tray != null)
            {
                tray.Visible = false;
                tray.Dispose();
            }
            singleInstance?.ReleaseMutex();
            base.OnExit(e);
        }
    }
}
