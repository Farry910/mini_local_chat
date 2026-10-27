using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MiniLocalChat.Infrastructure;
using MiniLocalChat.Services;
using SkiaSharp;

namespace MiniLocalChat.Controls;

/// <summary>
/// Plays a Telegram animated sticker (.tgs). The frames are drawn once, in the background, at the size
/// the sticker is shown (sharp at any DPI), and reused by every copy of it; recently used ones stay
/// cached up to <see cref="CacheBytes"/>. One shared timer moves all visible stickers, and only while
/// their window is on screen. With <see cref="PlayOnHover"/> (panel tiles) only the first frame is drawn,
/// and the rest while the mouse is on it. Otherwise it plays <see cref="Loops"/> times when it comes into
/// view, then rests on the first frame (playing costs CPU); hovering replays it.
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

    /// <summary>A 160 px sticker at 150 % is about 20 MB of frames, so this keeps a handful.</summary>
    private const long CacheBytes = 80L * 1024 * 1024;

    private static readonly LruCache<(string Path, int Pixels, bool All), BitmapSource[]> Cache =
        new(CacheBytes, frames => frames.Length * (long)frames[0].PixelWidth * frames[0].PixelHeight * 4);

    private static readonly Dictionary<(string Path, int Pixels, bool All), Task<BitmapSource[]?>> Rendering = new();
    private static readonly HashSet<StickerPlayer> Playing = [];
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static DispatcherTimer? _timer;

    private BitmapSource[]? _frames;
    private bool _allFrames;
    private int _loadId;
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
            if (PlayOnHover && !_allFrames)
            {
                Load(all: true);
            }

            UpdatePlaying();
        };
        MouseLeave += (_, _) =>
        {
            // A panel tile goes back to its still frame; the others stay in the cache for the next hover.
            if (PlayOnHover && _allFrames && _frames is { } frames)
            {
                _frames = [frames[0]];
                _allFrames = false;
            }

            UpdatePlaying();
        };
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

    /// <summary>Forgets all drawn frames (e.g. while the app sits in the tray); stickers on screen keep theirs.</summary>
    public static void ClearCache() => Cache.Clear();

    private void Reload()
    {
        Stop();
        _frames = null;
        _allFrames = false;
        _shownFrame = -1;
        _finished = false;
        Source = null;
        Load(all: !PlayOnHover || IsMouseOver);
    }

    /// <summary>Gets the frames (all, or just the first) and shows them. A newer load or path change wins.</summary>
    private async void Load(bool all)
    {
        var id = ++_loadId;
        if (!IsLoaded || Path is not { } path || double.IsNaN(Width))
        {
            return;
        }

        var pixels = (int)Math.Ceiling(Width * VisualTreeHelper.GetDpi(this).DpiScaleX);
        var frames = await FramesFor(path, pixels, all);
        if (id != _loadId || path != Path || frames is not { Length: > 0 })
        {
            return; // superseded, or not a sticker
        }

        if (all && PlayOnHover && !IsMouseOver)
        {
            frames = [frames[0]]; // the mouse already left
            all = false;
        }

        _frames = frames;
        _allFrames = all;
        _shownFrame = -1;
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

    /// <summary>
    /// The frames of a sticker at a pixel size (all of them, or just the first for a still tile), drawn once
    /// in the background and shared.
    /// </summary>
    private static async Task<BitmapSource[]?> FramesFor(string path, int pixels, bool all)
    {
        if (Cache.TryGet((path, pixels, all), out var frames))
        {
            return frames;
        }

        if (!all && Cache.TryGet((path, pixels, true), out var full))
        {
            return [full[0]];
        }

        var key = (path, pixels, all);
        if (!Rendering.TryGetValue(key, out var rendering))
        {
            Rendering[key] = rendering = Task.Run(() => Render(path, pixels, all ? MaxFrames : 1));
        }

        try
        {
            frames = await rendering;
        }
        finally
        {
            Rendering.Remove(key);
        }

        if (frames is { Length: > 0 })
        {
            Cache.Add(key, frames);
        }

        return frames;
    }

    private static BitmapSource[]? Render(string path, int pixels, int maxFrames)
    {
        try
        {
            using var animation = StickerLibrary.Open(path);
            if (animation is null)
            {
                return null;
            }

            var count = Math.Clamp((int)Math.Round(animation.Duration.TotalSeconds * FramesPerSecond), 1, maxFrames);
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
            Log.Error("Sticker", ex);
            return null;
        }
    }
}
