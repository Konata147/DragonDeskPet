param([string]$OutputPath = 'dist/pet-interactions-v2-check/pose-contact-sheet.png')

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath((Join-Path $root $OutputPath))
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output)) | Out-Null
$assetRoot = Join-Path $root 'assets/character/animations'
$ids = @('Greet','Pet','Feed','Cuddle','Hop','Dance','Stretch','LookAround','Land','Wake','Celebrate','Blink','Tail','Sleep')
$width = 1440; $height = 1180
$sheet = [Drawing.Bitmap]::new($width, $height, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [Drawing.Graphics]::FromImage($sheet)
$font = [Drawing.Font]::new('Segoe UI', 16)
$labelBrush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255, 58, 43, 75))
$tileBrush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255, 245, 241, 250))
try {
    $g.Clear([Drawing.Color]::White)
    $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    for ($i = 0; $i -lt $ids.Count; $i++) {
        $id = $ids[$i]
        $column = $i % 4; $row = [int][Math]::Floor($i / 4)
        $x = 20 + $column * 355; $y = 15 + $row * 290
        $g.FillRectangle($tileBrush, $x, $y, 335, 275)
        $frame = [Drawing.Bitmap]::FromFile((Join-Path $assetRoot "$id/03.png"))
        try { $g.DrawImage($frame, [Drawing.Rectangle]::new($x + 32, $y + 4, 270, 240)) }
        finally { $frame.Dispose() }
        $g.DrawString($id, $font, $labelBrush, [single]($x + 13), [single]($y + 246))
    }
    $sheet.Save($output, [Drawing.Imaging.ImageFormat]::Png)
}
finally { $labelBrush.Dispose(); $tileBrush.Dispose(); $font.Dispose(); $g.Dispose(); $sheet.Dispose() }
Write-Host $output
