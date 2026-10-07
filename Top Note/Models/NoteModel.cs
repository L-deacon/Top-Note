namespace Top_Note.Models
{
    public class NoteModel
    {
        public int Id { get; set; }
        public string Content { get; set; } = string.Empty;
        public bool IsPinned { get; set; }
        public string Color { get; set; } = NotePalette.DefaultHex;
        public double Left { get; set; }
        public double Top { get; set; }
        public int FontSize { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsCollapsed { get; set; }

        // ISO-8601 UTC ("o" format). NULL for notes saved by older versions.
        public string? CreatedUtc { get; set; }
        public string? ModifiedUtc { get; set; }
        public string? DeletedUtc { get; set; }

        // First non-blank line without checklist/bullet markers. Not a DB column (Dapper ignores get-only properties).
        public string Title => NoteText.Title(Content);

        public string Preview => Title;
    }

    public static class NoteText
    {
        public const string Unchecked = "☐ ";
        public const string Checked = "☑ ";
        public const string Bullet = "• ";

        public static IEnumerable<string> Lines(string? content) =>
            (content ?? string.Empty).Replace("\r", string.Empty).Split('\n');

        public static string StripMarker(string line)
        {
            var trimmed = line.Trim();
            foreach (var marker in new[] { Unchecked, Checked, Bullet })
            {
                if (trimmed.StartsWith(marker, StringComparison.Ordinal)) return trimmed[marker.Length..].Trim();
            }
            return trimmed;
        }

        public static string Title(string? content)
        {
            var line = Lines(content).Select(StripMarker).FirstOrDefault(l => l.Length > 0);
            if (line == null) return "Untitled note";
            return line.Length > 60 ? line[..60] + "…" : line;
        }

        public static string Snippet(string? content)
        {
            var lines = Lines(content).Where(l => l.Trim().Length > 0).Skip(1).Take(2).Select(l => l.Trim());
            return string.Join("  ·  ", lines);
        }

        public static int WordCount(string? content) =>
            (content ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Count(w => w != "☐" && w != "☑" && w != "•");

        public static (int done, int total) Checklist(string? content)
        {
            int done = 0, total = 0;
            foreach (var line in Lines(content))
            {
                var t = line.TrimStart();
                if (t.StartsWith(Checked, StringComparison.Ordinal)) { done++; total++; }
                else if (t.StartsWith(Unchecked, StringComparison.Ordinal)) total++;
            }
            return (done, total);
        }
    }
}
