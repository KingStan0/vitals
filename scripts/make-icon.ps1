[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'assets'
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Add-Type -AssemblyName System.Drawing

$background = [System.Drawing.Color]::FromArgb(255, 11, 18, 32)
$border = [System.Drawing.Color]::FromArgb(255, 35, 48, 68)
$accent = [System.Drawing.Color]::FromArgb(255, 34, 211, 238)
$muted = [System.Drawing.Color]::FromArgb(255, 148, 163, 184)

function New-RoundedPath([single]$width, [single]$height, [single]$radius) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = [single]($radius * 2)
    $path.AddArc(0, 0, $diameter, $diameter, 180, 90)
    $path.AddArc($width - $diameter, 0, $diameter, $diameter, 270, 90)
    $path.AddArc($width - $diameter, $height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc(0, $height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-PulsePoints([single]$width, [single]$height) {
    $coordinates = @(
        @(0.20, 0.53),
        @(0.35, 0.53),
        @(0.43, 0.32),
        @(0.53, 0.73),
        @(0.61, 0.45),
        @(0.68, 0.53),
        @(0.80, 0.53)
    )

    return @($coordinates | ForEach-Object {
        [System.Drawing.PointF]::new([single]($_[0] * $width), [single]($_[1] * $height))
    })
}

function New-vitalsIcon([int]$size) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $radius = [single]([Math]::Max(2, $size * 0.20))
    $path = New-RoundedPath $size $size $radius
    $fill = [System.Drawing.SolidBrush]::new($background)
    $stroke = [System.Drawing.Pen]::new($border, [single]([Math]::Max(1, $size * 0.018)))
    $graphics.FillPath($fill, $path)
    $graphics.DrawPath($stroke, $path)

    $points = New-PulsePoints $size $size

    $glow = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(55, 34, 211, 238), [single]([Math]::Max(3, $size * 0.115)))
    $glow.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $glow.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $glow.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $graphics.DrawLines($glow, [System.Drawing.PointF[]]$points)

    $line = [System.Drawing.Pen]::new($accent, [single]([Math]::Max(1.3, $size * 0.055)))
    $line.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $line.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $line.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $graphics.DrawLines($line, [System.Drawing.PointF[]]$points)

    $dot = [single]([Math]::Max(1.6, $size * 0.045))
    $brush = [System.Drawing.SolidBrush]::new($accent)
    $last = $points[$points.Count - 1]
    $graphics.FillEllipse($brush, $last.X - $dot, $last.Y - $dot, $dot * 2, $dot * 2)

    $stroke.Dispose()
    $fill.Dispose()
    $glow.Dispose()
    $line.Dispose()
    $brush.Dispose()
    $path.Dispose()
    $graphics.Dispose()
    return $bitmap
}

function Get-PngBytes([System.Drawing.Bitmap]$bitmap) {
    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $stream.ToArray()
    $stream.Dispose()
    return $bytes
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = foreach ($size in $sizes) {
    $bitmap = New-vitalsIcon $size
    $bytes = Get-PngBytes $bitmap
    [pscustomobject]@{ Size = $size; Bytes = [byte[]]$bytes }
    $bitmap.Dispose()
}

$iconPath = Join-Path $OutputDirectory 'vitals.ico'
$file = [System.IO.File]::Create($iconPath)
$writer = [System.IO.BinaryWriter]::new($file)
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$images.Count)

$offset = 6 + (16 * $images.Count)
foreach ($image in $images) {
    $dimension = if ($image.Size -ge 256) { 0 } else { $image.Size }
    $writer.Write([byte]$dimension)
    $writer.Write([byte]$dimension)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$image.Bytes.Length)
    $writer.Write([uint32]$offset)
    $offset += $image.Bytes.Length
}

foreach ($image in $images) {
    $writer.Write([byte[]]$image.Bytes)
}

$writer.Dispose()
$file.Dispose()

# Social preview: 1280x640, the size GitHub asks for.
$preview = [System.Drawing.Bitmap]::new(1280, 640)
$graphics = [System.Drawing.Graphics]::FromImage($preview)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit

$rect = [System.Drawing.Rectangle]::new(0, 0, 1280, 640)
$gradient = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
    $rect,
    [System.Drawing.Color]::FromArgb(255, 8, 14, 26),
    [System.Drawing.Color]::FromArgb(255, 17, 30, 48),
    [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
$graphics.FillRectangle($gradient, $rect)

$gridPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(18, 148, 163, 184), 1)
for ($x = 0; $x -lt 1280; $x += 64) {
    $graphics.DrawLine($gridPen, $x, 0, $x, 640)
}
for ($y = 0; $y -lt 640; $y += 64) {
    $graphics.DrawLine($gridPen, 0, $y, 1280, $y)
}

$icon = New-vitalsIcon 256
$graphics.DrawImage($icon, [System.Drawing.Rectangle]::new(96, 170, 220, 220))
$icon.Dispose()

$titleFont = [System.Drawing.Font]::new('Segoe UI', 104, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$subtitleFont = [System.Drawing.Font]::new('Segoe UI', 30, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$labelFont = [System.Drawing.Font]::new('Segoe UI', 22, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$white = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 248, 250, 252))
$grey = [System.Drawing.SolidBrush]::new($muted)

$graphics.DrawString('vitals', $titleFont, $white, 370, 165)
$graphics.DrawString('Fast, open-source Windows hardware monitoring', $subtitleFont, $grey, 376, 295)
$graphics.DrawString('CPU  ·  GPU  ·  temperatures  ·  JSON / CSV  ·  terminal-first', $labelFont, $grey, 378, 360)

$pulse = New-PulsePoints 1280 640
$pulse = @($pulse | ForEach-Object {
    [System.Drawing.PointF]::new([single](60 + ($_.X / 1280) * 1160), [single](500 + ($_.Y - 320) * 0.45))
})
$pulsePen = [System.Drawing.Pen]::new($accent, 7)
$pulsePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$pulsePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$pulsePen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
$graphics.DrawLines($pulsePen, [System.Drawing.PointF[]]$pulse)

$graphics.DrawString('Windows 10/11  ·  x64  ·  MIT', $labelFont, $grey, 378, 592)

$previewPath = Join-Path $OutputDirectory 'social-preview.png'
$preview.Save($previewPath, [System.Drawing.Imaging.ImageFormat]::Png)

$pulsePen.Dispose()
$titleFont.Dispose()
$subtitleFont.Dispose()
$labelFont.Dispose()
$white.Dispose()
$grey.Dispose()
$gridPen.Dispose()
$gradient.Dispose()
$graphics.Dispose()
$preview.Dispose()

Write-Host "icon  : $iconPath"
Write-Host "social: $previewPath"
