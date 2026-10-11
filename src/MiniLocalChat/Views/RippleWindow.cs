using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using MiniLocalChat.Infrastructure;
using Path = System.Windows.Shapes.Path;

namespace MiniLocalChat.Views;

/// <summary>
/// Transparent, click-through overlay covering one monitor. Plays green rings spreading out from
/// a point (the LED) and closes itself when done.
/// </summary>
public sealed class RippleWindow : Window
{
    private const int RingCount = 3;
    private static readonly TimeSpan RingDuration = TimeSpan.FromMilliseconds(1600);
    private static readonly TimeSpan RingStagger = TimeSpan.FromMilliseconds(280);
    private static readonly Color RingColor = Color.FromRgb(0x22, 0xC5, 0x5E);

    private readonly Canvas _canvas = new() { IsHitTestVisible = false };

    public RippleWindow(Rect bounds)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
        Content = _canvas;
        SourceInitialized += (_, _) => Win32.MakeClickThrough(this);
    }

    /// <param name="origin">Ring center, in this window's coordinates (may lie outside it, on another monitor).</param>
    public void Play(Point origin)
    {
        Show();

        // Big enough to sweep past the farthest corner of this monitor.
        var maxRadius = new[] { new Point(0, 0), new Point(Width, 0), new Point(0, Height), new Point(Width, Height) }
            .Max(corner => (corner - origin).Length) + 40;

        var brush = new SolidColorBrush(RingColor);
        brush.Freeze();
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        ease.Freeze();

        for (var i = 0; i < RingCount; i++)
        {
            var begin = TimeSpan.FromTicks(RingStagger.Ticks * i);
            var geometry = new EllipseGeometry(origin, 0, 0);
            var ring = new Path { Data = geometry, Stroke = brush, Opacity = 0 };
            _canvas.Children.Add(ring);

            var radius = new DoubleAnimation(0, maxRadius, RingDuration) { BeginTime = begin, EasingFunction = ease };
            geometry.BeginAnimation(EllipseGeometry.RadiusXProperty, radius);
            geometry.BeginAnimation(EllipseGeometry.RadiusYProperty, radius);

            ring.BeginAnimation(Shape.StrokeThicknessProperty, new DoubleAnimation(22, 3, RingDuration) { BeginTime = begin });

            var fade = new DoubleAnimationUsingKeyFrames { BeginTime = begin };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.75 - i * 0.15, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(RingDuration)));
            if (i == RingCount - 1)
            {
                fade.Completed += (_, _) => Close();
            }

            ring.BeginAnimation(OpacityProperty, fade);
        }
    }
}
