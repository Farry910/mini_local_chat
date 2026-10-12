# Renders the app's chat-bubble icon (same drawing as Infrastructure/AppIcons.cs) into a multi-size .ico.
# Run from the repo root:  powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
param([string]$Out = "src\MiniLocalChat\Assets\app.ico")

Add-Type -AssemblyName System.Drawing
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256

function Render([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 32.0

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.FillMode = [System.Drawing.Drawing2D.FillMode]::Winding
    $x = 1 * $s; $y = 4 * $s; $w = 28 * $s; $h = 19 * $s; $d = 12 * $s
    $path.StartFigure()
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    # Same winding direction as the bubble, so the overlap fills instead of cancelling out.
    $tail = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF (7 * $s), (20 * $s)),
        (New-Object System.Drawing.PointF (15 * $s), (22 * $s)),
        (New-Object System.Drawing.PointF (7 * $s), (29 * $s)))
    $path.AddPolygon($tail)

    $fill = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0x5B, 0x5F, 0xC7))
    $g.FillPath($fill, $path)
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    foreach ($cx in 8.5, 15, 21.5) { $g.FillEllipse($white, ($cx - 2) * $s, 11.5 * $s, 4 * $s, 4 * $s) }

    $g.Dispose(); $fill.Dispose(); $white.Dispose(); $path.Dispose()
    if ($size -ge 256) { $script:images.Add((PngBytes $bmp)) } else { $script:images.Add((DibBytes $bmp)) }
    $bmp.Dispose()
}

# 256 px: PNG (the standard for large icons).
function PngBytes($bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return , $ms.ToArray()
}

# Smaller sizes: classic 32-bit DIB, which every icon reader understands:
# BITMAPINFOHEADER (height doubled), bottom-up BGRA pixels, then an all-zero 1-bit AND mask.
function DibBytes($bmp) {
    $size = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter($ms)
    $w.Write([uint32]40); $w.Write([int32]$size); $w.Write([int32]($size * 2))
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]0)
    $w.Write([uint32]($size * $size * 4)); $w.Write([int32]0); $w.Write([int32]0); $w.Write([uint32]0); $w.Write([uint32]0)
    for ($y = $size - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $size; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
        }
    }
    $maskRow = [int]([math]::Ceiling($size / 32.0) * 4)
    $w.Write((New-Object byte[] ($maskRow * $size)))
    $w.Flush()
    return , $ms.ToArray()
}

$images = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($size in $sizes) { Render $size }

# ICO container with PNG-compressed entries (supported since Windows Vista).
$ico = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($ico)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
$w.Flush()

$full = Join-Path (Get-Location) $Out
New-Item -ItemType Directory -Force (Split-Path $full) | Out-Null
[IO.File]::WriteAllBytes($full, $ico.ToArray())
"Wrote $Out ($($ico.Length) bytes, sizes: $($sizes -join ', '))"
