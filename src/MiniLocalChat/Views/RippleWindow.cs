using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using MiniLocalChat.Infrastructure;

namespace MiniLocalChat.Views;

/// <summary>
/// Transparent, click-through overlay covering one monitor. Plays a Material (MUI) style ripple:
/// one translucent filled wave spreading out from a point (the LED), then fading. Closes itself when done.
/// </summary>
public sealed class RippleWindow : Window
{
    private static readonly TimeSpan ExpandDuration = TimeSpan.FromMilliseconds(1000);
    private static readonly TimeSpan FadeStart = TimeSpan.FromMilliseconds(550);
    private static readonly TimeSpan FadeEnd = TimeSpan.FromMilliseconds(1300);

    // MUI's ripple easing: cubic-bezier(0.4, 0, 0.2, 1).
    private static readonly KeySpline MuiEasing = new(0.4, 0, 0.2, 1);

    // Soft green fill, a little stronger toward the wave front, feathered at the very edge (no hard line).
    private static readonly Brush WaveBrush = CreateWaveBrush();

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

    /// <param name="origin">Wave center, in this window's coordinates (may lie outside it, on another monitor).</param>
    public void Play(Point origin)
    {
        Show();

        // Big enough to cover the farthest corner of this monitor.
        var radius = new[] { new Point(0, 0), new Point(Width, 0), new Point(0, Height), new Point(Width, Height) }
            .Max(corner => (corner - origin).Length) + 60;

        var scale = new ScaleTransform(0, 0, radius, radius);
        var wave = new Ellipse
        {
            Width = radius * 2,
            Height = radius * 2,
            Fill = WaveBrush,
            RenderTransform = scale,
        };
        Canvas.SetLeft(wave, origin.X - radius);
        Canvas.SetTop(wave, origin.Y - radius);
        _canvas.Children.Add(wave);

        var expand = new DoubleAnimationUsingKeyFrames();
        expand.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        expand.KeyFrames.Add(new SplineDoubleKeyFrame(1, KeyTime.FromTimeSpan(ExpandDuration), MuiEasing));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, expand);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, expand);

        var fade = new DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(FadeStart)));
        fade.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromTimeSpan(FadeEnd), MuiEasing));
        fade.Completed += (_, _) => Close();
        wave.BeginAnimation(OpacityProperty, fade);
    }

    private static Brush CreateWaveBrush()
    {
        var brush = new RadialGradientBrush();
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x24, 0x22, 0xC5, 0x5E), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x38, 0x22, 0xC5, 0x5E), 0.75));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x5C, 0x22, 0xC5, 0x5E), 0.93));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0x22, 0xC5, 0x5E), 1.0));
        brush.Freeze();
        return brush;
    }
}
