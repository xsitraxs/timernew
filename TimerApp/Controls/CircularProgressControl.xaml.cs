using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace TimerApp.Controls;

/// <summary>
/// Круговой индикатор оставшегося времени.
///
/// Дуга рисуется Path с ArcSegment от верхней точки (-90°) по часовой
/// стрелке. Progress = 1 — полное кольцо, 0 — пустое. Геометрия
/// пересчитывается при смене Progress и размера, поэтому кольцо плавно
/// «тает» вместе с тиками движка в 50 мс.
/// </summary>
public sealed partial class CircularProgressControl : UserControl
{
    private const double StrokeThickness = 10.0;

    public CircularProgressControl()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateArc();
    }

    /// <summary>Доля оставшегося времени: 0..1.</summary>
    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public static readonly DependencyProperty ProgressProperty =
        DependencyProperty.Register(
            nameof(Progress),
            typeof(double),
            typeof(CircularProgressControl),
            new PropertyMetadata(1.0, OnVisualPropertyChanged));

    /// <summary>Цвет дуги (акцентный цвет приложения).</summary>
    public Brush StrokeBrush
    {
        get => (Brush)GetValue(StrokeBrushProperty);
        set => SetValue(StrokeBrushProperty, value);
    }

    public static readonly DependencyProperty StrokeBrushProperty =
        DependencyProperty.Register(
            nameof(StrokeBrush),
            typeof(Brush),
            typeof(CircularProgressControl),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    private static void OnVisualPropertyChanged(
        DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((CircularProgressControl)d).UpdateArc();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateArc();

    private void UpdateArc()
    {
        ProgressPath.Stroke = StrokeBrush;

        double width = ActualWidth > 0 ? ActualWidth : 260;
        double height = ActualHeight > 0 ? ActualHeight : 260;
        double size = Math.Min(width, height);
        double center = size / 2.0;
        double radius = Math.Max(0, center - StrokeThickness / 2.0 - 1);

        TrackEllipse.Width = size;
        TrackEllipse.Height = size;
        ProgressPath.Width = size;
        ProgressPath.Height = size;

        double progress = double.IsNaN(Progress) || double.IsInfinity(Progress)
            ? 0.0
            : Math.Clamp(Progress, 0.0, 1.0);

        if (progress <= 0.001 || radius <= 0)
        {
            ProgressPath.Data = null;
            return;
        }

        var start = new Point(center, center - radius);

        PathGeometry geometry;

        if (progress >= 0.999)
        {
            // Полное кольцо: одна дуга в 360° вырождается, рисуем двумя по 180°.
            var bottom = new Point(center, center + radius);
            geometry = BuildGeometry(start,
                new ArcSegment
                {
                    Point = bottom,
                    Size = new Size(radius, radius),
                    SweepDirection = SweepDirection.Clockwise,
                    IsLargeArc = false
                },
                new ArcSegment
                {
                    Point = start,
                    Size = new Size(radius, radius),
                    SweepDirection = SweepDirection.Clockwise,
                    IsLargeArc = false
                });
        }
        else
        {
            double angle = -Math.PI / 2.0 + progress * Math.PI * 2.0;
            var end = new Point(
                center + radius * Math.Cos(angle),
                center + radius * Math.Sin(angle));

            geometry = BuildGeometry(start,
                new ArcSegment
                {
                    Point = end,
                    Size = new Size(radius, radius),
                    SweepDirection = SweepDirection.Clockwise,
                    IsLargeArc = progress > 0.5
                });
        }

        ProgressPath.Data = geometry;
    }

    private static PathGeometry BuildGeometry(Point start, params ArcSegment[] segments)
    {
        var figure = new PathFigure { StartPoint = start };
        foreach (var segment in segments)
            figure.Segments.Add(segment);

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }
}
