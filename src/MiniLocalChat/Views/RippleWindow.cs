using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using MiniLocalChat.Infrastructure;

namespace MiniLocalChat.Views;

/// <summary>
/// Transparent, click-through overlay covering one monitor. Plays a Material (MUI) style ripple:
/// two translucent filled waves in the sender's two colors, spreading out from a point (the LED)
/// one right after the other, then fading. Hides itself when done and is reused for the next ripple.
/// </summary>
public sealed class RippleWindow : Window
{
    private static readonly TimeSpan ExpandDuration = TimeSpan.FromMilliseconds(1000);
    private static readonly TimeSpan FadeStart = TimeSpan.FromMilliseconds(550);
    private static readonly TimeSpan FadeEnd = TimeSpan.FromMilliseconds(1300);
    private static readonly TimeSpan SecondWaveDelay = TimeSpan.FromMilliseconds(300);

    // MUI's ripple easing: cubic-bezier(0.4, 0, 0.2, 1).
    private static readonly KeySpline MuiEasing = new(0.4, 0, 0.2, 1);

    private readonly Canvas _canvas = new() { IsHitTestVisible = false };
    private int _playId;

    public RippleWindow(Rect bounds)
    {
        Bounds = bounds;
        // Transparency comes from Win32.MakeCompositedOverlay, not AllowsTransparency, which would copy
        // every full-screen frame from the GPU back to the CPU.
        WindowStyle = WindowStyle.None;
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
        SourceInitialized += (_, _) =>
        {
            Win32.MakeCompositedOverlay(this);
            Win32.MakeClickThroughOverlay(this);
        };
    }

    /// <summary>The monitor area this overlay covers, in DIPs.</summary>
    public Rect Bounds { get; }

    /// <param name="origin">Wave center, in this window's coordinates (may lie outside it, on another monitor).</param>
    public void Play(Point origin, Color first, Color second)
    {
        var playId = ++_playId;
        _canvas.Children.Clear();
        if (!IsVisible)
        {
            Show();
        }

        // Back on top of everything, including a full-screen window that came to the front since the last ripple.
        Win32.KeepOnTop(this);

        // Big enough to cover the farthest corner of this monitor.
        var radius = new[] { new Point(0, 0), new Point(Width, 0), new Point(0, Height), new Point(Width, Height) }
            .Max(corner => (corner - origin).Length) + 60;

        AddWave(origin, radius, first, TimeSpan.Zero, onDone: null);
        AddWave(origin, radius, second, SecondWaveDelay, onDone: () =>
        {
            // A newer ripple may have started on this window in the meantime.
            if (playId == _playId)
            {
                _canvas.Children.Clear();
                Hide();
            }
        });
    }

    private void AddWave(Point origin, double radius, Color color, TimeSpan delay, Action? onDone)
    {
        var scale = new ScaleTransform(0, 0, radius, radius);
        var wave = new Ellipse
        {
            Width = radius * 2,
            Height = radius * 2,
            Fill = CreateWaveBrush(color),
            RenderTransform = scale,
            Opacity = 0,
        };
        Canvas.SetLeft(wave, origin.X - radius);
        Canvas.SetTop(wave, origin.Y - radius);
        _canvas.Children.Add(wave);

        var expand = new DoubleAnimationUsingKeyFrames { BeginTime = delay };
        expand.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        expand.KeyFrames.Add(new SplineDoubleKeyFrame(1, KeyTime.FromTimeSpan(ExpandDuration), MuiEasing));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, expand);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, expand);

        var fade = new DoubleAnimationUsingKeyFrames { BeginTime = delay };
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(FadeStart)));
        fade.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromTimeSpan(FadeEnd), MuiEasing));
        if (onDone is not null)
        {
            fade.Completed += (_, _) => onDone();
        }

        wave.BeginAnimation(OpacityProperty, fade);
    }

    // Soft fill, a little stronger toward the wave front, feathered at the very edge (no hard line).
    private static Brush CreateWaveBrush(Color color)
    {
        var brush = new RadialGradientBrush();
        brush.GradientStops.Add(new GradientStop(WithAlpha(color, 0x24), 0.0));
        brush.GradientStops.Add(new GradientStop(WithAlpha(color, 0x38), 0.75));
        brush.GradientStops.Add(new GradientStop(WithAlpha(color, 0x5C), 0.93));
        brush.GradientStops.Add(new GradientStop(WithAlpha(color, 0x00), 1.0));
        brush.Freeze();
        return brush;
    }

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);
}
