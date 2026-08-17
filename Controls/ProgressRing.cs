using System.Windows.Media;
using System.Windows.Media.Animation;

namespace JellyfinWhisperCommand.Controls;

public sealed class ProgressRing : FrameworkElement
{
    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(nameof(IsActive), typeof(bool), typeof(ProgressRing),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender, OnIsActiveChanged));

    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(ProgressRing),
            new FrameworkPropertyMetadata(3.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(ProgressRing),
            new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
    public double StrokeThickness { get => (double)GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    private readonly RotateTransform _rotate = new();
    private Storyboard? _storyboard;

    public ProgressRing()
    {
        RenderTransform = _rotate;
        RenderTransformOrigin = new Point(0.5, 0.5);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0 || StrokeThickness <= 0) return;

        var radius = (size - StrokeThickness) / 2;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var start = PointFromAngle(center, radius, -135);
        var end = PointFromAngle(center, radius, 135);

        var figure = new PathFigure { StartPoint = start, IsFilled = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(radius, radius),
            IsLargeArc = true,
            SweepDirection = SweepDirection.Clockwise
        });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        var pen = new Pen(Foreground, StrokeThickness)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        drawingContext.DrawGeometry(null, pen, geometry);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        _rotate.CenterX = ActualWidth / 2;
        _rotate.CenterY = ActualHeight / 2;
        InvalidateVisual();
    }

    protected override void OnVisualParentChanged(DependencyObject oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        if (IsActive) StartAnimation();
    }

    private static void OnIsActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ring = (ProgressRing)d;
        if (true.Equals(e.NewValue)) ring.StartAnimation();
        else ring.StopAnimation();
    }

    private static Point PointFromAngle(Point center, double radius, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        return new Point(center.X + radius * Math.Sin(radians), center.Y - radius * Math.Cos(radians));
    }

    private void StartAnimation()
    {
        if (_storyboard is not null || !IsVisible) return;

        var animation = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(1.1)))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        _storyboard = new Storyboard();
        Storyboard.SetTarget(animation, _rotate);
        Storyboard.SetTargetProperty(animation, new PropertyPath(RotateTransform.AngleProperty));
        _storyboard.Children.Add(animation);
        _storyboard.Begin();
    }

    private void StopAnimation()
    {
        _storyboard?.Stop();
        _storyboard = null;
        _rotate.Angle = 0;
    }
}
