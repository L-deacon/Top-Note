using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Top_Note.Models;
using Top_Note.Services;

namespace Top_Note.ViewModels
{
    public enum NoteFilter
    {
        All,
        Pinned,
        Trash,
    }

    public class MainWindowViewModel : ViewModelBase
    {
        #region Properties

        public ObservableCollection<NoteCardViewModel> Notes { get; } = new();

        public ListCollectionView NotesView { get; }

        private NoteFilter filter = NoteFilter.All;
        public NoteFilter Filter
        {
            get => filter;
            set
            {
                if (filter == value) return;
                filter = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(IsAllFilter));
                RaisePropertyChanged(nameof(IsPinnedFilter));
                RaisePropertyChanged(nameof(IsTrashFilter));
                LoadNotes();
            }
        }

        // Two-way friendly flags for the filter pills.
        public bool IsAllFilter
        {
            get => Filter == NoteFilter.All;
            set { if (value) Filter = NoteFilter.All; }
        }

        public bool IsPinnedFilter
        {
            get => Filter == NoteFilter.Pinned;
            set { if (value) Filter = NoteFilter.Pinned; }
        }

        public bool IsTrashFilter
        {
            get => Filter == NoteFilter.Trash;
            set { if (value) Filter = NoteFilter.Trash; }
        }

