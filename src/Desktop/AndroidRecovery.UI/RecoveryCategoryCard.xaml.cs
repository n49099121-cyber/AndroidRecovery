using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;

namespace AndroidRecovery.UI;

public sealed partial class RecoveryCategoryCard : UserControl
{
    public static readonly DependencyProperty CategoryProperty = DependencyProperty.Register(
        nameof(Category), typeof(string), typeof(RecoveryCategoryCard), new PropertyMetadata(""));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(RecoveryCategoryCard), new PropertyMetadata(""));

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(RecoveryCategoryCard), new PropertyMetadata("Document"));

    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register(
        nameof(IsSelected), typeof(bool), typeof(RecoveryCategoryCard), new PropertyMetadata(false));

    public static readonly DependencyProperty InteractionEnabledProperty = DependencyProperty.Register(
        nameof(InteractionEnabled), typeof(bool), typeof(RecoveryCategoryCard), new PropertyMetadata(true));

    public RecoveryCategoryCard()
    {
        InitializeComponent();
    }

    public string Category
    {
        get => (string)GetValue(CategoryProperty);
        set => SetValue(CategoryProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public bool InteractionEnabled
    {
        get => (bool)GetValue(InteractionEnabledProperty);
        set => SetValue(InteractionEnabledProperty, value);
    }
}