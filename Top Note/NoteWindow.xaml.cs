using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using Top_Note.Models;
using Top_Note.Services;
using Top_Note.ViewModels;
using static Top_Note.NativeMethods;

namespace Top_Note
{
    /// <summary>
    /// Interaction logic for NoteWindow.xaml
    /// </summary>
    public partial class NoteWindow : Window
    {
        // Sent to all top-level windows when explorer.exe (the taskbar) restarts.
        private static readonly uint WM_TASKBARCREATED = RegisterWindowMessage("TaskbarCreated");

        private const double HeaderHeight = 32;
        private const double ExpandedMinHeight = 120;

        private IntPtr hwnd;
        private HwndSource? source;
        private readonly DispatcherTimer topmostTimer;
        private readonly DispatcherTimer clockTimer = new() { Interval = TimeSpan.FromMinutes(1) };

        // Off until the window is on screen, so opening a note (or clamping it onto a visible monitor)
        // doesn't count as the user moving it and overwrite the saved position.
        private bool trackGeometry;

        // While rolled up, the window's height is the header only and must not be saved as the note's height.
        private bool collapsedView;
        private bool? chromeShown;

        public NoteWindow()
        {
            InitializeComponent();
            DataContext = new NoteWindowViewModel();
            topmostTimer = CreateTopmostTimer();
            Init();

            // Open new notes near the mouse so they're on the screen the user is working on.
            WindowStartupLocation = WindowStartupLocation.Manual;
            var mouse = GetMousePositionInDips();
            Left = mouse.X - 40;
            Top = mouse.Y - 16;
            KeepOnScreen();
        }

        // overload for editing existing note
        public NoteWindow(NoteModel note)
        {
            InitializeComponent();
            DataContext = new NoteWindowViewModel(note);
            topmostTimer = CreateTopmostTimer();
            Init();

            Width = note.Width > 0 ? note.Width : 300;
            Height = note.Height > 0 ? note.Height : 300;
            Left = note.Left;
            Top = note.Top;
            KeepOnScreen();

            if (note.IsCollapsed)
            {
                ApplyCollapsed(true);
            }
        }

        private void Init()
        {
            // handledEventsToo: the TextBox has already chosen its I-beam cursor by the time this runs.
            NoteText.AddHandler(Mouse.QueryCursorEvent, new QueryCursorEventHandler(NoteText_QueryCursor), true);
            NoteText.LostKeyboardFocus += (_, _) => RemoveTrailingEmptyItem();

            if (ViewModel is { } vm)
            {
                vm.Saved += (_, _) => PlaySavedHint();
            }
            clockTimer.Tick += (_, _) => ViewModel?.RefreshTime();
            clockTimer.Start();
            MouseEnter += (_, _) => UpdateChrome();
            MouseLeave += (_, _) => UpdateChrome();
            MoreButton.ContextMenu.Opened += (_, _) => UpdateChrome();
            MoreButton.ContextMenu.Closed += (_, _) => UpdateChrome();
            UpdatePlaceholder();
            UpdateChrome(animate: false);
        }

        private NoteWindowViewModel? ViewModel => DataContext as NoteWindowViewModel;

        private bool IsPinned => ViewModel?.IsPinned == true;

        #region Always on top

        // WPF only applies Topmost once, when the value changes. Anything that later pushes the note
        // down (the taskbar, the Start menu, another always-on-top app) is never undone, so pinned
        // notes re-assert their place on top whenever they lose it.

        private DispatcherTimer CreateTopmostTimer()
        {
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) =>
            {
                // While the note is active it is already on top; re-asserting then would push it
                // above its own popups.
                if (IsVisible && !IsActive && WindowState != WindowState.Minimized && LostTopmost() && !IsFullScreenAppRunning())
                {
                    ReassertTopmost();
                }
            };
            return timer;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            hwnd = new WindowInteropHelper(this).Handle;
            source = HwndSource.FromHwnd(hwnd);
            source.AddHook(WndProc);

            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;

            if (ViewModel != null)
            {
                ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            }

            // Windows 11 draws rounded corners and a border tinted to the note colour.
            // Windows 10 has square corners, so a faint outline keeps notes from blending into white windows.
            Dwm.SetCorners(hwnd, Dwm.CornerRound);
            ApplyBorderColor();
            if (!Dwm.IsWindows11)
            {
                RootBorder.BorderThickness = new Thickness(1);
            }

