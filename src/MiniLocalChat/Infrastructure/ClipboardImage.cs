using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MiniLocalChat.Infrastructure;

/// <summary>
/// Reads a pasted picture (e.g. a Win+Shift+S screenshot) as PNG bytes.
/// WPF's own Clipboard.GetImage() often returns 32-bit screenshots fully transparent, because the bitmap's
/// unused alpha byte is 0. So: use the PNG copy if the source app provided one, otherwise rebuild a .bmp from
/// the raw device-independent bitmap (whose alpha the BMP decoder ignores), and only then fall back to GetImage.
/// </summary>
public static class ClipboardImage
{
    private const int FileHeaderSize = 14;

    /// <summary>True when pasting should attach a picture rather than insert text.</summary>
    public static bool HasPicture(IDataObject data) =>
        !data.GetDataPresent(DataFormats.UnicodeText)
        && (data.GetDataPresent("PNG") || data.GetDataPresent(DataFormats.Dib) || data.GetDataPresent(DataFormats.Bitmap));

    public static byte[]? ReadPng(IDataObject data)
    {
        try
        {
            if (data.GetDataPresent("PNG") && data.GetData("PNG") is MemoryStream png && IsPng(png.ToArray()))
            {
                return png.ToArray();
            }

            if (data.GetDataPresent(DataFormats.Dib) && data.GetData(DataFormats.Dib) is MemoryStream dib && FromDib(dib.ToArray()) is { } fromDib)
            {
                return fromDib;
            }

            if (data.GetDataPresent(DataFormats.Bitmap) && data.GetData(DataFormats.Bitmap) is BitmapSource bitmap)
            {
                // Drop the (possibly zero) alpha channel so the picture can't come out invisible.
                return Encode(new FormatConvertedBitmap(bitmap, PixelFormats.Bgr32, null, 0));
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException
                                       or NotSupportedException or InvalidOperationException or IOException)
        {
            Log.Error("Reading pasted picture", ex);
        }

        return null;
    }

    /// <summary>A raw DIB plus the 14-byte file header it lacks is a normal .bmp file.</summary>
    public static byte[]? FromDib(byte[] dib)
    {
        if (dib.Length < 40)
        {
            return null;
        }

        var headerSize = BitConverter.ToInt32(dib, 0);
        var bitCount = BitConverter.ToInt16(dib, 14);
        var compression = BitConverter.ToInt32(dib, 16);
        var colorsUsed = BitConverter.ToInt32(dib, 32);
        var colorTable = colorsUsed > 0 ? colorsUsed * 4 : bitCount <= 8 ? (1 << bitCount) * 4 : 0;
        var masks = compression == 3 && headerSize == 40 ? 12 : 0; // BI_BITFIELDS with a plain header
        var pixelOffset = FileHeaderSize + headerSize + masks + colorTable;

        var bmp = new byte[FileHeaderSize + dib.Length];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BitConverter.GetBytes(bmp.Length).CopyTo(bmp, 2);
        BitConverter.GetBytes(pixelOffset).CopyTo(bmp, 10);
        dib.CopyTo(bmp, FileHeaderSize);

        using var stream = new MemoryStream(bmp);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        BitmapSource opaque = frame.Format == PixelFormats.Bgra32 ? new FormatConvertedBitmap(frame, PixelFormats.Bgr32, null, 0) : frame;
        return Encode(opaque);
    }

    private static byte[] Encode(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    private static bool IsPng(byte[] bytes) =>
        bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == (byte)'P' && bytes[2] == (byte)'N' && bytes[3] == (byte)'G';
}
