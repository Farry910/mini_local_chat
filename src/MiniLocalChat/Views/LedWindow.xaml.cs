using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MiniLocalChat.Infrastructure;
using MiniLocalChat.Services;
using Screen = System.Windows.Forms.Screen;

namespace MiniLocalChat.Views;

public enum LedSide
{
    Left,
    Right,
}

/// <summary>
/// The always-on-top LED shown while the app sits in the tray. Dark when there's nothing new,
/// glowing green with the unread count when messages arrive. Drag it up/down or to the other side
/// of the screen; it snaps to the nearest edge.
/// </summary>
public partial class LedWindow : Window
{
    private const double EdgeMargin = 4;
    private const double DragThreshold = 5;

    // See-through at rest so it doesn't cover what's underneath; fully opaque while hovered.
    private const double IdleOpacityOff = 0.35;
    private const double IdleOpacityOn = 0.75;
    private const int PulseCount = 3;

    private static readonly Brush OffBody = Radial(("#A3A9B3", 0), ("#5F6670", 0.55), ("#3A3F46", 1));
    private static readonly Brush OffStroke = Solid("#2A2E33");
    private static readonly Brush OnBody = Radial(("#EAFFE8", 0), ("#6EE787", 0.3), ("#22C55E", 0.65), ("#15803D", 1));
    private static readonly Brush OnStroke = Solid("#166534");

    private readonly ChatService _chat = App.Chat;
    private readonly DispatcherTimer _topmostTimer;
    private Point _downPoint;
    private bool _pressed;
    private int _shownCount = -1;

    public LedWindow()
    {
        InitializeComponent();
        _topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _topmostTimer.Tick += (_, _) => Win32.KeepOnTop(this);
        SourceInitialized += (_, _) => Win32.MakeToolWindow(this);
        IsVisibleChanged += (_, _) => _topmostTimer.IsEnabled = IsVisible;
        _chat.PropertyChanged += OnChatPropertyChanged;
        UpdateState();
    }

    public event EventHandler? Clicked;

    public event EventHandler? DoubleClicked;

    public event EventHandler? DragStarted;

    public event EventHandler? OpenRequested;

    public event EventHandler? ExitRequested;

    public LedSide Side { get; private set; } = LedSide.Right;

    public bool IsDragging { get; private set; }

    /// <summary>Work area (DIPs) of the monitor the LED is on.</summary>
    public Rect WorkArea => WorkAreaAt(new Point(Left + Width / 2, Top + Height / 2));

    public void ShowAtSavedPosition()
    {
        if (!IsVisible)
        {
            Show();
        }

        PlaceFromSettings();
        Win32.KeepOnTop(this);
    }

