using System.Runtime.InteropServices;
using System.Windows.Media;

namespace Top_Note.Services
{
    // Desktop Window Manager tweaks: dark title bars, rounded corners and tinted borders on Windows 11.
    // Every call is best-effort; on older Windows the attributes are simply ignored.
    public static class Dwm
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWA_CAPTION_COLOR = 35;

        public const int CornerRound = 2;

        private static readonly int Build = Environment.OSVersion.Version.Build;
        public static bool IsWindows11 => Build >= 22000;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private static void Set(IntPtr hwnd, int attribute, int value)
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));
            }
            catch (Exception)
            {
                // dwmapi missing or attribute unsupported: keep the default look.
            }
        }

        private static int ToColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

        public static void SetDarkMode(IntPtr hwnd, bool dark) =>
            Set(hwnd, Build >= 18985 ? DWMWA_USE_IMMERSIVE_DARK_MODE : DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, dark ? 1 : 0);

        public static void SetCorners(IntPtr hwnd, int preference)
        {
            if (IsWindows11) Set(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, preference);
        }

        public static void SetBorderColor(IntPtr hwnd, Color color)
        {
            if (IsWindows11) Set(hwnd, DWMWA_BORDER_COLOR, ToColorRef(color));
        }

        public static void SetCaptionColor(IntPtr hwnd, Color color)
        {
            if (IsWindows11) Set(hwnd, DWMWA_CAPTION_COLOR, ToColorRef(color));
        }
    }
}
