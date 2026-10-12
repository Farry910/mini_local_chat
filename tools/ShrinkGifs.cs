#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property OutputType=Exe
#:property Nullable=enable
#:property PublishAot=false
#:property PublishTrimmed=false

// Shrinks animated GIFs (e.g. Google's 512 px Noto animated emoji) to sticker size, keeping
// transparency, frame timing and looping.
//   dotnet run tools/ShrinkGifs.cs -- <input dir> <output dir> [size=160]
// Frames are decoded and composited (GIF disposal rules), scaled with high quality, quantized to a
// shared 255-color palette plus transparency, and written as full frames with LZW compression.

using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

var input = args.Length > 0 ? args[0] : throw new ArgumentException("input dir");
var output = args.Length > 1 ? args[1] : throw new ArgumentException("output dir");
var size = args.Length > 2 ? int.Parse(args[2]) : 160;
Directory.CreateDirectory(output);

foreach (var file in Directory.GetFiles(input, "*.gif").OrderBy(f => f))
{
    var (frames, delays) = Decode(file, size);
    var bytes = GifWriter.Write(frames, delays, size, size);
    var target = Path.Combine(output, Path.GetFileName(file));
    File.WriteAllBytes(target, bytes);
    Console.WriteLine($"{Path.GetFileName(file),-24} {new FileInfo(file).Length / 1024,6} KB -> {bytes.Length / 1024,4} KB, {frames.Count} frames");
}

static (List<byte[]> Frames, List<int> Delays) Decode(string path, int size)
{
    var decoder = new GifBitmapDecoder(new Uri(Path.GetFullPath(path)), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
    var meta = (BitmapMetadata)decoder.Metadata;
    var width = Convert.ToInt32(meta.GetQuery("/logscrdesc/Width"));
    var height = Convert.ToInt32(meta.GetQuery("/logscrdesc/Height"));

    var canvas = new byte[width * height * 4];
    var frames = new List<byte[]>();
    var delays = new List<int>();
    foreach (var frame in decoder.Frames)
    {
        var fm = (BitmapMetadata)frame.Metadata;
        int Q(string q, int fallback) => fm.ContainsQuery(q) ? Convert.ToInt32(fm.GetQuery(q)) : fallback;
        var left = Q("/imgdesc/Left", 0);
        var top = Q("/imgdesc/Top", 0);
        var disposal = Q("/grctlext/Disposal", 0);
        var delay = Q("/grctlext/Delay", 10);

        var previous = disposal == 3 ? (byte[])canvas.Clone() : null;

        // Paint this frame's pixels over the canvas (transparent pixels leave the canvas as is).
        var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var fw = bgra.PixelWidth;
        var fh = bgra.PixelHeight;
        var pixels = new byte[fw * fh * 4];
        bgra.CopyPixels(pixels, fw * 4, 0);
        for (var y = 0; y < fh; y++)
        {
            for (var x = 0; x < fw; x++)
            {
                var cx = left + x;
                var cy = top + y;
                if (cx >= width || cy >= height)
                {
                    continue;
                }

                var s = (y * fw + x) * 4;
                if (pixels[s + 3] == 0)
                {
                    continue;
                }

                var d = (cy * width + cx) * 4;
                Buffer.BlockCopy(pixels, s, canvas, d, 4);
            }
        }

        frames.Add(Scale(canvas, width, height, size));
        delays.Add(Math.Max(2, delay));

        // Prepare the canvas for the next frame.
        if (disposal == 2)
        {
            for (var y = top; y < Math.Min(height, top + fh); y++)
            {
                Array.Clear(canvas, (y * width + left) * 4, Math.Min(fw, width - left) * 4);
            }
        }
        else if (disposal == 3 && previous is not null)
        {
            canvas = previous;
        }
    }

    return (frames, delays);
}

static byte[] Scale(byte[] canvas, int width, int height, int size)
{
    var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, canvas, width * 4);
    var visual = new DrawingVisual();
    RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
    using (var dc = visual.RenderOpen())
    {
        dc.DrawImage(source, new Rect(0, 0, size, size));
    }

    var target = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
    target.Render(visual);
    var straight = new FormatConvertedBitmap(target, PixelFormats.Bgra32, null, 0);
    var result = new byte[size * size * 4];
    straight.CopyPixels(result, size * 4, 0);
    return result;
}

/// <summary>Animated GIF89a writer: shared palette (255 colors + transparent), full frames, LZW, loops forever.</summary>
static class GifWriter
{
    private const int Transparent = 255;

