using System.Windows;
using System.Windows.Media;

namespace Top_Note.Services
{
    public static class ScreenHelper
    {
        // The working area (screen minus taskbar) of the monitor nearest to a window rectangle, in WPF units.
        // Clamping to this, rather than the box around all monitors, means a window can't land in a gap
        // that no monitor actually shows.
        public static Rect NearestWorkingArea(Rect windowDips, Visual dpiSource)
        {
            double sx = 1, sy = 1;
            try
            {
                var dpi = VisualTreeHelper.GetDpi(dpiSource);
                sx = dpi.DpiScaleX;
                sy = dpi.DpiScaleY;
            }
            catch (Exception)
            {
                // Not yet in a visual tree: assume 100%.
            }

            var pixels = new System.Drawing.Rectangle(
                (int)(windowDips.Left * sx), (int)(windowDips.Top * sy),
                Math.Max(1, (int)(windowDips.Width * sx)), Math.Max(1, (int)(windowDips.Height * sy)));
            var area = System.Windows.Forms.Screen.FromRectangle(pixels).WorkingArea;
            return new Rect(area.Left / sx, area.Top / sy, area.Width / sx, area.Height / sy);
        }
    }
}
