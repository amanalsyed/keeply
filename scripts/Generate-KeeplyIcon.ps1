$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root "assets"
New-Item -ItemType Directory -Force -Path $assets | Out-Null
$iconPath = Join-Path $assets "Keeply.ico"

$bitmap = New-Object System.Drawing.Bitmap 1024, 1024, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::Transparent)

$tile = New-Object System.Drawing.Drawing2D.GraphicsPath
$tile.AddArc(32, 32, 480, 480, 180, 90)
$tile.AddArc(512, 32, 480, 480, 270, 90)
$tile.AddArc(512, 512, 480, 480, 0, 90)
$tile.AddArc(32, 512, 480, 480, 90, 90)
$tile.CloseFigure()
$graphics.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 166, 231, 195))), $tile)

$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 16, 21, 18)), 112
$pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
$graphics.DrawLine($pen, 368, 288, 368, 736)
$graphics.DrawLine($pen, 672, 320, 432, 512)
$graphics.DrawLine($pen, 432, 512, 704, 736)

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngs = [System.Collections.Generic.List[byte[]]]::new()
foreach ($size in $sizes) {
    $small = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($small)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($bitmap, 0, 0, $size, $size)
    $stream = New-Object System.IO.MemoryStream
    $small.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs.Add($stream.ToArray())
    $stream.Dispose()
    $g.Dispose()
    $small.Dispose()
}

$file = [System.IO.File]::Create($iconPath)
$writer = New-Object System.IO.BinaryWriter $file
$writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([byte]1); $writer.Write([byte]0)
$writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]
    $writer.Write([byte]($(if ($size -eq 256) { 0 } else { $size })))
    $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$pngs[$i].Length)
    $writer.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($png in $pngs) { $writer.Write($png) }
$writer.Dispose()
$graphics.Dispose()
$bitmap.Dispose()
$pen.Dispose()
$tile.Dispose()
Write-Host "Created $iconPath"
