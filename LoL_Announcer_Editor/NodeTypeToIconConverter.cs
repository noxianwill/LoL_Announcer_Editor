using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace LoL_Announcer_Editor
{
    public class NodeTypeToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is NodeType nodeType)
            {
                string resourceKey = nodeType switch
                {
                    NodeType.Root => "RootIconData",
                    NodeType.Event => "EventIconData",
                    NodeType.Container => "ContainerIconData",
                    NodeType.WemFile => "AudioFileIconData",
                    _ => "RootIconData"
                };

                return Application.Current.TryFindResource(resourceKey) as Geometry;
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