        private string searchText = string.Empty;
        public string SearchText
        {
            get { return searchText; }
            set
            {
                value ??= string.Empty;
                if (searchText == value) return;
                searchText = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(HasSearch));
                NotesView.Refresh();
                UpdateEmptyState();
            }
        }

        public bool HasSearch => SearchText.Length > 0;

        private int allCount;
        public int AllCount { get => allCount; private set => Set(ref allCount, value); }

        private int pinnedCount;
        public int PinnedCount { get => pinnedCount; private set => Set(ref pinnedCount, value); }

        private int trashCount;
        public int TrashCount { get => trashCount; private set => Set(ref trashCount, value); }

        private string summary = string.Empty;
        public string Summary { get => summary; private set => Set(ref summary, value); }

        private string sort;
        public string Sort
        {
            get => sort;
            set
            {
                if (sort == value) return;
                sort = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(SortLabel));
                ApplySort();
                SettingsService.Update(s => s.Sort = value);
            }
        }

        public string SortLabel => Sort switch
        {
            "Created" => "Date created",
            "Colour" => "Colour",
            _ => "Last edited",
        };

        public string ThemeMode => ThemeManager.Mode;

        // Empty state
        private bool isEmpty;
        public bool IsEmpty { get => isEmpty; private set => Set(ref isEmpty, value); }

        private string emptyGlyph = "";
        public string EmptyGlyph { get => emptyGlyph; private set => Set(ref emptyGlyph, value); }

        private string emptyTitle = string.Empty;
        public string EmptyTitle { get => emptyTitle; private set => Set(ref emptyTitle, value); }

        private string emptyMessage = string.Empty;
        public string EmptyMessage { get => emptyMessage; private set => Set(ref emptyMessage, value); }

        private bool emptyShowsNewButton;
        public bool EmptyShowsNewButton { get => emptyShowsNewButton; private set => Set(ref emptyShowsNewButton, value); }

        // Snackbar ("Note moved to Trash · Undo")
        private bool snackbarVisible;
        public bool SnackbarVisible { get => snackbarVisible; private set => Set(ref snackbarVisible, value); }

        private string snackbarText = string.Empty;
        public string SnackbarText { get => snackbarText; private set => Set(ref snackbarText, value); }

        private readonly DispatcherTimer snackbarTimer = new() { Interval = TimeSpan.FromSeconds(6) };
        private readonly DispatcherTimer clockTimer = new() { Interval = TimeSpan.FromMinutes(1) };
        private (int id, bool reopen)? lastDeleted;

        public event EventHandler? FocusSearchRequested;

        #endregion

        #region Constuctor

        public MainWindowViewModel()
        {
            NewNoteCommand = new RelayCommand(NoteWindowManager.OpenNew);
            RefreshListCommand = new RelayCommand(LoadNotes);
            OpenNoteCommand = new RelayCommand<NoteCardViewModel>(OpenNote);
            DeleteNoteCommand = new RelayCommand<NoteCardViewModel>(DeleteNote);
            TogglePinCommand = new RelayCommand<NoteCardViewModel>(TogglePin);
            RefreshTasksCommand = new RelayCommand<NoteCardViewModel>(RefreshTasks);
            DuplicateCommand = new RelayCommand<NoteCardViewModel>(Duplicate);
            CopyTextCommand = new RelayCommand<NoteCardViewModel>(c => ClipboardHelper.TrySetText(c?.Content));
            SetColorCommand = new RelayCommand<object[]>(SetColor);
            RestoreCommand = new RelayCommand<NoteCardViewModel>(Restore);
            DeleteForeverCommand = new RelayCommand<NoteCardViewModel>(DeleteForever);
            EmptyTrashCommand = new RelayCommand(EmptyTrash);
            UndoDeleteCommand = new RelayCommand(UndoDelete);
            DismissSnackbarCommand = new RelayCommand(() => SnackbarVisible = false);
            ShowAllCommand = new RelayCommand(NoteWindowManager.ShowAll);
            HideAllCommand = new RelayCommand(NoteWindowManager.HideAll);
            ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
            FocusSearchCommand = new RelayCommand(() => FocusSearchRequested?.Invoke(this, EventArgs.Empty));
            SetThemeCommand = new RelayCommand<string>(SetTheme);
            SetSortCommand = new RelayCommand<string>(s => Sort = s ?? "Edited");

            sort = SettingsService.Load().Sort;

            NotesView = (ListCollectionView)CollectionViewSource.GetDefaultView(Notes);
            NotesView.Filter = o => o is NoteCardViewModel card && card.Matches(SearchText);
            NotesView.IsLiveSorting = true;
            ApplySort();

            // Keep the list in sync whenever any note saves, opens or closes.
            NoteWindowViewModel.NotesChanged += (_, _) => LoadNotes();
            NoteActions.Changed += (_, _) => LoadNotes();
            NoteActions.MovedToTrash += OnMovedToTrash;
            NoteWindowManager.OpenWindowsChanged += (_, _) => RefreshOpenState();
            ThemeManager.ThemeChanged += (_, _) => RaisePropertyChanged(nameof(ThemeMode));

            snackbarTimer.Tick += (_, _) =>
            {
                snackbarTimer.Stop();
                SnackbarVisible = false;
            };
            clockTimer.Tick += (_, _) =>
            {
                foreach (var card in Notes) card.RefreshTime();
            };
            clockTimer.Start();

            LoadNotes();
        }

        #endregion

        #region Methods

        private void ApplySort()
        {
            using (NotesView.DeferRefresh())
            {
                NotesView.SortDescriptions.Clear();
                NotesView.LiveSortingProperties.Clear();
                NotesView.SortDescriptions.Add(new SortDescription(nameof(NoteCardViewModel.IsPinned), ListSortDirection.Descending));
                NotesView.LiveSortingProperties.Add(nameof(NoteCardViewModel.IsPinned));
                var (key, direction) = Sort switch
                {
                    "Created" => (nameof(NoteCardViewModel.SortCreated), ListSortDirection.Descending),
                    "Colour" => (nameof(NoteCardViewModel.SortColourIndex), ListSortDirection.Ascending),
                    _ => (nameof(NoteCardViewModel.SortEdited), ListSortDirection.Descending),
                };
                NotesView.SortDescriptions.Add(new SortDescription(key, direction));
                NotesView.LiveSortingProperties.Add(key);
                NotesView.SortDescriptions.Add(new SortDescription(nameof(NoteCardViewModel.Id), ListSortDirection.Descending));
            }
        }

        // Diff by Id instead of clearing, so selection, scroll position and animations survive autosaves.
        public void LoadNotes()
        {
            var active = SqliteDataAccess.LoadNotes();
            var trashed = SqliteDataAccess.LoadDeleted();
            AllCount = active.Count;
            PinnedCount = active.Count(n => n.IsPinned);
            TrashCount = trashed.Count;
            Summary = AllCount == 0
                ? "Nothing here yet"
                : $"{AllCount} {(AllCount == 1 ? "note" : "notes")}" + (PinnedCount > 0 ? $"  ·  {PinnedCount} pinned" : string.Empty);

            var wanted = Filter switch
            {
                NoteFilter.Pinned => active.Where(n => n.IsPinned).ToList(),
                NoteFilter.Trash => trashed,
                _ => active,
            };

            var byId = wanted.ToDictionary(n => n.Id);
            for (int i = Notes.Count - 1; i >= 0; i--)
            {
                if (!byId.ContainsKey(Notes[i].Id)) Notes.RemoveAt(i);
            }
            var existing = Notes.ToDictionary(c => c.Id);
            foreach (var model in wanted)
            {
                if (existing.TryGetValue(model.Id, out var card))
                {
                    if (!SameNote(card.Model, model)) card.Update(model);
                }
                else
                {
                    Notes.Add(new NoteCardViewModel(model) { IsOpen = NoteWindowManager.IsOpen(model.Id) });
                }
            }
            // Edited text may now match (or stop matching) the search.
            if (HasSearch) NotesView.Refresh();
            UpdateEmptyState();
        }

        private static bool SameNote(NoteModel a, NoteModel b) =>
            a.Content == b.Content && a.IsPinned == b.IsPinned && a.Color == b.Color
            && a.ModifiedUtc == b.ModifiedUtc && a.DeletedUtc == b.DeletedUtc;

        private void RefreshOpenState()
        {
            foreach (var card in Notes) card.IsOpen = NoteWindowManager.IsOpen(card.Id);
        }

        private void UpdateEmptyState()
        {
            IsEmpty = NotesView.IsEmpty;
            if (!IsEmpty) return;

            EmptyShowsNewButton = false;
            if (HasSearch)
            {
                EmptyGlyph = "";
                EmptyTitle = $"No notes match “{SearchText.Trim()}”";
                EmptyMessage = "Try a different word, or search for a colour like “blue”.";
            }
            else if (Filter == NoteFilter.Pinned)
            {
                EmptyGlyph = "";
                EmptyTitle = "Nothing pinned";
                EmptyMessage = "Pin a note (Ctrl+P) to keep it above every window.";
            }
            else if (Filter == NoteFilter.Trash)
            {
                EmptyGlyph = "";
                EmptyTitle = "Trash is empty";
                EmptyMessage = "Deleted notes stay here for 30 days.";
            }
            else
            {
                EmptyGlyph = "";
                EmptyTitle = "No notes yet";
                EmptyMessage = "Press Ctrl+Alt+N anywhere to jot something down.";
                EmptyShowsNewButton = true;
            }
        }

        private void OpenNote(NoteCardViewModel? card)
        {
            if (card == null) return;
            if (card.IsTrashed)
            {
                Restore(card);
                return;
            }
            NoteWindowManager.Open(card.Model);
        }

        private void TogglePin(NoteCardViewModel? card)
        {
            if (card == null || card.IsTrashed) return;
            if (NoteWindowManager.TryGetViewModel(card.Id, out var vm) && vm != null)
            {
                vm.TogglePinCommand.Execute(null);
                return;
            }
            SqliteDataAccess.SetPinned(card.Id, !card.IsPinned);
            LoadNotes();
        }

        private async void RefreshTasks(NoteCardViewModel? card)
        {
            // Checked fresh: the card's flag can be a day old if the list hasn't reloaded since midnight.
            if (card == null || !DailyNoteService.IsTodaysNote(card.Id)) return;
            if (!await DailyNoteService.RefreshNoteAsync(card.Id))
            {
                MessageBox.Show(Application.Current.MainWindow!,
                    "Couldn't read the Outlook calendar. Make sure classic Outlook is installed and signed in.",
                    "Top Note", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SetColor(object[]? args)
        {
            if (args is not [NoteCardViewModel card, string hex]) return;
            if (NoteWindowManager.TryGetViewModel(card.Id, out var vm) && vm != null)
            {
                vm.ChangeColor(hex);
                vm.Flush();
                return;
            }
            SqliteDataAccess.SetColor(card.Id, hex);
            LoadNotes();
        }

        private void Duplicate(NoteCardViewModel? card)
        {
            if (card == null || card.IsTrashed) return;
            NoteWindowManager.SaveAll();
            var source = SqliteDataAccess.GetNote(card.Id) ?? card.Model;
            int id = SqliteDataAccess.DuplicateNote(source);
            if (SqliteDataAccess.GetNote(id) is { } copy)
            {
                NoteWindowManager.Open(copy);
            }
            LoadNotes();
        }

        private void DeleteNote(NoteCardViewModel? card)
        {
            if (card == null) return;
            if (card.IsTrashed)
            {
                DeleteForever(card);
                return;
            }
            NoteActions.MoveToTrash(card.Id);
        }

        private void OnMovedToTrash(int id, bool wasOpen)
        {
            lastDeleted = (id, wasOpen);
            SnackbarText = "Note moved to Trash";
            SnackbarVisible = true;
            snackbarTimer.Stop();
            snackbarTimer.Start();
        }

        private void UndoDelete()
        {
            if (lastDeleted is not { } deleted) return;
            lastDeleted = null;
            SnackbarVisible = false;
            NoteActions.Restore(deleted.id, deleted.reopen);
        }

        // Ctrl+Z in the list.
        public bool CanUndo => lastDeleted != null;

        public void Undo() => UndoDelete();

        private void Restore(NoteCardViewModel? card)
        {
            if (card == null) return;
            NoteActions.Restore(card.Id, reopen: false);
        }

        private void DeleteForever(NoteCardViewModel? card)
        {
            if (card == null) return;
            var answer = MessageBox.Show(Application.Current.MainWindow!,
                $"Delete “{card.Title}” forever?\n\nThis can't be undone.", "Top Note",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;
            SqliteDataAccess.DeleteNote(card.Id);
            LoadNotes();
        }

        private void EmptyTrash()
        {
            if (TrashCount == 0) return;
            var answer = MessageBox.Show(Application.Current.MainWindow!,
                $"Delete all {TrashCount} notes in the Trash forever?\n\nThis can't be undone.", "Top Note",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;
            SqliteDataAccess.EmptyTrash();
            LoadNotes();
        }

        private void SetTheme(string? mode)
        {
            ThemeManager.ApplyTheme(mode);
            SettingsService.Update(s => s.Theme = ThemeManager.Mode);
        }

        #endregion

        #region RelayCommands
        public ICommand NewNoteCommand { get; }
        public ICommand RefreshListCommand { get; }
        public ICommand OpenNoteCommand { get; }
        public ICommand DeleteNoteCommand { get; }
        public ICommand TogglePinCommand { get; }
        public ICommand RefreshTasksCommand { get; }
        public ICommand DuplicateCommand { get; }
        public ICommand CopyTextCommand { get; }
        public ICommand SetColorCommand { get; }
        public ICommand RestoreCommand { get; }
        public ICommand DeleteForeverCommand { get; }
        public ICommand EmptyTrashCommand { get; }
        public ICommand UndoDeleteCommand { get; }
        public ICommand DismissSnackbarCommand { get; }
        public ICommand ShowAllCommand { get; }
        public ICommand HideAllCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand FocusSearchCommand { get; }
        public ICommand SetThemeCommand { get; }
        public ICommand SetSortCommand { get; }
        #endregion
    }
}
