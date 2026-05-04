using System;
using System.Globalization;
using System.Windows.Data;

namespace LoL_Announcer_Editor.UI.Converters
{
    /// <summary>
    /// Prefixes a replacement filename with an arrow for UI display.
    /// </summary>
    public class WemArrowConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var replacement = value as string;
            if (!string.IsNullOrEmpty(replacement))
                return $" -> {replacement}";
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 
