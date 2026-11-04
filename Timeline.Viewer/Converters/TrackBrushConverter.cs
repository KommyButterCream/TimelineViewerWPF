using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Timeline.Viewer.Rendering;

namespace Timeline.Viewer.Converters
{
    public sealed class TrackBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is string id ? TrackPalette.GetBrush(id) : DependencyProperty.UnsetValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
