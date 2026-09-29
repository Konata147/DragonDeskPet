param(
    [Parameter(Mandatory)][ValidateSet('Hover','Greet','Pet','Feed','Cuddle','Hop','Dance','Stretch','LookAround','Land','Wake','Celebrate','Blink','Tail','Sleep')]
    [string]$Id,
    [string]$OutputDirectory = 'dist/pet-interactions-v2-check'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$outputRoot = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$sheet = [Drawing.Bitmap]::new(1840, 350, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [Drawing.Graphics]::FromImage($sheet)
$font = [Drawing.Font]::new('Segoe UI', 14)
$brush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255, 65, 50, 82))
$guide = [Drawing.Pen]::new([Drawing.Color]::FromArgb(150, 120, 72, 150))
try {
    $g.Clear([Drawing.Color]::FromArgb(255, 244, 240, 249))
    $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $frameCount = if ($Id -eq 'Hover') { 4 } else { 6 }
    for ($i=0; $i -lt $frameCount; $i++) {
        $path = Join-Path $root ('assets/character/animations/{0}/{1:D2}.png' -f $Id,$i)
        $frame = [Drawing.Bitmap]::FromFile($path)
        $x = 8 + $i * 305
        try { $g.DrawImage($frame, [Drawing.Rectangle]::new($x, 5, 300, 300)) }
        finally { $frame.Dispose() }
        $g.DrawLine($guide, $x, [single](5 + 495 * 300 / 512), $x + 300, [single](5 + 495 * 300 / 512))
        $g.DrawString(('Frame {0}' -f $i), $font, $brush, [single]($x + 8), [single]310)
    }
    $path = Join-Path $outputRoot "$Id-frames.png"
    $sheet.Save($path, [Drawing.Imaging.ImageFormat]::Png)
    Write-Host $path
}
finally { $guide.Dispose(); $brush.Dispose(); $font.Dispose(); $g.Dispose(); $sheet.Dispose() }
