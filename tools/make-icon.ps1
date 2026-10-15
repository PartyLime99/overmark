# Generates src/Overmark/Assets/Overmark.ico: a red rounded square with a white crosshair.
# Each size is drawn separately so the small tray sizes stay crisp.
#
# Run with Windows PowerShell 5.1:
#   powershell -ExecutionPolicy Bypass -File tools/make-icon.ps1 [-PreviewFile preview.png]

param(
    [string]$OutFile = (Join-Path $PSScriptRoot '..\src\Overmark\Assets\Overmark.ico'),
    [string]$PreviewFile
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$red = [System.Drawing.Color]::FromArgb(255, 0xE5, 0x48, 0x4D)

function New-RoundedRectangle([single]$x, [single]$y, [single]$width, [single]$height, [single]$radius) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $width - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $width - $d, $y + $height - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $height - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $path
}

function New-IconBitmap([int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.Clear([System.Drawing.Color]::Transparent)

    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $inset = [Math]::Max(0.5, $size * 0.03)
    $background = New-RoundedRectangle $inset $inset ($size - 2 * $inset) ($size - 2 * $inset) ($size * 0.22)
    $brush = New-Object System.Drawing.SolidBrush $red
    $g.FillPath($brush, $background)

    # Crosshair arms as whole-pixel rectangles (even thickness on even sizes stays sharp).
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None
    $t = 2 * [Math]::Max(1, [Math]::Round($size / 20))
    $c = $size / 2
    $gap = [Math]::Round($size * 0.12)
    $from = [Math]::Round($size * 0.2)
    $to = $size - $from
    $white = [System.Drawing.Brushes]::White
    $g.FillRectangle($white, $c - $t / 2, $from, $t, $c - $gap - $from)
    $g.FillRectangle($white, $c - $t / 2, $c + $gap, $t, $to - $c - $gap)
    $g.FillRectangle($white, $from, $c - $t / 2, $c - $gap - $from, $t)
    $g.FillRectangle($white, $c + $gap, $c - $t / 2, $to - $c - $gap, $t)

    $brush.Dispose()
    $background.Dispose()
    $g.Dispose()
    $bitmap
}

# A 32-bit DIB as stored in .ico files: bottom-up rows, doubled height, then an all-zero AND mask.
function Get-DibBytes([System.Drawing.Bitmap]$bitmap) {
    $size = $bitmap.Width
    $rect = New-Object System.Drawing.Rectangle 0, 0, $size, $size
    $data = $bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $pixels = New-Object byte[] ($data.Stride * $size)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    $stride = $data.Stride
    $bitmap.UnlockBits($data)

    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter $stream
    $writer.Write([int]40)
    $writer.Write([int]$size)
    $writer.Write([int]($size * 2))
    $writer.Write([int16]1)
    $writer.Write([int16]32)
    foreach ($i in 1..6) { $writer.Write([int]0) }
    for ($y = $size - 1; $y -ge 0; $y--) { $writer.Write($pixels, $y * $stride, $size * 4) }
    $maskRowBytes = [int]([Math]::Ceiling($size / 32) * 4)
    $writer.Write((New-Object byte[] ($maskRowBytes * $size)))
    $writer.Flush()
    $stream.ToArray()
}

function Get-PngBytes([System.Drawing.Bitmap]$bitmap) {
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $stream.ToArray()
}

$images = foreach ($size in $sizes) {
    $bitmap = New-IconBitmap $size
    # 256 px is stored as PNG (standard for that size); smaller sizes as DIBs for the widest compatibility.
    $bytes = if ($size -ge 256) { Get-PngBytes $bitmap } else { Get-DibBytes $bitmap }
    if ($PreviewFile -and $size -eq 256) { $bitmap.Save($PreviewFile, [System.Drawing.Imaging.ImageFormat]::Png) }
    $bitmap.Dispose()
    [pscustomobject]@{ Size = $size; Bytes = $bytes }
}

$outDir = Split-Path -Parent $OutFile
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

$stream = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $stream
$writer.Write([int16]0)              # reserved
$writer.Write([int16]1)              # type: icon
$writer.Write([int16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($image in $images) {
    $dimension = if ($image.Size -ge 256) { 0 } else { $image.Size }   # 0 means 256
    $writer.Write([byte]$dimension)
    $writer.Write([byte]$dimension)
    $writer.Write([byte]0)           # palette colours
    $writer.Write([byte]0)           # reserved
    $writer.Write([int16]1)          # colour planes
    $writer.Write([int16]32)         # bits per pixel
    $writer.Write([int]$image.Bytes.Length)
    $writer.Write([int]$offset)
    $offset += $image.Bytes.Length
}
foreach ($image in $images) { $writer.Write([byte[]]$image.Bytes) }
$writer.Flush()
[System.IO.File]::WriteAllBytes((Resolve-Path -LiteralPath $outDir).Path + '\' + (Split-Path -Leaf $OutFile), $stream.ToArray())
Write-Output "Wrote $OutFile ($($images.Count) sizes)"
