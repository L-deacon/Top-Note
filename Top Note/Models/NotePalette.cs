using System.Globalization;
using System.Windows.Media;

namespace Top_Note.Models
{
    // One note colour: paper body, darker header strip, a saturated swatch for dots/rings, and ink for text.
    public sealed class NoteColor
    {
        public NoteColor(string key, string name, string body, string header, string swatch, string ink)
        {
            Key = key;
            Name = name;
            BodyHex = body;
            BodyColor = Parse(body);
            Body = Freeze(new SolidColorBrush(BodyColor));
            HeaderColor = Parse(header);
            Header = Freeze(new SolidColorBrush(HeaderColor));
            Swatch = Freeze(new SolidColorBrush(Parse(swatch)));
            Ink = Freeze(new SolidColorBrush(Parse(ink)));
            bool darkPaper = Luminance(BodyColor) < 0.5;
            ChromeHover = Freeze(new SolidColorBrush(Parse(darkPaper ? "#1AFFFFFF" : "#14000000")));
            ChromePressed = Freeze(new SolidColorBrush(Parse(darkPaper ? "#26FFFFFF" : "#24000000")));
            Divider = Freeze(new SolidColorBrush(Parse(darkPaper ? "#1FFFFFFF" : "#1A000000")));
        }

        public string Key { get; }
        public string Name { get; }
        public string BodyHex { get; }
        public Color BodyColor { get; }
        public Color HeaderColor { get; }
        public SolidColorBrush Body { get; }
        public SolidColorBrush Header { get; }
        public SolidColorBrush Swatch { get; }
        public SolidColorBrush Ink { get; }
        public SolidColorBrush ChromeHover { get; }
        public SolidColorBrush ChromePressed { get; }
        public SolidColorBrush Divider { get; }

        internal static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);

        internal static double Luminance(Color c)
        {
            static double Channel(byte v)
            {
                double s = v / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        }

        private static SolidColorBrush Freeze(SolidColorBrush b)
        {
            b.Freeze();
            return b;
        }
    }

    // The note colours. They don't change with the app theme, like paper sticky notes.
    public static class NotePalette
    {
        public const string DefaultHex = "#FFF7D1";

        public static IReadOnlyList<NoteColor> All { get; } = new[]
        {
            new NoteColor("yellow", "Yellow", "#FFF7D1", "#FFEFA6", "#F5D33F", "#1F1F1F"),
            new NoteColor("orange", "Orange", "#FFE9D4", "#FFD6AD", "#F7A04B", "#1F1F1F"),
            new NoteColor("green", "Green", "#E4F9E0", "#C9F0C0", "#6CCB5F", "#1F1F1F"),
            new NoteColor("pink", "Pink", "#FFE4F1", "#FFCCE4", "#EF6FA7", "#1F1F1F"),
            new NoteColor("purple", "Purple", "#F2E6FF", "#E2CCFF", "#A97CF0", "#1F1F1F"),
            new NoteColor("blue", "Blue", "#E2F1FF", "#C7E3FF", "#4FA3F7", "#1F1F1F"),
            new NoteColor("charcoal", "Charcoal", "#3A3A3A", "#2E2E2E", "#8A8A8A", "#F3F3F3"),
        };

        // Colours saved by earlier versions of Top Note.
        private static readonly Dictionary<string, string> Legacy = new(StringComparer.OrdinalIgnoreCase)
        {
            ["#FFFF00"] = "yellow",
            ["Yellow"] = "yellow",
            ["#FFB026"] = "orange",
            ["#99FFA9"] = "green",
            ["#F7532D"] = "pink",
            ["#E389FA"] = "purple",
            ["#60ABFC"] = "blue",
        };

        private static readonly Dictionary<string, NoteColor> Custom = new(StringComparer.OrdinalIgnoreCase);

        public static NoteColor Yellow => All[0];

        public static NoteColor Resolve(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return Yellow;

            var match = All.FirstOrDefault(c => string.Equals(c.BodyHex, hex, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;

            if (Legacy.TryGetValue(hex, out var key)) return All.First(c => c.Key == key);

            if (Custom.TryGetValue(hex, out var cached)) return cached;

            try
            {
                var body = NoteColor.Parse(hex);
                string header = Shade(body, 0.92);
                string swatch = Shade(body, 0.70);
                string ink = NoteColor.Luminance(body) > 0.5 ? "#1F1F1F" : "#F3F3F3";
                var custom = new NoteColor("custom", "Custom", hex, header, swatch, ink);
                Custom[hex] = custom;
                return custom;
            }
            catch (FormatException)
            {
                return Yellow;
            }
        }

        public static int IndexOf(NoteColor color)
        {
            int i = All.ToList().FindIndex(c => c.Key == color.Key);
            return i < 0 ? All.Count : i;
        }

        private static string Shade(Color c, double factor) =>
            string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}",
                (byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));
    }
}