    public static byte[] Write(List<byte[]> frames, List<int> delays, int width, int height)
    {
        var palette = BuildPalette(frames);
        var lookup = new Dictionary<int, byte>();
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write("GIF89a"u8.ToArray());
        w.Write((ushort)width);
        w.Write((ushort)height);
        w.Write((byte)0xF7); // global color table, 256 entries
        w.Write((byte)Transparent);
        w.Write((byte)0);
        for (var i = 0; i < 256; i++)
        {
            var c = i < palette.Count ? palette[i] : (R: 0, G: 0, B: 0);
            w.Write((byte)c.R);
            w.Write((byte)c.G);
            w.Write((byte)c.B);
        }

        w.Write(new byte[] { 0x21, 0xFF, 0x0B });
        w.Write("NETSCAPE2.0"u8.ToArray());
        w.Write(new byte[] { 3, 1, 0, 0, 0 });

        for (var f = 0; f < frames.Count; f++)
        {
            // Graphic control: restore to background (so transparent areas stay clear), transparent index.
            w.Write(new byte[] { 0x21, 0xF9, 4, (2 << 2) | 1 });
            w.Write((ushort)delays[f]);
            w.Write((byte)Transparent);
            w.Write((byte)0);

            w.Write((byte)0x2C);
            w.Write((ushort)0);
            w.Write((ushort)0);
            w.Write((ushort)width);
            w.Write((ushort)height);
            w.Write((byte)0);

            var indices = new byte[width * height];
            var px = frames[f];
            for (var i = 0; i < indices.Length; i++)
            {
                var b = px[i * 4];
                var g = px[i * 4 + 1];
                var r = px[i * 4 + 2];
                var a = px[i * 4 + 3];
                if (a < 128)
                {
                    indices[i] = Transparent;
                    continue;
                }

                var key = (r << 16) | (g << 8) | b;
                if (!lookup.TryGetValue(key, out var index))
                {
                    lookup[key] = index = Nearest(palette, r, g, b);
                }

                indices[i] = index;
            }

            w.Write((byte)8);
            var data = Lzw(indices, 8);
            for (var p = 0; p < data.Length; p += 255)
            {
                var len = Math.Min(255, data.Length - p);
                w.Write((byte)len);
                w.Write(data, p, len);
            }

            w.Write((byte)0);
        }

        w.Write((byte)0x3B);
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>The 255 most used colors (5 bits per channel), averaged, over all frames.</summary>
    private static List<(int R, int G, int B)> BuildPalette(List<byte[]> frames)
    {
        var count = new int[32768];
        var sumR = new long[32768];
        var sumG = new long[32768];
        var sumB = new long[32768];
        foreach (var px in frames)
        {
            for (var i = 0; i < px.Length; i += 4)
            {
                if (px[i + 3] < 128)
                {
                    continue;
                }

                var key = ((px[i + 2] >> 3) << 10) | ((px[i + 1] >> 3) << 5) | (px[i] >> 3);
                count[key]++;
                sumR[key] += px[i + 2];
                sumG[key] += px[i + 1];
                sumB[key] += px[i];
            }
        }

        return Enumerable.Range(0, 32768)
            .Where(k => count[k] > 0)
            .OrderByDescending(k => count[k])
            .Take(255)
            .Select(k => ((int)(sumR[k] / count[k]), (int)(sumG[k] / count[k]), (int)(sumB[k] / count[k])))
            .ToList();
    }

    private static byte Nearest(List<(int R, int G, int B)> palette, int r, int g, int b)
    {
        var best = 0;
        var bestDistance = int.MaxValue;
        for (var i = 0; i < palette.Count; i++)
        {
            var dr = palette[i].R - r;
            var dg = palette[i].G - g;
            var db = palette[i].B - b;
            var distance = 2 * dr * dr + 4 * dg * dg + 3 * db * db;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return (byte)best;
    }

    /// <summary>Standard GIF LZW: variable code width, clear code when the table is full.</summary>
    private static byte[] Lzw(byte[] indices, int minCodeSize)
    {
        var clear = 1 << minCodeSize;
        var end = clear + 1;
        var output = new List<byte>();
        int buffer = 0, bits = 0;
        var codeSize = minCodeSize + 1;
        var next = end + 1;
        var table = new Dictionary<int, int>();

        void Emit(int code)
        {
            buffer |= code << bits;
            bits += codeSize;
            while (bits >= 8)
            {
                output.Add((byte)(buffer & 0xFF));
                buffer >>= 8;
                bits -= 8;
            }
        }

        Emit(clear);
        var prefix = (int)indices[0];
        for (var i = 1; i < indices.Length; i++)
        {
            var k = indices[i];
            var key = (prefix << 8) | k;
            if (table.TryGetValue(key, out var code))
            {
                prefix = code;
                continue;
            }

            Emit(prefix);
            if (next < 4096)
            {
                table[key] = next++;
                if (next > (1 << codeSize) && codeSize < 12)
                {
                    codeSize++;
                }
            }
            else
            {
                Emit(clear);
                table.Clear();
                next = end + 1;
                codeSize = minCodeSize + 1;
            }

            prefix = k;
        }

        Emit(prefix);
        Emit(end);
        if (bits > 0)
        {
            output.Add((byte)(buffer & 0xFF));
        }

        return output.ToArray();
    }
}
