using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace AndroidRecovery.UI;

public sealed class SelectionToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var key = value is true ? "InfoBackgroundBrush" : "SurfaceBrush";
        return Application.Current.Resources.TryGetValue(key, out var resource) && resource is Brush brush
            ? brush
            : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        DependencyProperty.UnsetValue;
}