            ApplyPinnedState();
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(NoteWindowViewModel.IsPinned):
                    ApplyPinnedState();
                    UpdateChrome();
                    break;
                case nameof(NoteWindowViewModel.Colors):
                    ApplyBorderColor();
                    break;
            }
        }

        private void ApplyBorderColor()
        {
            if (ViewModel is { } vm) Dwm.SetBorderColor(hwnd, vm.Colors.HeaderColor);
        }

        private void ApplyPinnedState()
        {
            if (hwnd == IntPtr.Zero) return;

            Topmost = IsPinned;

            // Without a minimise box, Win+M and "Show desktop" leave pinned notes alone.
            long style = GetWindowLong(hwnd, GWL_STYLE);
            style = IsPinned ? style & ~WS_MINIMIZEBOX : style | WS_MINIMIZEBOX;
            style &= ~WS_MAXIMIZEBOX;
            SetWindowLong(hwnd, GWL_STYLE, style);

            if (IsPinned)
            {
                ReassertTopmost();
                topmostTimer.Start();
            }
            else
            {
                topmostTimer.Stop();
            }
        }

        private void ReassertTopmost()
        {
            if (hwnd == IntPtr.Zero || !IsPinned) return;
            // Leave the order alone while the user is in another Top Note window, so overlapping pinned
            // notes, tooltips and the tray menu aren't covered. Other apps can't get above pinned notes anyway.
            if (ForegroundIsThisApp()) return;
            // NOACTIVATE: move to the top of the z-order without taking keyboard focus from other apps.
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        }

        // Only act when the note has really dropped out of the always-on-top group. Re-raising a note that is
        // still topmost would put it over other apps' open menus and undo the order of overlapping pinned notes.
        private bool LostTopmost() =>
            hwnd != IntPtr.Zero && (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TOPMOST) == 0;

        private void ReassertTopmostSoon()
        {
            // Let whatever just took over (Start menu, taskbar, another window) finish first.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (LostTopmost() && !IsFullScreenAppRunning()) ReassertTopmost();
            }));
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            UpdateChrome();
        }

        protected override void OnDeactivated(EventArgs e)
        {
            base.OnDeactivated(e);
            UpdateChrome();
            ReassertTopmostSoon();
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            if (IsPinned && WindowState == WindowState.Minimized)
            {
                // Show desktop (Win+D) fallback: restore without stealing focus.
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    ShowWindow(hwnd, SW_SHOWNOACTIVATE);
                    ReassertTopmost();
                }));
            }
        }

        private IntPtr WndProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_WINDOWPOSCHANGING && IsPinned)
            {
                // If something tries to move a pinned note out of the always-on-top group, keep it there.
                var wp = Marshal.PtrToStructure<WINDOWPOS>(lParam);
                if ((wp.flags & SWP_NOZORDER) == 0 && wp.hwndInsertAfter != HWND_TOPMOST)
                {
                    bool insertAfterIsTopmost = wp.hwndInsertAfter.ToInt64() > 1
                        && (GetWindowLong(wp.hwndInsertAfter, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0;
                    if (!insertAfterIsTopmost)
                    {
                        wp.hwndInsertAfter = HWND_TOPMOST;
                        Marshal.StructureToPtr(wp, lParam, false);
                    }
                }
            }
            else if (msg == WM_SYSCOMMAND)
            {
                int command = wParam.ToInt32() & 0xFFF0;
                if (command == SC_MAXIMIZE || (command == SC_MINIMIZE && IsPinned))
                {
                    handled = true;
                }
            }
            else if (msg == WM_DISPLAYCHANGE || msg == WM_DPICHANGED || (uint)msg == WM_TASKBARCREATED)
            {
                ReassertTopmostSoon();
            }
            return IntPtr.Zero;
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                KeepOnScreen();
                ReassertTopmost();
            }));
        }

        private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
        {
            if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.RemoteConnect or SessionSwitchReason.ConsoleConnect)
            {
                ReassertTopmostSoon();
            }
        }

        #endregion

        #region Chrome (header buttons and footer appear while you use the note)

        // An idle note shows just its coloured strip, title and text, like paper. The buttons fade in
        // when the note is active or under the mouse.
        private void UpdateChrome(bool animate = true)
        {
            bool show = IsActive || IsMouseOver || ColorPopup.IsOpen || MoreButton.ContextMenu.IsOpen;
            if (chromeShown == show) return;
            chromeShown = show;

            Fade(HeaderActions, show, animate);
            Fade(Footer, show && !collapsedView, animate);
            Fade(PinButton, show || IsPinned, animate);
            // The title would just repeat the first line of the note, so it only shows when rolled up.
            Fade(TitleText, collapsedView, animate, visibleOpacity: 0.7);
            if (!show) SavedHint.BeginAnimation(OpacityProperty, null);
        }

        private static void Fade(UIElement element, bool visible, bool animate, double visibleOpacity = 1)
        {
            element.IsHitTestVisible = visible;
            double to = visible ? visibleOpacity : 0;
            if (!animate)
            {
                element.BeginAnimation(OpacityProperty, null);
                element.Opacity = to;
                return;
            }
            var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(visible ? 120 : 200))
            {
                BeginTime = visible ? TimeSpan.Zero : TimeSpan.FromMilliseconds(400),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            element.BeginAnimation(OpacityProperty, animation);
        }

        private void PlaySavedHint()
        {
            if (chromeShown != true || collapsedView) return;
            var animation = new DoubleAnimationUsingKeyFrames();
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(0.6, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(0.6, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1320))));
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1620))));
            SavedHint.BeginAnimation(OpacityProperty, animation);
        }

        private void Flyout_Closed(object? sender, EventArgs e) => UpdateChrome();

        private void ColorButton_Click(object sender, RoutedEventArgs e)
        {
            ColorPopup.IsOpen = true;
            UpdateChrome();
        }

        private void Swatch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string hex })
            {
                ViewModel?.ChangeColor(hex);
            }
            ColorPopup.IsOpen = false;
        }

        private void MoreButton_Click(object sender, RoutedEventArgs e)
        {
            var menu = MoreButton.ContextMenu;
            if (menu.Items[0] is MenuItem rollUp)
            {
                rollUp.Header = collapsedView ? "Expand" : "Roll up";
                rollUp.Icon = new TextBlock { Text = collapsedView ? "" : "" };
            }
            menu.PlacementTarget = MoreButton;
            menu.Placement = PlacementMode.Bottom;
            menu.HorizontalOffset = -160;
            menu.VerticalOffset = -8;
            menu.IsOpen = true;
        }

        #endregion

        #region Roll up

        private void RollUp_Click(object sender, RoutedEventArgs e) => ToggleCollapse();

        private void ToggleCollapse()
        {
            if (ViewModel is not { } vm) return;
            vm.IsCollapsed = !collapsedView;
            ApplyCollapsed(vm.IsCollapsed);
        }

        // Rolled up = only the header strip shows. The saved height stays the expanded one.
        private void ApplyCollapsed(bool collapse)
        {
            collapsedView = collapse;
            NoteText.Visibility = collapse ? Visibility.Collapsed : Visibility.Visible;
            Footer.Visibility = collapse ? Visibility.Collapsed : Visibility.Visible;
            UpdatePlaceholder();

            if (collapse)
            {
                MinHeight = HeaderHeight;
                MaxHeight = HeaderHeight;
                Height = HeaderHeight;
            }
            else
            {
                double expanded = Math.Max(ExpandedMinHeight, ViewModel?.WindowHeight ?? 300);
                MaxHeight = double.PositiveInfinity;
                MinHeight = ExpandedMinHeight;
                Height = expanded;
            }

            chromeShown = null;
            UpdateChrome(animate: false);

            // Keep keyboard focus inside the window so shortcuts (Ctrl+T to expand, Esc, Ctrl+P) still work.
            if (collapse)
            {
                Root.Focus();
            }
            else if (IsActive)
            {
                NoteText.Focus();
            }
        }

        // Window-level so it works while rolled up (the text box is hidden then).
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            // Esc closes the colour picker first, rather than the whole note.
            if (e.Key == Key.Escape && ColorPopup.IsOpen)
            {
                ColorPopup.IsOpen = false;
                e.Handled = true;
                return;
            }
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.T)
            {
                ToggleCollapse();
                e.Handled = true;
                return;
            }
            base.OnPreviewKeyDown(e);
        }

        #endregion

        #region Checklists

        private void Checklist_Click(object sender, RoutedEventArgs e) => ToggleChecklistOnSelection();

        // Ctrl+L: turn the selected lines into checklist items, or back into plain lines.
        private void ToggleChecklistOnSelection()
        {
            var text = NoteText.Text;
            int caret = NoteText.CaretIndex;
            // Work on logical lines (split by \n), mapping from the caret's character position.
            var lines = text.Split('\n').ToList();
            int startLogical = LogicalLineAt(text, NoteText.SelectionStart);
            int endLogical = LogicalLineAt(text, NoteText.SelectionStart + NoteText.SelectionLength);
            bool allChecklist = Enumerable.Range(startLogical, endLogical - startLogical + 1)
                .All(i => IsChecklistLine(lines[i]));

            int delta = 0;
            for (int i = startLogical; i <= endLogical; i++)
            {
                if (allChecklist)
                {
                    var stripped = RemoveMarker(lines[i]);
                    delta -= lines[i].Length - stripped.Length;
                    lines[i] = stripped;
                }
                else if (!IsChecklistLine(lines[i]))
                {
                    var withoutBullet = RemoveMarker(lines[i]);
                    delta += Models.NoteText.Unchecked.Length - (lines[i].Length - withoutBullet.Length);
                    lines[i] = Models.NoteText.Unchecked + withoutBullet;
                }
            }

            NoteText.Text = string.Join("\n", lines);
            NoteText.CaretIndex = Math.Clamp(caret + delta, 0, NoteText.Text.Length);
            NoteText.Focus();
        }

        private static int LogicalLineAt(string text, int index)
        {
            index = Math.Clamp(index, 0, text.Length);
            int count = 0;
            for (int i = 0; i < index; i++)
            {
                if (text[i] == '\n') count++;
            }
            return count;
        }

        private static bool IsChecklistLine(string line) =>
            line.StartsWith(Models.NoteText.Unchecked, StringComparison.Ordinal) || line.StartsWith(Models.NoteText.Checked, StringComparison.Ordinal);

        private static string RemoveMarker(string line)
        {
            foreach (var marker in new[] { Models.NoteText.Unchecked, Models.NoteText.Checked, Models.NoteText.Bullet })
            {
                if (line.StartsWith(marker, StringComparison.Ordinal)) return line[marker.Length..];
            }
            return line;
        }

        private void NoteText_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.L)
            {
                ToggleChecklistOnSelection();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None && NoteText.SelectionLength == 0)
            {
                // Enter on a checklist/bullet line continues the list; Enter on an empty item ends it.
                var text = NoteText.Text;
                int caret = NoteText.CaretIndex;
                int lineStart = caret == 0 ? 0 : text.LastIndexOf('\n', caret - 1) + 1;
                var line = text[lineStart..caret];
                string? marker = line.StartsWith(Models.NoteText.Unchecked, StringComparison.Ordinal) || line.StartsWith(Models.NoteText.Checked, StringComparison.Ordinal)
                    ? Models.NoteText.Unchecked
                    : line.StartsWith(Models.NoteText.Bullet, StringComparison.Ordinal) ? Models.NoteText.Bullet : null;
                if (marker == null) return;

                int lineEnd = text.IndexOf('\n', caret);
                bool emptyItem = line.Length <= 2 && (lineEnd < 0 ? text[caret..] : text[caret..lineEnd]).Trim().Length == 0;
                if (emptyItem)
                {
                    NoteText.Text = text.Remove(lineStart, line.Length);
                    NoteText.CaretIndex = lineStart;
                }
                else
                {
                    // Same line break the TextBox itself inserts for Enter.
                    NoteText.Text = text.Insert(caret, Environment.NewLine + marker);
                    NoteText.CaretIndex = caret + Environment.NewLine.Length + marker.Length;
                }
                e.Handled = true;
            }
        }

        // Pressing Enter after the last task leaves an empty box behind; tidy it away when the user leaves the note.
        private void RemoveTrailingEmptyItem()
        {
            var text = NoteText.Text;
            int lastLineStart = text.LastIndexOf('\n') + 1;
            if (lastLineStart == 0 || text[lastLineStart..].Trim() is not ("☐" or "☑" or "•")) return;

            int caret = NoteText.CaretIndex;
            NoteText.Text = text[..(lastLineStart - 1)].TrimEnd('\r');
            NoteText.CaretIndex = Math.Min(caret, NoteText.Text.Length);
        }

        // Typing "[] " or "- " at the start of a line turns it into a checkbox or bullet.
        private void NoteText_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdatePlaceholder();

            var text = NoteText.Text;
            int caret = NoteText.CaretIndex;
            if (caret < 2) return;
            int lineStart = text.LastIndexOf('\n', caret - 1) + 1;
            var typed = text[lineStart..caret];
            string? replacement = typed switch
            {
                "[] " => Models.NoteText.Unchecked,
                "[ ] " => Models.NoteText.Unchecked,
                "- " => Models.NoteText.Bullet,
                "* " => Models.NoteText.Bullet,
                _ => null,
            };
            if (replacement == null) return;
            NoteText.Text = text.Remove(lineStart, typed.Length).Insert(lineStart, replacement);
            NoteText.CaretIndex = lineStart + replacement.Length;
        }

        // Index of the ☐ / ☑ at the start of a line under the mouse, or -1.
        private int CheckboxIndexAt(MouseEventArgs e)
        {
            int index = NoteText.GetCharacterIndexFromPoint(e.GetPosition(NoteText), false);
            if (index < 0 || index >= NoteText.Text.Length) return -1;

            char c = NoteText.Text[index];
            if (c != '☐' && c != '☑') return -1;
            int lineStart = index == 0 ? 0 : NoteText.Text.LastIndexOf('\n', index - 1) + 1;
            return index == lineStart ? index : -1;
        }

        // Hand cursor over a checkbox, so it reads as clickable rather than as text.
        private void NoteText_QueryCursor(object sender, QueryCursorEventArgs e)
        {
            if (CheckboxIndexAt(e) < 0) return;
            e.Cursor = Cursors.Hand;
            e.Handled = true;
        }

        // Clicking a ☐ or ☑ ticks it.
        private void NoteText_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            int index = CheckboxIndexAt(e);
            if (index < 0) return;

            char c = NoteText.Text[index];
            int caret = NoteText.CaretIndex;
            var chars = NoteText.Text.ToCharArray();
            chars[index] = c == '☐' ? '☑' : '☐';
            NoteText.Text = new string(chars);
            NoteText.CaretIndex = Math.Min(caret, NoteText.Text.Length);
            NoteText.Focus();
            e.Handled = true;
        }

        #endregion

        private void NoteText_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control || ViewModel is not { } vm) return;
            (e.Delta > 0 ? vm.BiggerTextCommand : vm.SmallerTextCommand).Execute(null);
            e.Handled = true;
        }

        private void UpdatePlaceholder()
        {
            Placeholder.Visibility = !collapsedView && string.IsNullOrEmpty(NoteText.Text) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void DragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source && FindParent<ButtonBase>(source) != null) return;

            if (e.ClickCount == 2 && sender == Header)
            {
                ToggleCollapse();
                e.Handled = true;
                return;
            }
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try
                {
                    DragMove();
                }
                catch (InvalidOperationException)
                {
                    // The button was released before the drag started.
                }
            }
        }

        private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var current = child;
            while (current != null)
            {
                if (current is T match) return match;
                current = current is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                    ? System.Windows.Media.VisualTreeHelper.GetParent(current)
                    : LogicalTreeHelper.GetParent(current);
            }
            return null;
        }

        public void BringToFront()
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Show();
            Activate();
            // Notes restored at startup were shown without focus; put the cursor in the text now.
            if (!collapsedView && !NoteText.IsKeyboardFocusWithin)
            {
                NoteText.Focus();
                NoteText.CaretIndex = NoteText.Text.Length;
            }
        }

        // A note saved on a monitor that's since been unplugged would otherwise open invisibly.
        private void KeepOnScreen()
        {
            var area = ScreenHelper.NearestWorkingArea(new Rect(Left, Top, Width, Height), Application.Current.MainWindow ?? this);
            Left = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width));
            Top = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height));
        }

        private Point GetMousePositionInDips()
        {
            var pixels = System.Windows.Forms.Control.MousePosition;
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(Application.Current.MainWindow ?? this);
            return new Point(pixels.X / dpi.DpiScaleX, pixels.Y / dpi.DpiScaleY);
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            trackGeometry = true;
            // A brand-new note's starting position is what gets saved with its first text.
            if (ViewModel is { NoteId: 0 } vm)
            {
                vm.SetInitialGeometry(Left, Top, ActualWidth, ActualHeight);
            }
        }

        private void NoteWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (!ShowActivated || collapsedView) return;
            NoteText.Focus();
            NoteText.CaretIndex = NoteText.Text.Length;
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);

            if (trackGeometry && ViewModel is { } vm)
            {
                vm.WindowLeft = Left;
                vm.WindowTop = Top;
            }
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);

            if (trackGeometry && ViewModel is { } vm)
            {
                vm.WindowWidth = ActualWidth;
                if (!collapsedView)
                {
                    vm.WindowHeight = ActualHeight;
                }
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            ViewModel?.Flush();
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            topmostTimer.Stop();
            clockTimer.Stop();
            // SystemEvents are static: unhook or every closed note leaks.
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            source?.RemoveHook(WndProc);
            if (ViewModel != null)
            {
                ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }
            NoteWindowManager.Unregister(this);
            base.OnClosed(e);
        }
    }
}
