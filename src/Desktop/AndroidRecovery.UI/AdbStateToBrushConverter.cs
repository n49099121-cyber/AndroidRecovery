using AndroidRecovery.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace AndroidRecovery.UI;

public sealed class AdbStateToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var key = value is AdbDeviceState state ? state switch
        {
            AdbDeviceState.Device => "SuccessBrush",
            AdbDeviceState.Unauthorized => "WarningBrush",
            AdbDeviceState.Offline => "ErrorBrush",
            _ => "TextSecondaryBrush"
        } : "TextSecondaryBrush";

        return Application.Current.Resources.TryGetValue(key, out var resource) && resource is Brush brush
            ? brush
            : new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        DependencyProperty.UnsetValue;
}