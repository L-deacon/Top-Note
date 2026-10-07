using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Top_Note.Models;
using Top_Note.Services;

namespace Top_Note.ViewModels
{
    // One colour choice in the note's colour picker.
    public class SwatchOption : ObservableObject
    {
        public SwatchOption(NoteColor color) => Color = color;

        public NoteColor Color { get; }

        private bool isSelected;
        public bool IsSelected
        {
            get => isSelected;
            set => Set(ref isSelected, value);
        }
    }

    public class NoteWindowViewModel : ViewModelBase
    {
        public const int DefaultFontSize = 15;
        public static readonly int[] FontSizeSteps = { 10, 12, 14, 15, 16, 18, 20, 22, 24, 28, 32 };

        #region Properties

        private bool isPinned;
        public bool IsPinned
        {
            get { return isPinned; }
            set
            {
                if (isPinned == value)
                {
                    return;
                }
                isPinned = value;
                RaisePropertyChanged();
                // Pinning is saved straight away so a pinned note is reopened on top after a restart.
                if (initialised && !isDeleted)
                {
                    isDirty = true;
                    listChanged = true;
                    contentChanged = true;
                    Flush();
                }
            }
        }

        private NoteColor colors = NotePalette.Yellow;
        public NoteColor Colors
        {
            get => colors;
            private set
            {
                colors = value;
                RaisePropertyChanged();
                foreach (var swatch in Swatches)
                {
                    swatch.IsSelected = swatch.Color.Key == value.Key;
                }
            }
        }

        public List<SwatchOption> Swatches { get; } = NotePalette.All.Select(c => new SwatchOption(c)).ToList();

        private string content = string.Empty;
        public string Content
        {
            get { return content; }
            set
            {
                if (content == value)
                {
                    return;
                }
                content = value ?? string.Empty;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(Title));
                RaisePropertyChanged(nameof(MetaText));
                listChanged = true;
                contentChanged = true;
                QueueSave();
            }
        }

        public string Title => NoteText.Title(Content);

        private string colorHex = NotePalette.DefaultHex;
        public string ColorHex
        {
            get { return colorHex; }
            set
            {
                if (colorHex == value)
                {
                    return;
                }
                colorHex = value;
                RaisePropertyChanged();
                listChanged = true;
                contentChanged = true;
                QueueSave();
            }
        }

        private int selectedFontSize = DefaultFontSize;
        public int SelectedFontSize
        {
            get { return selectedFontSize; }
            set
            {
                value = Math.Clamp(value, FontSizeSteps[0], FontSizeSteps[^1]);
                if (selectedFontSize == value)
                {
                    return;
                }
                selectedFontSize = value;
                RaisePropertyChanged();
                QueueSave();
            }
        }

        private bool isCollapsed;
        public bool IsCollapsed
        {
            get => isCollapsed;
            set
            {
                if (isCollapsed == value) return;
                isCollapsed = value;
                RaisePropertyChanged();
                QueueSave();
            }
        }

        private int noteId = 0;
        public int NoteId => noteId;

        private string? createdUtc;
        private string? modifiedUtc;

        // Footer text: when the note was last edited, plus the word count.
        public string MetaText
        {
            get
            {
                int words = NoteText.WordCount(Content);
                var edited = BackupService.TryParse(modifiedUtc);
                string wordText = words == 1 ? "1 word" : $"{words} words";
                return edited == null ? wordText : $"Edited {RelativeTime.Format(edited.Value)}  ·  {wordText}";
            }
        }

        public string MetaToolTip
        {
            get
            {
                var created = BackupService.TryParse(createdUtc);
                var edited = BackupService.TryParse(modifiedUtc);
                if (created == null && edited == null) return "Not saved yet";
                var parts = new List<string>();
                if (created != null) parts.Add($"Created {created:d MMM yyyy HH:mm}");
                if (edited != null) parts.Add($"Edited {edited:d MMM yyyy HH:mm}");
                return string.Join("\n", parts);
            }
        }

        private double windowLeft;
        public double WindowLeft
        {
            get => windowLeft;
            set
            {
                if (windowLeft == value) return;
                windowLeft = value;
                RaisePropertyChanged();
                QueueSave();
            }
        }

