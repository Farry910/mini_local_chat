using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using MiniLocalChat.Controls;
using MiniLocalChat.Infrastructure;
using MiniLocalChat.Models;
using MiniLocalChat.Services;
using XamlAnimatedGif;
using Screen = System.Windows.Forms.Screen;

namespace MiniLocalChat.Views;

/// <summary>
/// Pictures full size, like Telegram's photo viewer: a dark backdrop over the whole screen, the picture in
/// the middle, whose it is at the top. Shows someone's profile picture, or a picture or GIF from a chat;
/// then ← → (or the arrows at the sides) go through the chat's other pictures, and it can be saved.
/// Mouse wheel / double-click / + − zoom (around the pointer), drag to move when zoomed, Esc or a click
/// on the backdrop closes it.
/// </summary>
public sealed class ImageViewer : Window
{
    private const double MaxZoom = 6;
    private const double ZoomStep = 1.25;
    private const double DoubleClickZoom = 2.5;

    /// <summary>Profile pictures are small (old ones 128 px): shown at least this big, scaled up smoothly.</summary>
    private const double MinAvatarSide = 420;

    private readonly Rect _area;
    private readonly IReadOnlyList<ChatMessage> _album;
    private readonly Grid _stage = new() { RenderTransformOrigin = new Point(0, 0) };
    private readonly MatrixTransform _transform = new();
    private readonly Image _picture;
    private readonly TextBlock _problem;
    private readonly StackPanel _header;
    private readonly TextBlock _zoomText;
    private readonly TextBlock _counterText;
    private readonly StackPanel _fileButtons;
    private readonly Button _previous;
    private readonly Button _next;
    private int _index;
    private Point _dragStart;
    private Point _lastDrag;
    private bool _dragging;
    private bool _moved;
    private bool _closing;
    private bool _inDialog;

    private ImageViewer(Rect area, IReadOnlyList<ChatMessage> album)
    {
        _area = area;
        _album = album;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromArgb(0xE8, 0x10, 0x10, 0x14));
        Left = area.Left;
        Top = area.Top;
        Width = area.Width;
        Height = area.Height;
        FontFamily = (FontFamily)Application.Current.FindResource("TextFont");

