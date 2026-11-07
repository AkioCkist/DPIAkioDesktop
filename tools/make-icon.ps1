# Generate a multi-size .ico from a white-on-transparent logo PNG:
# dark rounded-square tile + logo cropped to its bounding box and centered.
param(
    [string]$Source = "$PSScriptRoot\..\DPIAkio\Assets\logo.png",
    [string[]]$Out = @("$PSScriptRoot\..\DPIAkio\Assets\app.ico", "$PSScriptRoot\..\native\src\icon.ico"),
    [string]$Background = '#1F2129',
    [double]$LogoScale = 0.80
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$src = [Drawing.Bitmap]::new((Resolve-Path $Source).Path)

# Bounding box of visible pixels
$minX = $src.Width; $minY = $src.Height; $maxX = 0; $maxY = 0
$data = $src.LockBits([Drawing.Rectangle]::new(0, 0, $src.Width, $src.Height), 'ReadOnly', 'Format32bppArgb')
$bytes = [byte[]]::new($data.Stride * $src.Height)
[Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
$src.UnlockBits($data)
for ($y = 0; $y -lt $src.Height; $y++) {
    $row = $y * $data.Stride
    for ($x = 0; $x -lt $src.Width; $x++) {
        if ($bytes[$row + $x * 4 + 3] -gt 16) {
            if ($x -lt $minX) { $minX = $x }; if ($x -gt $maxX) { $maxX = $x }
            if ($y -lt $minY) { $minY = $y }; if ($y -gt $maxY) { $maxY = $y }
        }
    }
}
$crop = [Drawing.RectangleF]::new($minX, $minY, $maxX - $minX + 1, $maxY - $minY + 1)
$bg = [Drawing.ColorTranslator]::FromHtml($Background)

function New-Tile([int]$size) {
    $bmp = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([Drawing.Color]::Transparent)

    # Rounded square (22% corner radius, like Windows 11 app tiles)
    $r = [Math]::Max(2.0, $size * 0.22); $d = $r * 2; $s = $size - 1
    $path = [Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($s - $d, 0, $d, $d, 270, 90)
    $path.AddArc($s - $d, $s - $d, $d, $d, 0, 90); $path.AddArc(0, $s - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $brush = [Drawing.SolidBrush]::new($bg)
    $g.FillPath($brush, $path)

    # Logo fit inside LogoScale of the tile, keeping aspect ratio
    $box = $size * $LogoScale
    $k = [Math]::Min($box / $crop.Width, $box / $crop.Height)
    $w = $crop.Width * $k; $h = $crop.Height * $k
    $dest = [Drawing.RectangleF]::new(($size - $w) / 2, ($size - $h) / 2, $w, $h)
    $g.DrawImage($src, $dest, $crop, [Drawing.GraphicsUnit]::Pixel)

    $brush.Dispose(); $path.Dispose(); $g.Dispose()
    return $bmp
}

# ICO with PNG-compressed entries (supported since Windows Vista)
$sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
$pngs = foreach ($sz in $sizes) {
    $bmp = New-Tile $sz
    $ms = [IO.MemoryStream]::new(); $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    , $ms.ToArray()
}
$ico = [IO.MemoryStream]::new()
$bw = [IO.BinaryWriter]::new($ico)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $len = $pngs[$i].Length
    $bw.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz }))); $bw.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz })))
    $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$len); $bw.Write([uint32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Flush()
foreach ($o in $Out) { [IO.File]::WriteAllBytes($o, $ico.ToArray()); Write-Host "Wrote $o" }

# Preview strip for a quick visual check
$preview = [Drawing.Bitmap]::new([int](($sizes | Measure-Object -Sum).Sum + 10 * $sizes.Count), 266)
$pg = [Drawing.Graphics]::FromImage($preview); $pg.Clear([Drawing.Color]::White); $x = 5
foreach ($sz in $sizes) { $t = New-Tile $sz; $pg.DrawImage($t, $x, 5, $sz, $sz); $t.Dispose(); $x += $sz + 10 }
$pg.Dispose()
$previewPath = Join-Path ([IO.Path]::GetTempPath()) 'dpiakio-icon-preview.png'
$preview.Save($previewPath); $preview.Dispose(); $src.Dispose()
Write-Host "Preview: $previewPath"
