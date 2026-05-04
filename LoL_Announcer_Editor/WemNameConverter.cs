using System;
using System.Globalization;
using System.Windows.Data;

namespace LoL_Announcer_Editor
{
    public class WemNameConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            string name = values[0] as string ?? string.Empty;
            string? replacement = values[1] as string;
            if (!string.IsNullOrEmpty(replacement))
                return $"{name} -> {replacement}";
            return name;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 
