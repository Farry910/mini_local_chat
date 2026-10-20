using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using MiniLocalChat.Controls;
using MiniLocalChat.Models;
using MiniLocalChat.Services;
using Screen = System.Windows.Forms.Screen;

namespace MiniLocalChat.Views;

/// <summary>
/// Someone's profile picture full size, like Telegram's photo viewer: a dark backdrop over the whole
/// screen, the picture in the middle, their name at the top. Mouse wheel / double-click / + − zoom
/// (around the pointer), drag to move when zoomed, Esc or a click on the backdrop closes it.
/// </summary>
public sealed class AvatarViewer : Window
{
    private const double MaxZoom = 6;
    private const double ZoomStep = 1.25;
    private const double DoubleClickZoom = 2.5;

    private readonly Grid _stage = new() { RenderTransformOrigin = new Point(0, 0) };
    private readonly MatrixTransform _transform = new();
    private readonly Image _picture;
    private readonly TextBlock _zoomText;
    private Point _dragStart;
    private Point _lastDrag;
    private bool _dragging;
    private bool _moved;
    private bool _closing;

    private AvatarViewer(Contact person, BitmapSource picture, Rect area)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromArgb(0xE8, 0x10, 0x10, 0x14));
        Left = area.Left;
        Top = area.Top;
        Width = area.Width;
        Height = area.Height;
        Title = person.DisplayName;
        FontFamily = (FontFamily)Application.Current.FindResource("TextFont");

        // Big, but never more than most of the screen; small (old 128 px) pictures are scaled up smoothly.
        var shortSide = Math.Min(area.Width, area.Height);
        var side = Math.Min(shortSide * 0.78, Math.Max(picture.PixelWidth, 420));
        _picture = new Image
        {
            Source = picture,
            Width = side,
            Height = side,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
        };
        RenderOptions.SetBitmapScalingMode(_picture, BitmapScalingMode.HighQuality);
        _picture.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 30, ShadowDepth = 0, Opacity = 0.5 };
        _stage.Children.Add(_picture);
        _stage.RenderTransform = _transform;

        _zoomText = new TextBlock { Foreground = Brushes.White, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), Opacity = 0.8 };

        var root = new Grid();
        root.Children.Add(_stage);

        // Keeps the name and buttons readable over a zoomed-in picture.
        var shade = new LinearGradientBrush(Color.FromArgb(0xB0, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), 90);
        root.Children.Add(new Border { Height = 110, VerticalAlignment = VerticalAlignment.Top, Background = shade, IsHitTestVisible = false });
        root.Children.Add(Header(person));
        root.Children.Add(Toolbar());
        Content = root;

        KeyDown += OnKey;
        MouseWheel += (_, e) => ZoomAt(e.GetPosition(this), e.Delta > 0 ? ZoomStep : 1 / ZoomStep);
        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        Closing += (_, _) => _closing = true;
        Deactivated += (_, _) =>
        {
            if (!_closing)
            {
                Close(); // clicked somewhere else
            }
        };
        Loaded += (_, _) => Appear();
        UpdateZoomText();
    }

    /// <summary>Opens the person's picture full size (does nothing if they have none).</summary>
    public static void Show(Window? owner, Contact person)
    {
        if (AvatarStore.FullImageFor(person.AvatarId) is not { } picture)
        {
            return;
        }

        var viewer = new AvatarViewer(person, picture, ScreenOf(owner));
        if (owner is { IsVisible: true })
        {
            viewer.Owner = owner;
        }

        viewer.Show();
        viewer.Activate();
    }

    /// <summary>The whole monitor the window is on, in DIPs.</summary>
    private static Rect ScreenOf(Window? owner)
    {
        var dpi = VisualTreeHelper.GetDpi(owner ?? (Visual)new DrawingVisual());
        var screen = owner is not null
            ? Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(owner).Handle)
            : Screen.PrimaryScreen!;
        var b = screen.Bounds;
        return new Rect(b.Left / dpi.DpiScaleX, b.Top / dpi.DpiScaleY, b.Width / dpi.DpiScaleX, b.Height / dpi.DpiScaleY);
    }

    private FrameworkElement Header(Contact person)
    {
        var avatar = new Avatar
        {
            Width = 40,
            Height = 40,
            Margin = new Thickness(0, 0, 12, 0),
            Text = person.Initials,
            Fill = person.AvatarBrush,
            Image = person.AvatarImage,
        };
        var name = new TextBlock { Text = person.DisplayName, Foreground = Brushes.White, FontSize = 16, FontWeight = FontWeights.SemiBold };
        var detail = new TextBlock
        {
            Text = $"{(person.IsOnline ? person.IsAway ? "Away" : "Online" : "Offline")} · {person.Ip}",
            Foreground = new SolidColorBrush(Color.FromRgb(0xB8, 0xB8, 0xC0)),
            FontSize = 12,
        };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(name);
        text.Children.Add(detail);
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(24, 20, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        header.Children.Add(avatar);
        header.Children.Add(text);
        return header;
    }

    private FrameworkElement Toolbar()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 20, 20, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        bar.Children.Add(_zoomText);
        bar.Children.Add(ToolButton("", "Zoom out (−)", () => ZoomAt(Center, 1 / ZoomStep)));
        bar.Children.Add(ToolButton("", "Zoom in (+)", () => ZoomAt(Center, ZoomStep)));
        bar.Children.Add(ToolButton("", "Close (Esc)", Close));
        return bar;
    }

    private Button ToolButton(string glyph, string tip, Action action)
    {
        var button = new Button
        {
            Content = glyph,
            ToolTip = tip,
            Width = 40,
            Height = 40,
            Margin = new Thickness(4, 0, 0, 0),
            FontSize = 16,
            Foreground = Brushes.White,
            Style = (Style)Application.Current.FindResource("IconButton"),
        };
        button.Click += (_, e) =>
        {
            e.Handled = true;
            action();
        };
        return button;
    }

    private Point Center => new(ActualWidth / 2, ActualHeight / 2);

    private double Zoom => _transform.Matrix.M11;

    private void Appear()
    {
        Opacity = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        var pop = new ScaleTransform(0.9, 0.9);
        _picture.RenderTransformOrigin = new Point(0.5, 0.5);
        _picture.RenderTransform = pop;
        var grow = new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        pop.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        pop.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
    }

    /// <summary>Zooms by <paramref name="factor"/>, keeping the point under <paramref name="at"/> in place.</summary>
    private void ZoomAt(Point at, double factor)
    {
        var target = Math.Clamp(Zoom * factor, 1, MaxZoom);
        if (target <= 1.001)
        {
            _transform.Matrix = Matrix.Identity; // back to fitting the screen, centered
        }
        else
        {
            var matrix = _transform.Matrix;
            var step = target / Zoom;
            matrix.ScaleAt(step, step, at.X, at.Y);
            _transform.Matrix = matrix;
        }

        UpdateZoomText();
    }

    private void UpdateZoomText() => _zoomText.Text = $"{Zoom * 100:0}%";

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.Add or Key.OemPlus:
                ZoomAt(Center, ZoomStep);
                break;
            case Key.Subtract or Key.OemMinus:
                ZoomAt(Center, 1 / ZoomStep);
                break;
            case Key.D0 or Key.NumPad0:
                ZoomAt(Center, 0);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindAncestor<Button>(source) is not null)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ZoomAt(e.GetPosition(this), Zoom > 1.001 ? 0 : DoubleClickZoom);
            _dragging = false;
            return;
        }

        _dragStart = _lastDrag = e.GetPosition(this);
        _dragging = true;
        _moved = false;
        CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        var position = e.GetPosition(this);
        if ((position - _dragStart).Length > 4)
        {
            _moved = true;
        }

        if (Zoom > 1.001)
        {
            var matrix = _transform.Matrix;
            matrix.Translate(position.X - _lastDrag.X, position.Y - _lastDrag.Y);
            _transform.Matrix = matrix;
        }

        _lastDrag = position;
    }

    // A click (not a drag) outside the picture closes the viewer, like Telegram.
    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        ReleaseMouseCapture();
        if (!_moved && !_picture.IsMouseOver)
        {
            Close();
        }
    }

    private static T? FindAncestor<T>(DependencyObject node) where T : DependencyObject
    {
        for (var current = node; current is not null; current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }
}
