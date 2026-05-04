using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LoL_Announcer_Editor.UI.Converters
{
    /// <summary>
    /// Converts a string value to Visibility.Visible if not null/empty, otherwise Visibility.Collapsed.
    /// </summary>
    public class StringVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var str = value as string;
            return !string.IsNullOrEmpty(str) ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 