        private double windowTop;
        public double WindowTop
        {
            get => windowTop;
            set
            {
                if (windowTop == value) return;
                windowTop = value;
                RaisePropertyChanged();
                QueueSave();
            }
        }

        private double windowWidth = 300;
        public double WindowWidth
        {
            get => windowWidth;
            set
            {
                if (windowWidth == value) return;
                windowWidth = value;
                RaisePropertyChanged();
                QueueSave();
            }
        }

        private double windowHeight = 300;
        public double WindowHeight
        {
            get => windowHeight;
            set
            {
                if (windowHeight == value) return;
                windowHeight = value;
                RaisePropertyChanged();
                QueueSave();
            }
        }

        // Debounced autosave: every change restarts the timer, the note is written once typing pauses.
        private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
        private bool initialised;
        private bool isDirty;
        private bool listChanged;
        private bool contentChanged;
        private bool isDeleted;

        #endregion

        #region Constructor

        public NoteWindowViewModel()
        {
            TogglePinCommand = new RelayCommand(TogglePin);
            SaveNoteCommand = new RelayCommand(Flush);
            CloseNoteCommand = new RelayCommand(CloseWindow);
            NewNoteCommand = new RelayCommand(() => NoteWindowManager.OpenNew());
            ChangeColorCommand = new RelayCommand<string>(ChangeColor);
            BiggerTextCommand = new RelayCommand(() => StepFontSize(+1));
            SmallerTextCommand = new RelayCommand(() => StepFontSize(-1));
            ResetTextSizeCommand = new RelayCommand(() => SelectedFontSize = DefaultFontSize);
            DuplicateCommand = new RelayCommand(Duplicate);
            CopyTextCommand = new RelayCommand(() => ClipboardHelper.TrySetText(Content));
            DeleteCommand = new RelayCommand(Delete);
            ShowListCommand = new RelayCommand(() => (Application.Current as App)?.ShowMainWindow());

            saveTimer.Tick += (_, _) => Flush();

            Colors = NotePalette.Resolve(ColorHex);
            listChanged = false;
            contentChanged = false;
            initialised = true;
        }

        public NoteWindowViewModel(NoteModel note) : this()
        {
            if (note == null) return;

            initialised = false;
            noteId = note.Id;
            Content = note.Content ?? string.Empty;
            isPinned = note.IsPinned;
            // Keep the stored value (even a legacy colour) until the user picks a new one.
            colorHex = string.IsNullOrEmpty(note.Color) ? NotePalette.DefaultHex : note.Color;
            Colors = NotePalette.Resolve(colorHex);
            WindowLeft = note.Left;
            WindowTop = note.Top;
            WindowWidth = note.Width > 0 ? note.Width : 300;
            WindowHeight = note.Height > 0 ? note.Height : 300;
            SelectedFontSize = note.FontSize > 0 ? note.FontSize : DefaultFontSize;
            isCollapsed = note.IsCollapsed;
            createdUtc = note.CreatedUtc;
            modifiedUtc = note.ModifiedUtc;
            // Loading the note isn't an edit.
            isDirty = false;
            listChanged = false;
            contentChanged = false;
            initialised = true;
        }

        #endregion

        #region Methods

        private void TogglePin()
        {
            IsPinned = !IsPinned;
        }

        public void ChangeColor(string? hex)
        {
            var color = NotePalette.Resolve(hex);
            ColorHex = color.BodyHex;
            Colors = color;
        }

        private void StepFontSize(int direction)
        {
            int next = direction > 0
                ? FontSizeSteps.FirstOrDefault(s => s > SelectedFontSize, FontSizeSteps[^1])
                : FontSizeSteps.LastOrDefault(s => s < SelectedFontSize, FontSizeSteps[0]);
            SelectedFontSize = next;
        }

        private void Duplicate()
        {
            Flush();
            // Nothing typed yet: a copy would just be another blank note.
            if (noteId == 0 && string.IsNullOrWhiteSpace(Content)) return;
            var model = ToModel();
            int id = SqliteDataAccess.DuplicateNote(model);
            if (SqliteDataAccess.GetNote(id) is { } copy)
            {
                NoteWindowManager.Open(copy);
            }
            NotesChanged?.Invoke(this, EventArgs.Empty);
        }

