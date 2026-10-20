using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MiniLocalChat.Services;
using SkiaSharp;

namespace MiniLocalChat.Controls;

/// <summary>
/// Plays a Telegram animated sticker (.tgs). The frames are drawn once, in the background, at the size
/// the sticker is shown (sharp at any DPI), and reused by every copy of it. One shared timer moves all
/// visible stickers, and only while their window is on screen. With <see cref="PlayOnHover"/> it shows
/// the first frame and plays only under the mouse (panel tiles). Otherwise it plays <see cref="Loops"/>
/// times when it comes into view, then rests on the first frame (playing costs CPU); hovering replays it.
/// </summary>
public sealed class StickerPlayer : Image
{
    public static readonly DependencyProperty PathProperty = DependencyProperty.Register(
        nameof(Path), typeof(string), typeof(StickerPlayer), new PropertyMetadata(null, (d, _) => ((StickerPlayer)d).Reload()));

    public static readonly DependencyProperty PlayOnHoverProperty = DependencyProperty.Register(
        nameof(PlayOnHover), typeof(bool), typeof(StickerPlayer), new PropertyMetadata(false));

    public static readonly DependencyProperty LoopsProperty = DependencyProperty.Register(
        nameof(Loops), typeof(int), typeof(StickerPlayer), new PropertyMetadata(4));

    private const double FramesPerSecond = 30;
    private const int MaxFrames = 180;
    private const int MaxCachedSizes = 40;

    private static readonly Dictionary<(string Path, int Pixels), Task<BitmapSource[]?>> Cache = new();
    private static readonly HashSet<StickerPlayer> Playing = [];
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static DispatcherTimer? _timer;

    private BitmapSource[]? _frames;
    private long _startedTicks;
    private int _shownFrame = -1;
    private bool _finished;

    public StickerPlayer()
    {
        Stretch = Stretch.Uniform;
        Loaded += (_, _) => Reload();
        Unloaded += (_, _) => Stop();
        IsVisibleChanged += (_, _) =>
        {
            _finished = false; // coming back into view plays it again
            UpdatePlaying();
        };
        MouseEnter += (_, _) =>
        {
            _finished = false;
            UpdatePlaying();
        };
        MouseLeave += (_, _) => UpdatePlaying();
    }

    public string? Path
    {
        get => (string?)GetValue(PathProperty);
        set => SetValue(PathProperty, value);
    }

    public bool PlayOnHover
    {
        get => (bool)GetValue(PlayOnHoverProperty);
        set => SetValue(PlayOnHoverProperty, value);
    }

    /// <summary>How many times it plays before resting (while the mouse isn't on it); 0 = forever.</summary>
    public int Loops
    {
        get => (int)GetValue(LoopsProperty);
        set => SetValue(LoopsProperty, value);
    }

    private async void Reload()
    {
        Stop();
        _frames = null;
        _shownFrame = -1;
        _finished = false;
        Source = null;
        if (!IsLoaded || Path is not { } path || double.IsNaN(Width))
        {
            return;
        }

        var pixels = (int)Math.Ceiling(Width * VisualTreeHelper.GetDpi(this).DpiScaleX);
        var frames = await FramesFor(path, pixels);
        if (path != Path || frames is not { Length: > 0 })
        {
            return; // changed meanwhile, or not a sticker
        }

        _frames = frames;
        ShowFrame(0);
        UpdatePlaying();
    }

    private void UpdatePlaying()
    {
        var play = _frames is { Length: > 1 } && IsVisible && (PlayOnHover ? IsMouseOver : !_finished || IsMouseOver);
        if (!play)
        {
            Stop();
            if (_frames is not null)
            {
                ShowFrame(0);
            }

            return;
        }

        if (Playing.Add(this))
        {
            _startedTicks = Clock.ElapsedTicks;
            _timer ??= CreateTimer();
            _timer.Start();
        }
    }

    private void Stop()
    {
        Playing.Remove(this);
        if (Playing.Count == 0)
        {
            _timer?.Stop();
        }
    }

    private void ShowFrame(int index)
    {
        if (_frames is { } frames && index != _shownFrame)
        {
            _shownFrame = index;
            Source = frames[index];
        }
    }

    private static DispatcherTimer CreateTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromSeconds(1 / FramesPerSecond) };
        timer.Tick += (_, _) =>
        {
            List<StickerPlayer>? finished = null;
            foreach (var player in Playing)
            {
                // Hidden in the tray or minimized: nothing to see.
                if (player._frames is not { } frames || Window.GetWindow(player) is not { IsVisible: true, WindowState: not WindowState.Minimized })
                {
                    continue;
                }

                var seconds = (Clock.ElapsedTicks - player._startedTicks) / (double)Stopwatch.Frequency;
                var frame = (int)(seconds * FramesPerSecond);
                if (!player.PlayOnHover && player.Loops > 0 && !player.IsMouseOver && frame >= player.Loops * frames.Length)
                {
                    player._finished = true;
                    finished ??= [];
                    finished.Add(player);
                    continue;
                }

                player.ShowFrame(frame % frames.Length);
            }

            foreach (var player in finished ?? [])
            {
                player.UpdatePlaying(); // rests on the first frame
            }
        };
        return timer;
    }

    /// <summary>All frames of a sticker at a pixel size, drawn once in the background and shared.</summary>
    private static Task<BitmapSource[]?> FramesFor(string path, int pixels)
    {
        var key = (path, pixels);
        if (!Cache.TryGetValue(key, out var frames))
        {
            if (Cache.Count >= MaxCachedSizes)
            {
                Cache.Remove(Cache.Keys.First());
            }

            frames = Task.Run(() => Render(path, pixels));
            Cache[key] = frames;
        }

        return frames;
    }

    private static BitmapSource[]? Render(string path, int pixels)
    {
        try
        {
            using var animation = StickerLibrary.Open(path);
            if (animation is null)
            {
                return null;
            }

            var count = Math.Clamp((int)Math.Round(animation.Duration.TotalSeconds * FramesPerSecond), 1, MaxFrames);
            var info = new SKImageInfo(pixels, pixels, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            var target = new SKRect(0, 0, pixels, pixels);
            var stride = pixels * 4;
            var buffer = new byte[stride * pixels];
            var frames = new BitmapSource[count];
            var pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                for (var i = 0; i < count; i++)
                {
                    surface.Canvas.Clear(SKColors.Transparent);
                    animation.SeekFrameTime(TimeSpan.FromSeconds(i / FramesPerSecond));
                    animation.Render(surface.Canvas, target);
                    surface.ReadPixels(info, pinned.AddrOfPinnedObject(), stride, 0, 0);
                    var frame = BitmapSource.Create(pixels, pixels, 96, 96, PixelFormats.Pbgra32, null, buffer, stride);
                    frame.Freeze();
                    frames[i] = frame;
                }
            }
            finally
            {
                pinned.Free();
            }

            return frames;
        }
        catch (Exception ex)
        {
            Infrastructure.Log.Error("Sticker", ex);
            return null;
        }
    }
}