        _picture = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
        };
        RenderOptions.SetBitmapScalingMode(_picture, BitmapScalingMode.HighQuality);
        _picture.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 30, ShadowDepth = 0, Opacity = 0.5 };
        _stage.Children.Add(_picture);
        _stage.RenderTransform = _transform;

        _problem = new TextBlock
        {
            Text = "This picture can't be shown.",
            Foreground = Brushes.White,
            FontSize = 14,
            Opacity = 0.8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };

        _header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(24, 20, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        _zoomText = new TextBlock { Foreground = Brushes.White, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), Opacity = 0.8 };
        _counterText = new TextBlock { Foreground = Brushes.White, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0), Opacity = 0.8 };
        _fileButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0) };
        _fileButtons.Children.Add(ToolButton("", "Save as…", SaveAs));
        _fileButtons.Children.Add(ToolButton("", "Show in folder", ShowInFolder));
        _fileButtons.Children.Add(ToolButton("", "Open in another app", OpenElsewhere));
        _previous = SideButton("", "Previous picture (←)", HorizontalAlignment.Left, () => Go(-1));
        _next = SideButton("", "Next picture (→)", HorizontalAlignment.Right, () => Go(1));

        var root = new Grid();
        root.Children.Add(_stage);
        root.Children.Add(_problem);

        // Keeps the name and buttons readable over a zoomed-in picture.
        var shade = new LinearGradientBrush(Color.FromArgb(0xB0, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), 90);
        root.Children.Add(new Border { Height = 110, VerticalAlignment = VerticalAlignment.Top, Background = shade, IsHitTestVisible = false });
        root.Children.Add(_header);
        root.Children.Add(Toolbar());
        root.Children.Add(_previous);
        root.Children.Add(_next);
        Content = root;

        KeyDown += OnKey;
        MouseWheel += (_, e) => ZoomAt(e.GetPosition(this), e.Delta > 0 ? ZoomStep : 1 / ZoomStep);
        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        Closing += (_, _) => _closing = true;
        Closed += (_, _) => AnimationBehavior.SetSourceUri(_picture, null); // stops a GIF and lets go of its file
        Deactivated += (_, _) =>
        {
            if (!_closing && !_inDialog)
            {
                Close(); // clicked somewhere else
            }
        };
        Loaded += (_, _) => Appear();
    }

    /// <summary>Opens the person's picture full size (does nothing if they have none).</summary>
    public static void Show(Window? owner, Contact person)
    {
        if (AvatarStore.FullImageFor(person.AvatarId) is not { } picture)
        {
            return;
        }

        var viewer = new ImageViewer(ScreenOf(owner), []);
        viewer.Title = person.DisplayName;
        viewer.SetHeader(person, person.DisplayName, $"{(person.IsOnline ? person.IsAway ? "Away" : "Online" : "Offline")} · {person.Ip}");
        viewer._fileButtons.Visibility = Visibility.Collapsed;
        viewer.SetPicture(picture, null, picture.PixelWidth, picture.PixelHeight, MinAvatarSide);
        viewer.UpdateNavigation();
        Open(viewer, owner);
    }

    /// <summary>
    /// Opens a picture or GIF from a chat; ← → go through the chat's other pictures. Returns false if it
    /// can't be shown here (its file is gone, or it's a format Windows can't decode), so the caller can fall back.
    /// </summary>
    public static bool Show(Window? owner, Conversation? conversation, ChatMessage message)
    {
        if (message.LocalPath is not { } path || PixelSize(path) is null)
        {
            return false;
        }

        // The chat's pictures in order (not stickers), so the arrows can page through them.
        var album = conversation?.Messages
            .Where(m => m.Kind == MessageKind.Image && !m.IsDeleted && !m.IsSticker && m.LocalPath is { } p && File.Exists(p))
            .ToList() ?? [];
        if (!album.Contains(message))
        {
            album = [message];
        }

        var viewer = new ImageViewer(ScreenOf(owner), album);
        viewer.ShowAt(album.IndexOf(message));
        Open(viewer, owner);
        return true;
    }

    private static void Open(ImageViewer viewer, Window? owner)
    {
        // Owned by the main window (it closes along with it). The floating panel hides when the viewer takes
        // focus, and would take an owned viewer with it, so from there the viewer stands alone, on top.
        if (owner is MainWindow { IsVisible: true })
        {
            viewer.Owner = owner;
        }
        else
        {
            viewer.Topmost = true;
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

    private ChatMessage? Current => _index >= 0 && _index < _album.Count ? _album[_index] : null;

    /// <summary>Shows the album's picture at <paramref name="index"/>, with its sender and time at the top.</summary>
    private void ShowAt(int index)
    {
        _index = index;
        var message = _album[index];
        var sender = message.IsMine ? App.Chat.Me : message.Sender ?? new Contact { Ip = message.SenderIp };
        var time = new MessageTimeConverter().Convert(message.SentUtc, typeof(string), null!, CultureInfo.CurrentCulture);
        Title = message.FileName ?? "Picture";
        SetHeader(sender, message.IsMine ? "You" : sender.DisplayName, $"{time} · {message.FileName}");

        var path = message.LocalPath!;
        var gif = GifLibrary.IsGif(path);
        if (Load(path, gif, MaxDecodePixels) is { } loaded)
        {
            SetPicture(loaded.Still, gif ? new Uri(path) : null, loaded.Width, loaded.Height, 0);
        }
        else
        {
            SetPicture(null, null, 0, 0, 0);
        }

        UpdateNavigation();
    }

    private void Go(int step)
    {
        var index = _index + step;
        if (index >= 0 && index < _album.Count)
        {
            ShowAt(index);
        }
    }

    private void UpdateNavigation()
    {
        var album = _album.Count > 1;
        _previous.Visibility = album && _index > 0 ? Visibility.Visible : Visibility.Hidden;
        _next.Visibility = album && _index < _album.Count - 1 ? Visibility.Visible : Visibility.Hidden;
        _counterText.Text = album ? $"{_index + 1} of {_album.Count}" : "";
    }

    /// <summary>Puts a picture (a still one, or a GIF that plays) on stage, fitted to the screen and unzoomed.</summary>
    private void SetPicture(BitmapSource? still, Uri? gif, double pixelWidth, double pixelHeight, double minSide)
    {
        _transform.Matrix = Matrix.Identity;
        UpdateZoomText();
        AnimationBehavior.SetSourceUri(_picture, null); // stops the previous GIF, if any
        _picture.Source = still;
        if (gif is not null)
        {
            AnimationBehavior.SetSourceUri(_picture, gif);
        }

        var shown = still is not null || gif is not null;
        _problem.Visibility = shown ? Visibility.Collapsed : Visibility.Visible;
        if (!shown || pixelWidth <= 0 || pixelHeight <= 0)
        {
            _picture.Width = _picture.Height = 0;
            return;
        }

        // Its natural size, shrunk to fit most of the screen (or grown to minSide).
        var dpi = VisualTreeHelper.GetDpi(this);
        var width = pixelWidth / dpi.DpiScaleX;
        var height = pixelHeight / dpi.DpiScaleY;
        var fit = Math.Min(_area.Width * 0.86 / width, _area.Height * 0.8 / height);
        var scale = Math.Min(fit, Math.Max(1, minSide / Math.Min(width, height)));
        _picture.Width = width * scale;
        _picture.Height = height * scale;
    }

    /// <summary>Big enough to stay sharp when zoomed in a bit; a 50-megapixel photo isn't decoded at full size.</summary>
    private int MaxDecodePixels => (int)(_area.Width * VisualTreeHelper.GetDpi(this).DpiScaleX * 2);

    /// <summary>
    /// The picture's size, and for still pictures the picture itself (GIFs are played from the file).
    /// Null if it can't be read.
    /// </summary>
    private static (BitmapSource? Still, int Width, int Height)? Load(string path, bool gif, int maxPixels)
    {
        if (PixelSize(path) is not var (width, height))
        {
            return null;
        }

        if (gif)
        {
            return (null, width, height);
        }

        try
        {

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path);
            if (width > maxPixels)
            {
                bitmap.DecodePixelWidth = maxPixels;
            }

            bitmap.EndInit();
            bitmap.Freeze();
            return (bitmap, width, height);
        }
        catch (Exception ex) when (IsUnreadable(ex))
        {
            return null;
        }
    }

    /// <summary>A picture's size in pixels, read from its header only; null if Windows can't read the file.</summary>
    private static (int Width, int Height)? PixelSize(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            return (frame.PixelWidth, frame.PixelHeight);
        }
        catch (Exception ex) when (IsUnreadable(ex))
        {
            return null;
        }
    }

    private static bool IsUnreadable(Exception ex) =>
        ex is IOException or NotSupportedException or ArgumentException or InvalidOperationException
            or UriFormatException or FileFormatException or UnauthorizedAccessException;

    private void SetHeader(Contact person, string name, string detail)
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
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = name, Foreground = Brushes.White, FontSize = 16, FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock { Text = detail, Foreground = new SolidColorBrush(Color.FromRgb(0xB8, 0xB8, 0xC0)), FontSize = 12 });
        _header.Children.Clear();
        _header.Children.Add(avatar);
        _header.Children.Add(text);
    }

    private FrameworkElement Toolbar()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 20, 20, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        bar.Children.Add(_counterText);
        bar.Children.Add(_fileButtons);
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

    /// <summary>A round arrow at the left or right edge, for paging through the chat's pictures.</summary>
    private Button SideButton(string glyph, string tip, HorizontalAlignment side, Action action)
    {
        var button = ToolButton(glyph, tip, action);
        button.Width = button.Height = 52;
        button.FontSize = 20;
        button.Margin = new Thickness(24);
        button.HorizontalAlignment = side;
        button.VerticalAlignment = VerticalAlignment.Center;
        button.Background = new SolidColorBrush(Color.FromArgb(0x50, 0, 0, 0));
        button.Visibility = Visibility.Hidden;
        return button;
    }

    private void SaveAs()
    {
        if (Current?.LocalPath is not { } path || !File.Exists(path))
        {
            return;
        }

        var extension = Path.GetExtension(path);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save picture",
            FileName = Current.FileName ?? Path.GetFileName(path),
            DefaultExt = extension,
            Filter = $"Picture (*{extension})|*{extension}|All files|*.*",
        };

        // The file dialog takes the focus; the viewer stays open meanwhile.
        _inDialog = true;
        try
        {
            if (DialogGuard.Run(() => dialog.ShowDialog(this)) == true)
            {
                File.Copy(path, dialog.FileName, overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DialogGuard.Run(() => MessageBox.Show(this, "The picture couldn't be saved there.\n\n" + ex.Message, "Save picture",
                MessageBoxButton.OK, MessageBoxImage.Warning));
        }
        finally
        {
            _inDialog = false;
            Activate();
        }
    }

    private void ShowInFolder()
    {
        if (Current?.LocalPath is { } path && File.Exists(path))
        {
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
    }

    private void OpenElsewhere()
    {
        if (Current?.LocalPath is { } path && File.Exists(path))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
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
            case Key.Left:
                Go(-1);
                break;
            case Key.Right:
                Go(1);
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
