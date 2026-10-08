using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Top_Note.Models;
using Top_Note.Services;
using Top_Note.ViewModels;

namespace Top_Note
{
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel vm;

        public MainWindow()
        {
            InitializeComponent();
            vm = new MainWindowViewModel();
            vm.FocusSearchRequested += (_, _) =>
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
            };
            vm.PropertyChanged += OnViewModelPropertyChanged;
            DataContext = vm;

            RestoreBounds();
            ThemeManager.ThemeChanged += (_, _) => ApplyTitleBarTheme();
            PreviewKeyDown += Window_PreviewKeyDown;
        }

        private MainWindowViewModel ViewModel => vm;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ApplyTitleBarTheme();
        }

        // Match the system title bar to the app theme (dark title bar in dark mode).
        private void ApplyTitleBarTheme()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            Dwm.SetDarkMode(hwnd, ThemeManager.IsDark);
            if (TryFindResource("WindowBgColor") is Color background)
            {
                Dwm.SetCaptionColor(hwnd, background);
            }
        }

        #region Window position

        private void RestoreBounds()
        {
            var s = SettingsService.Load();
            if (s.MainWidth is > 0 && s.MainHeight is > 0)
            {
                Width = Math.Max(MinWidth, s.MainWidth.Value);
                Height = Math.Max(MinHeight, s.MainHeight.Value);
            }
            if (s.MainLeft is { } left && s.MainTop is { } top)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                var area = ScreenHelper.NearestWorkingArea(new Rect(left, top, Width, Height), this);
                Left = Math.Clamp(left, area.Left, Math.Max(area.Left, area.Right - Width));
                Top = Math.Clamp(top, area.Top, Math.Max(area.Top, area.Bottom - Height));
            }
        }

        public void SaveBounds()
        {
            if (WindowState != WindowState.Normal) return;
            SettingsService.Update(s =>
            {
                s.MainLeft = Left;
                s.MainTop = Top;
                s.MainWidth = Width;
                s.MainHeight = Height;
            });
        }

        #endregion

        #region Snackbar

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.SnackbarVisible))
            {
                AnimateSnackbar(ViewModel.SnackbarVisible);
            }
        }

        private void AnimateSnackbar(bool show)
        {
            Snackbar.IsHitTestVisible = show;
            var duration = TimeSpan.FromMilliseconds(show ? 167 : 120);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            Snackbar.BeginAnimation(OpacityProperty, new DoubleAnimation(show ? 1 : 0, duration) { EasingFunction = ease });
            SnackbarShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(show ? 0 : 12, duration) { EasingFunction = ease });
        }

        #endregion

        #region Menus

        private static MenuItem Item(string header, string? glyph, Action onClick, string? gesture = null, bool danger = false)
        {
            var item = new MenuItem { Header = header, InputGestureText = gesture ?? string.Empty };
            if (glyph != null) item.Icon = new TextBlock { Text = glyph };
            if (danger) item.SetResourceReference(ForegroundProperty, "Danger");
            item.Click += (_, _) => onClick();
            return item;
        }

        private static MenuItem Check(string header, bool isChecked, Action onClick)
        {
            var item = new MenuItem { Header = header, IsCheckable = false, IsChecked = isChecked };
            item.Click += (_, _) => onClick();
            return item;
        }

        private static void ShowMenu(ContextMenu menu, FrameworkElement target)
        {
            menu.PlacementTarget = target;
            menu.Placement = PlacementMode.Bottom;
            menu.HorizontalOffset = -(220 - target.ActualWidth) - 12;
            menu.VerticalOffset = -8;
            menu.IsOpen = true;
        }

        private void MoreButton_Click(object sender, RoutedEventArgs e)
        {
            var app = (App)Application.Current;
            var menu = new ContextMenu();
            menu.Items.Add(Item("Show all notes", "", NoteWindowManager.ShowAll));
            menu.Items.Add(Item("Hide all notes", "", NoteWindowManager.HideAll));
            menu.Items.Add(new Separator());

            var theme = new MenuItem { Header = "Theme", Icon = new TextBlock { Text = "" } };
            theme.Items.Add(Check("System", ThemeManager.Mode == ThemeManager.System, () => ViewModel.SetThemeCommand.Execute(ThemeManager.System)));
            theme.Items.Add(Check("Light", ThemeManager.Mode == ThemeManager.Light, () => ViewModel.SetThemeCommand.Execute(ThemeManager.Light)));
            theme.Items.Add(Check("Dark", ThemeManager.Mode == ThemeManager.Dark, () => ViewModel.SetThemeCommand.Execute(ThemeManager.Dark)));
            menu.Items.Add(theme);

            var startup = Check("Start with Windows", StartupService.IsEnabled, () => StartupService.SetEnabled(!StartupService.IsEnabled));
            startup.IsEnabled = StartupService.IsAvailable || StartupService.IsEnabled;
            if (!startup.IsEnabled) startup.ToolTip = "Available once Top Note is installed";
            menu.Items.Add(startup);

            var settings = SettingsService.Load();
            var phone = new MenuItem { Header = "Phone notifications", Icon = new TextBlock { Text = "" } };
            phone.Items.Add(Check("Send reminders to my phone", PhoneNotifier.IsEnabled,
                () => PhoneNotifier.SetEnabled(!PhoneNotifier.IsEnabled)));
            phone.Items.Add(Item("How to set up my phone…", null, PhoneNotifier.ShowSetup));
            var test = Item("Send a test notification", null, PhoneNotifier.SendTest);
            test.IsEnabled = PhoneNotifier.IsEnabled;
            phone.Items.Add(test);
            phone.Items.Add(new Separator());
            var showText = Check("Include the task text", settings.PhoneShowTaskText,
                () => SettingsService.Update(s => s.PhoneShowTaskText = !s.PhoneShowTaskText));
            showText.ToolTip = "Off: the phone only shows the time, e.g. \"Task at 14:00\"";
            phone.Items.Add(showText);
            menu.Items.Add(phone);
            menu.Items.Add(new Separator());

            menu.Items.Add(Item("Export notes…", "", ExportNotes));
            menu.Items.Add(Item("Open data folder", "", OpenDataFolder));
            menu.Items.Add(Item("Keyboard shortcuts", "", ShowShortcuts));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Exit Top Note", "", app.ExitApp));
            ShowMenu(menu, MoreButton);
        }

        private void SortButton_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();
            menu.Items.Add(Check("Last edited", ViewModel.Sort == "Edited", () => ViewModel.Sort = "Edited"));
            menu.Items.Add(Check("Date created", ViewModel.Sort == "Created", () => ViewModel.Sort = "Created"));
            menu.Items.Add(Check("Colour", ViewModel.Sort == "Colour", () => ViewModel.Sort = "Colour"));
            ShowMenu(menu, SortButton);
        }

        // Right-click on a card.
        private void NotesListBox_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if ((e.OriginalSource as FrameworkElement)?.DataContext is not NoteCardViewModel card)
            {
                e.Handled = true;
                return;
            }
            NotesListBox.SelectedItem = card;
            var menu = NotesListBox.ContextMenu;
            menu.Items.Clear();

            if (card.IsTrashed)
            {
                menu.Items.Add(Item("Restore", "", () => ViewModel.RestoreCommand.Execute(card)));
                menu.Items.Add(Item("Delete forever", "", () => ViewModel.DeleteForeverCommand.Execute(card), danger: true));
                return;
            }

            menu.Items.Add(Item("Open", "", () => ViewModel.OpenNoteCommand.Execute(card), "Enter"));
            menu.Items.Add(Item(card.IsPinned ? "Unpin" : "Pin on top", card.IsPinned ? "" : "",
                () => ViewModel.TogglePinCommand.Execute(card), "Ctrl+P"));

            var colour = new MenuItem { Header = "Colour", Icon = new TextBlock { Text = "" } };
            foreach (var c in NotePalette.All)
            {
                var swatch = new Ellipse { Width = 14, Height = 14, Fill = c.Body, Stroke = c.Swatch, StrokeThickness = 1.5 };
                var item = new MenuItem { Header = c.Name, Icon = swatch };
                if (card.Colors.Key == c.Key) item.FontWeight = FontWeights.SemiBold;
                var hex = c.BodyHex;
                item.Click += (_, _) => ViewModel.SetColorCommand.Execute(new object[] { card, hex });
                colour.Items.Add(item);
            }
            menu.Items.Add(colour);
            menu.Items.Add(Item("Duplicate", "", () => ViewModel.DuplicateCommand.Execute(card), "Ctrl+D"));
            menu.Items.Add(Item("Copy text", "", () => ViewModel.CopyTextCommand.Execute(card)));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Move to Trash", "", () => ViewModel.DeleteNoteCommand.Execute(card), "Del", danger: true));
        }

        #endregion

        #region More-menu actions

        private void ExportNotes()
        {
            NoteWindowManager.SaveAll();
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export notes",
                FileName = $"Top Note export {DateTime.Now:yyyy-MM-dd}",
                DefaultExt = ".md",
                Filter = "Markdown (*.md)|*.md|Text (*.txt)|*.txt",
            };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                BackupService.ExportMarkdown(dialog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static void OpenDataFolder()
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{SqliteDataAccess.DataFolder}\"") { UseShellExecute = true });
            }
            catch (Exception)
            {
                // Explorer unavailable; nothing to do.
            }
        }

        private void ShowShortcuts()
        {
            const string text =
                "Anywhere\n" +
                "  Ctrl+Alt+N\tNew note\n\n" +
                "Notes list\n" +
                "  Ctrl+N\tNew note\n" +
                "  Ctrl+F\tSearch\n" +
                "  Enter\tOpen selected note\n" +
                "  Ctrl+P\tPin / unpin\n" +
                "  Ctrl+D\tDuplicate\n" +
                "  Del\tMove to Trash\n" +
                "  Ctrl+Z\tUndo delete\n\n" +
                "In a note\n" +
                "  Ctrl+P\tPin on top\n" +
                "  Ctrl+L\tChecklist line\n" +
                "  [] or -\tStart a checkbox / bullet\n" +
                "  Ctrl+T\tRoll up / expand\n" +
                "  Ctrl+= / Ctrl+−\tBigger / smaller text\n" +
                "  Ctrl+D\tDuplicate\n" +
                "  Esc\tClose (the note is kept)";
            MessageBox.Show(this, text, "Keyboard shortcuts", MessageBoxButton.OK, MessageBoxImage.None);
        }

        #endregion

        #region Keyboard

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.FocusedElement is TextBox) return;
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z && ViewModel.CanUndo)
            {
                ViewModel.Undo();
                e.Handled = true;
            }
        }

        private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (SearchBox.Text.Length > 0)
                {
                    ViewModel.SearchText = string.Empty;
                }
                else
                {
                    NotesListBox.Focus();
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Down && NotesListBox.Items.Count > 0)
            {
                NotesListBox.SelectedIndex = Math.Max(0, NotesListBox.SelectedIndex);
                (NotesListBox.ItemContainerGenerator.ContainerFromIndex(NotesListBox.SelectedIndex) as ListBoxItem)?.Focus();
                e.Handled = true;
            }
        }

        private void NotesListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Use the item under the mouse, and ignore double-clicks on the card's buttons.
            if (e.OriginalSource is DependencyObject source && FindParent<ButtonBase>(source) != null) return;
            if ((e.OriginalSource as FrameworkElement)?.DataContext is NoteCardViewModel card)
            {
                ViewModel.OpenNoteCommand.Execute(card);
            }
        }

        private void NotesListBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (NotesListBox.SelectedItem is not NoteCardViewModel card) return;

            switch (e.Key)
            {
                case Key.Enter:
                    ViewModel.OpenNoteCommand.Execute(card);
                    e.Handled = true;
                    break;
                case Key.Delete:
                    ViewModel.DeleteNoteCommand.Execute(card);
                    e.Handled = true;
                    break;
                case Key.P when Keyboard.Modifiers == ModifierKeys.Control:
                    ViewModel.TogglePinCommand.Execute(card);
                    e.Handled = true;
                    break;
                case Key.D when Keyboard.Modifiers == ModifierKeys.Control:
                    ViewModel.DuplicateCommand.Execute(card);
                    e.Handled = true;
                    break;
            }
        }

        private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var current = child;
            while (current != null)
            {
                if (current is T match) return match;
                current = current is Visual or System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(current)
                    : LogicalTreeHelper.GetParent(current);
            }
            return null;
        }

        #endregion

        // Closing the list hides it to the tray; notes stay open. Exit from the tray menu.
        protected override void OnClosing(CancelEventArgs e)
        {
            SaveBounds();
            if (!App.IsExiting)
            {
                e.Cancel = true;
                Hide();
            }
            base.OnClosing(e);
        }
    }
}
