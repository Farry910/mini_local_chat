using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MiniLocalChat.Infrastructure;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is true) != Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible when the value is set (e.g. a message has a reply quote).</summary>
public sealed class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

/// <summary>Visible when the count is above zero (or exactly zero when inverted).</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is int n && n > 0) != Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class PositiveConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is int n && n > 0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible when the string is empty (placeholders); inverted, visible when it has text.</summary>
public sealed class EmptyToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.IsNullOrEmpty(value as string) != Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Presence line under a person's name; makes clear that offline people can still be messaged.</summary>
public sealed class PresenceTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? "Online" : "Offline · messages are delivered when they come online";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Timestamp shown above a message bubble.</summary>
public sealed class MessageTimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not DateTime utc || utc == default)
        {
            return "";
        }

        var local = utc.ToLocalTime();
        var c = CultureInfo.CurrentCulture;
        var today = DateTime.Today;
        if (local.Date == today)
        {
            return local.ToString("t", c);
        }

        if (local.Date == today.AddDays(-1))
        {
            return "Yesterday " + local.ToString("t", c);
        }

        return local.ToString("d", c) + " " + local.ToString("t", c);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Compact timestamp for the chat list.</summary>
public sealed class ListTimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not DateTime utc || utc == default)
        {
            return "";
        }

        var local = utc.ToLocalTime();
        var c = CultureInfo.CurrentCulture;
        var days = (DateTime.Today - local.Date).TotalDays;
        return days switch
        {
            < 1 => local.ToString("t", c),
            < 7 => local.ToString("ddd", c),
            _ => local.ToString("d", c),
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class FileSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is long bytes ? Format(bytes) : "";

    public static string Format(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{size:0.#} {units[unit]}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Loads an image file without locking it, downscaling large pictures.</summary>
public sealed class PathToImageConverter : IValueConverter
{
    private const int MaxDecodeWidth = 640;
    private static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path))
        {
            return null;
        }

        if (Cache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        try
        {
            int pixelWidth;
            using (var stream = File.OpenRead(path))
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
                pixelWidth = decoder.Frames[0].PixelWidth;
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.UriSource = new Uri(path);
            if (pixelWidth > MaxDecodeWidth)
            {
                bitmap.DecodePixelWidth = MaxDecodeWidth;
            }

            bitmap.EndInit();
            bitmap.Freeze();

            if (Cache.Count > 200)
            {
                Cache.Clear();
            }

            Cache[path] = bitmap;
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or InvalidOperationException or UriFormatException)
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
