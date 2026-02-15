using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Top_Note.Converters
{
    public class StringToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var s = value as string;
            if (string.IsNullOrWhiteSpace(s))
                return Brushes.Transparent;

            try
            {
                var brush = (Brush)new BrushConverter().ConvertFromString(s);
                return brush ?? Brushes.Transparent;
            }
            catch
            {
                return Brushes.Transparent;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}