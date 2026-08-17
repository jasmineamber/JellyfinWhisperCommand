using System.Windows.Media;

namespace JellyfinWhisperCommand.Controls;

public static class RoundedClipper
{
    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.RegisterAttached(
            "CornerRadius",
            typeof(CornerRadius),
            typeof(RoundedClipper),
            new PropertyMetadata(default(CornerRadius), OnCornerRadiusChanged));

    public static void SetCornerRadius(DependencyObject element, CornerRadius value) => element.SetValue(CornerRadiusProperty, value);
    public static CornerRadius GetCornerRadius(DependencyObject element) => (CornerRadius)element.GetValue(CornerRadiusProperty);

    private static void OnCornerRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        element.SizeChanged += OnElementSizeChanged;
        element.Loaded += OnElementLoaded;
        ApplyClip(element);
    }

    private static void OnElementSizeChanged(object sender, SizeChangedEventArgs e) => ApplyClip((FrameworkElement)sender);

    private static void OnElementLoaded(object sender, RoutedEventArgs e) => ApplyClip((FrameworkElement)sender);

    private static void ApplyClip(FrameworkElement element)
    {
        var radius = GetCornerRadius(element);
        if (radius.TopLeft <= 0 && radius.TopRight <= 0 && radius.BottomLeft <= 0 && radius.BottomRight <= 0)
        {
            element.Clip = null;
            return;
        }
        element.Clip = new RectangleGeometry(new Rect(0, 0, element.ActualWidth, element.ActualHeight),
            radius.TopLeft, radius.TopLeft);
    }
}
