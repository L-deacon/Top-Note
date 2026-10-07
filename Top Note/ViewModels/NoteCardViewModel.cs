using GalaSoft.MvvmLight;
using Top_Note.Models;
using Top_Note.Services;

namespace Top_Note.ViewModels
{
    // One card in the notes list. Updated in place when the note changes, so the list never flickers.
    public class NoteCardViewModel : ObservableObject
    {
        public NoteCardViewModel(NoteModel model)
        {
            Model = model;
            Id = model.Id;
            Recompute();
        }

        public int Id { get; }

        public NoteModel Model { get; private set; }

        public bool IsPinned => Model.IsPinned;
        public bool IsTrashed => Model.DeletedUtc != null;
        public string Content => Model.Content ?? string.Empty;

        public NoteColor Colors { get; private set; } = NotePalette.Yellow;
        public string Title { get; private set; } = string.Empty;
        public string Snippet { get; private set; } = string.Empty;
        public bool HasSnippet => Snippet.Length > 0;
        public string MetaLine { get; private set; } = string.Empty;

        // Sort keys.
        public string SortEdited => Model.ModifiedUtc ?? string.Empty;
        public string SortCreated => Model.CreatedUtc ?? string.Empty;
        public int SortColourIndex => NotePalette.IndexOf(Colors);

        private bool isOpen;
        public bool IsOpen
        {
            get => isOpen;
            set => Set(ref isOpen, value);
        }

        public void Update(NoteModel model)
        {
            Model = model;
            Recompute();
            RaisePropertyChanged(string.Empty);
        }

        public void RefreshTime()
        {
            MetaLine = BuildMeta();
            RaisePropertyChanged(nameof(MetaLine));
        }

        private void Recompute()
        {
            Colors = NotePalette.Resolve(Model.Color);
            Title = NoteText.Title(Model.Content);
            Snippet = NoteText.Snippet(Model.Content);
            MetaLine = BuildMeta();
        }

        private string BuildMeta()
        {
            var parts = new List<string>();
            var when = BackupService.TryParse(IsTrashed ? Model.DeletedUtc : Model.ModifiedUtc);
            if (when != null)
            {
                parts.Add((IsTrashed ? "Deleted " : "") + RelativeTime.Format(when.Value));
            }
            int words = NoteText.WordCount(Model.Content);
            parts.Add(words == 1 ? "1 word" : $"{words} words");
            var (done, total) = NoteText.Checklist(Model.Content);
            if (total > 0)
            {
                parts.Add($"☑ {done}/{total}");
            }
            return string.Join("  ·  ", parts);
        }

        public bool Matches(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            query = query.Trim();
            return Content.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || Colors.Name.Equals(query, StringComparison.CurrentCultureIgnoreCase);
        }
    }
}