    public void PlaceFromSettings()
    {
        var settings = _chat.Settings;
        Side = settings.LedSide == nameof(LedSide.Left) ? LedSide.Left : LedSide.Right;
        var screen = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == settings.LedScreen) ?? Screen.PrimaryScreen;
        var area = screen is null ? SystemParameters.WorkArea : ToDips(screen.WorkingArea);
        Place(area, area.Top + settings.LedOffsetY);
    }

    private void Place(Rect area, double top)
    {
        Left = Side == LedSide.Left ? area.Left + EdgeMargin : area.Right - Width - EdgeMargin;
        var minTop = area.Top + EdgeMargin;
        var maxTop = Math.Max(minTop, area.Bottom - Height - EdgeMargin);
        Top = Math.Clamp(top, minTop, maxTop);
    }

    private void SnapToEdge()
    {
        var center = new Point(Left + Width / 2, Top + Height / 2);
        var area = WorkAreaAt(center);
        Side = center.X < area.Left + area.Width / 2 ? LedSide.Left : LedSide.Right;
        Place(area, Top);

        var settings = _chat.Settings;
        settings.LedSide = Side.ToString();
        settings.LedOffsetY = Top - area.Top;
        settings.LedScreen = ScreenAt(center)?.DeviceName;
        _chat.SaveSettings();
    }

    private double Scale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    private Rect ToDips(System.Drawing.Rectangle r)
    {
        var s = Scale;
        return new Rect(r.Left / s, r.Top / s, r.Width / s, r.Height / s);
    }

    private Screen? ScreenAt(Point dip)
    {
        var s = Scale;
        return Screen.FromPoint(new System.Drawing.Point((int)(dip.X * s), (int)(dip.Y * s)));
    }

    private Rect WorkAreaAt(Point dip) =>
        ScreenAt(dip) is { } screen ? ToDips(screen.WorkingArea) : SystemParameters.WorkArea;

    // ---- Visual state ---------------------------------------------------------------------------

    private void OnChatPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatService.TotalUnread))
        {
            UpdateState();
        }
    }

    private void UpdateState()
    {
        var count = _chat.TotalUnread;
        var wasOn = _shownCount > 0;
        var grew = _shownCount >= 0 && count > _shownCount;
        _shownCount = count;

        if (count > 0)
        {
            Body.Fill = OnBody;
            Body.Stroke = OnStroke;
            CountText.Text = count > 99 ? "99+" : count.ToString();
            CountText.FontSize = count > 99 ? 4.5 : count > 9 ? 6.5 : 8;
            CountText.Visibility = Visibility.Visible;
            ToolTip = null;
            if (!wasOn || grew)
            {
                Pulse();
            }

            if (grew)
            {
                Bump();
            }
        }
        else
        {
            Body.Fill = OffBody;
            Body.Stroke = OffStroke;
            CountText.Visibility = Visibility.Collapsed;
            Glow.BeginAnimation(OpacityProperty, null);
            Glow.Opacity = 0;
        }

        UpdateOpacity();
    }

    private void UpdateOpacity()
    {
        var target = IsMouseOver ? 1.0 : _shownCount > 0 ? IdleOpacityOn : IdleOpacityOff;
        Visual.BeginAnimation(OpacityProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(150)));
    }

    private void OnHoverChanged(object sender, MouseEventArgs e) => UpdateOpacity();

    /// <summary>
    /// Pulses the glow a few times when something new arrives, then holds a steady glow. A pulse that
    /// never stops kept WPF redrawing ~60 times a second for as long as anything was unread (~10% of a CPU core).
    /// </summary>
    private void Pulse()
    {
        var pulse = new DoubleAnimation(0.45, 1.0, TimeSpan.FromSeconds(1.1))
        {
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(PulseCount),
            FillBehavior = FillBehavior.Stop,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        Timeline.SetDesiredFrameRate(pulse, 30);
        Glow.Opacity = 1.0;
        Glow.BeginAnimation(OpacityProperty, pulse);
    }

    private void Bump()
    {
        var bump = new DoubleAnimation(1, 1.3, TimeSpan.FromMilliseconds(160))
        {
            AutoReverse = true,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        BulbScale.BeginAnimation(ScaleTransform.ScaleXProperty, bump);
        BulbScale.BeginAnimation(ScaleTransform.ScaleYProperty, bump);
    }

    // ---- Mouse: click, double-click, drag -------------------------------------------------------

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _pressed = false;
            ReleaseMouseCapture();
            DoubleClicked?.Invoke(this, EventArgs.Empty);
            return;
        }

        _pressed = true;
        _downPoint = e.GetPosition(this);
        CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_pressed || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var p = e.GetPosition(this);
        if (Math.Abs(p.X - _downPoint.X) < DragThreshold && Math.Abs(p.Y - _downPoint.Y) < DragThreshold)
        {
            return;
        }

        _pressed = false;
        ReleaseMouseCapture();
        IsDragging = true;
        DragStarted?.Invoke(this, EventArgs.Empty);
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // Button was released before the move started.
        }
        finally
        {
            IsDragging = false;
        }

        SnapToEdge();
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pressed)
        {
            return;
        }

        _pressed = false;
        ReleaseMouseCapture();
        Clicked?.Invoke(this, EventArgs.Empty);
    }

    private void OnOpenClick(object sender, RoutedEventArgs e) => OpenRequested?.Invoke(this, EventArgs.Empty);

    private void OnExitClick(object sender, RoutedEventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty);

    // ---- Brushes --------------------------------------------------------------------------------

    private static Brush Solid(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private static Brush Radial(params (string Hex, double Offset)[] stops)
    {
        var brush = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.38, 0.32),
            Center = new Point(0.45, 0.42),
            RadiusX = 0.62,
            RadiusY = 0.62,
        };
        foreach (var (hex, offset) in stops)
        {
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(hex), offset));
        }

        brush.Freeze();
        return brush;
    }
}
