# Generates Assets\tray.ico (16/20/24/32/48/256 px, PNG-compressed entries) with the same
# three-bar mixer glyph the app draws at runtime. Run from the project root:
#   powershell -ExecutionPolicy Bypass -File tools\Make-Icon.ps1
param(
    [string]$Out = (Join-Path $PSScriptRoot "..\Assets\tray.ico"),
    [string]$Hex = "#2DD4BF"
)
Add-Type -AssemblyName System.Drawing

$color = [System.Drawing.ColorTranslator]::FromHtml($Hex)
$sizes = 16, 20, 24, 32, 48, 256

function New-GlyphPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $brush = New-Object System.Drawing.SolidBrush $color
    $u = $size / 16.0
    $barW = 3.2 * $u; $gap = 1.6 * $u
    $heights = 0.55, 0.9, 0.7
    $totalW = 3 * $barW + 2 * $gap
    $x0 = ($size - $totalW) / 2.0
    for ($i = 0; $i -lt 3; $i++) {
        $h = $size * $heights[$i]
        $x = $x0 + $i * ($barW + $gap)
        $y = ($size - $h) / 2.0
        $r = $barW / 2.0; $d = $r * 2
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.AddArc($x, $y, $d, $d, 180, 90)
        $path.AddArc($x + $barW - $d, $y, $d, $d, 270, 90)
        $path.AddArc($x + $barW - $d, $y + $h - $d, $d, $d, 0, 90)
        $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
        $path.CloseFigure()
        $g.FillPath($brush, $path)
        $path.Dispose()
    }
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose(); $brush.Dispose()
    return $ms.ToArray()
}

$images = @()
foreach ($s in $sizes) { $images += ,@{ Size = $s; Data = (New-GlyphPng $s) } }

$outStream = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $outStream
# ICONDIR
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $sz = if ($img.Size -ge 256) { 0 } else { $img.Size }
    $bw.Write([byte]$sz); $bw.Write([byte]$sz)      # width, height (0 = 256)
    $bw.Write([byte]0); $bw.Write([byte]0)          # palette, reserved
    $bw.Write([uint16]1); $bw.Write([uint16]32)     # planes, bpp
    $bw.Write([uint32]$img.Data.Length)             # bytes in resource
    $bw.Write([uint32]$offset)                      # offset
    $offset += $img.Data.Length
}
foreach ($img in $images) { $bw.Write([byte[]]$img.Data) }
$bw.Flush()

$dir = Split-Path -Parent $Out
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
[System.IO.File]::WriteAllBytes($Out, $outStream.ToArray())
Write-Output "wrote $Out ($($outStream.Length) bytes, sizes: $($sizes -join ','))"