        private void Delete()
        {
            Flush();
            if (noteId == 0)
            {
                // Never saved: nothing to keep.
                MarkDeleted();
                CloseWindow();
                return;
            }
            NoteActions.MoveToTrash(noteId);
        }

        private void QueueSave()
        {
            if (!initialised || isDeleted) return;
            isDirty = true;
            saveTimer.Stop();
            saveTimer.Start();
        }

        private NoteModel ToModel() => new()
        {
            Id = noteId,
            Content = Content ?? string.Empty,
            IsPinned = IsPinned,
            Color = ColorHex ?? NotePalette.DefaultHex,
            Left = WindowLeft,
            Top = WindowTop,
            FontSize = SelectedFontSize,
            Width = (int)Math.Round(WindowWidth),
            Height = (int)Math.Round(WindowHeight),
            IsCollapsed = IsCollapsed,
            CreatedUtc = createdUtc,
        };

        // Writes the note now. Called by the autosave timer, Ctrl+S, pin changes and on close.
        public void Flush()
        {
            saveTimer.Stop();
            if (!initialised || isDeleted) return;
            if (!isDirty) return;

            // Don't create database rows for notes that were never typed in.
            if (noteId == 0 && string.IsNullOrWhiteSpace(Content)) return;

            var note = ToModel();
            // Only real edits move the "edited" time; moving or resizing a note doesn't.
            note.ModifiedUtc = contentChanged || noteId == 0 ? SqliteDataAccess.Now() : null;

            if (noteId > 0)
            {
                SqliteDataAccess.UpdateNote(note);
            }
            else
            {
                noteId = SqliteDataAccess.SaveNote(note);
                createdUtc = note.CreatedUtc;
                NoteWindowManager.Register(this);
                listChanged = true;
            }

            if (note.ModifiedUtc != null)
            {
                modifiedUtc = note.ModifiedUtc;
                RaisePropertyChanged(nameof(MetaText));
                RaisePropertyChanged(nameof(MetaToolTip));
            }

            bool wasEdit = contentChanged;
            isDirty = false;
            contentChanged = false;
            if (wasEdit) Saved?.Invoke(this, EventArgs.Empty);

            // Moves and resizes don't change the list, so don't refresh it for those.
            if (listChanged)
            {
                listChanged = false;
                NotesChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        // Called every minute so "Edited 3 min ago" stays true.
        public void RefreshTime() => RaisePropertyChanged(nameof(MetaText));

        // Position/size of a new window before the user has touched it; not a change by itself.
        public void SetInitialGeometry(double left, double top, double width, double height)
        {
            windowLeft = left;
            windowTop = top;
            windowWidth = width;
            windowHeight = height;
        }

        // Pin/colour changed from the notes list while this note is open.
        public void ApplyExternalChange(NoteModel note)
        {
            initialised = false;
            isPinned = note.IsPinned;
            RaisePropertyChanged(nameof(IsPinned));
            colorHex = note.Color;
            Colors = NotePalette.Resolve(note.Color);
            RaisePropertyChanged(nameof(ColorHex));
            modifiedUtc = note.ModifiedUtc;
            RaisePropertyChanged(nameof(MetaText));
            initialised = true;
        }

        // The note was deleted: stop writing it back.
        public void MarkDeleted()
        {
            saveTimer.Stop();
            isDeleted = true;
        }

        private void CloseWindow()
        {
            Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.DataContext == this)?.Close();
        }

        // Raised after a save that changes what the notes list shows.
        public static event EventHandler? NotesChanged;

        // Raised after the user's edits are written, for the "Saved" hint.
        public event EventHandler? Saved;

        #endregion

        #region RelayCommands

        public ICommand TogglePinCommand { get; }
        public ICommand SaveNoteCommand { get; }
        public ICommand CloseNoteCommand { get; }
        public ICommand NewNoteCommand { get; }
        public ICommand ChangeColorCommand { get; }
        public ICommand BiggerTextCommand { get; }
        public ICommand SmallerTextCommand { get; }
        public ICommand ResetTextSizeCommand { get; }
        public ICommand DuplicateCommand { get; }
        public ICommand CopyTextCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand ShowListCommand { get; }

        #endregion
    }
}
