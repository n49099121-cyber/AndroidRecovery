using System.Reflection;
using Microsoft.UI.Xaml.Controls;

namespace AndroidRecovery.UI;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
        VersionValue.Text = $"Version {Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "Unknown"}";
    }
}