$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$sourcePath = Join-Path $projectRoot 'assets\character\default.png'
$outputDirectory = Join-Path $projectRoot 'assets\icons'
$previewPath = Join-Path $outputDirectory 'app.png'
$iconPath = Join-Path $outputDirectory 'app.ico'

Add-Type -AssemblyName System.Drawing
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null

$source = [System.Drawing.Bitmap]::FromFile($sourcePath)
$iconImages = New-Object System.Collections.Generic.List[byte[]]
$sizes = @(16, 24, 32, 48, 64, 128, 256)

try {
    foreach ($size in $sizes) {
        $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([System.Drawing.Color]::Transparent)
                $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

                $background = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 235, 228, 252))
                $outline = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 133, 108, 184), [Math]::Max(1, $size / 48))
                try {
                    $inset = [single]([Math]::Max(1, $size * 0.025))
                    $circleSize = [single]($size - 2 * $inset)
                    $graphics.FillEllipse($background, $inset, $inset, $circleSize, $circleSize)
                    $graphics.DrawEllipse($outline, $inset, $inset, $circleSize, $circleSize)
                }
                finally {
                    $background.Dispose()
                    $outline.Dispose()
                }

                $destination = New-Object System.Drawing.RectangleF(0, 0, $size, $size)
                $sourceCrop = New-Object System.Drawing.RectangleF(230, 0, 780, 780)
                $graphics.DrawImage($source, $destination, $sourceCrop, [System.Drawing.GraphicsUnit]::Pixel)
            }
            finally {
                $graphics.Dispose()
            }

            if ($size -eq 256) {
                $bitmap.Save($previewPath, [System.Drawing.Imaging.ImageFormat]::Png)
            }

            $memory = New-Object System.IO.MemoryStream
            try {
                $bitmap.Save($memory, [System.Drawing.Imaging.ImageFormat]::Png)
                $iconImages.Add($memory.ToArray())
            }
            finally {
                $memory.Dispose()
            }
        }
        finally {
            $bitmap.Dispose()
        }
    }
}
finally {
    $source.Dispose()
}

$file = [System.IO.File]::Create($iconPath)
try {
    $writer = New-Object System.IO.BinaryWriter($file)
    try {
        $writer.Write([UInt16]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]$sizes.Count)

        $offset = 6 + (16 * $sizes.Count)
        for ($index = 0; $index -lt $sizes.Count; $index++) {
            $size = $sizes[$index]
            $writer.Write([byte]($(if ($size -eq 256) { 0 } else { $size })))
            $writer.Write([byte]($(if ($size -eq 256) { 0 } else { $size })))
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([UInt16]1)
            $writer.Write([UInt16]32)
            $writer.Write([UInt32]$iconImages[$index].Length)
            $writer.Write([UInt32]$offset)
            $offset += $iconImages[$index].Length
        }

        foreach ($imageBytes in $iconImages) {
            $writer.Write($imageBytes)
        }
    }
    finally {
        $writer.Dispose()
    }
}
finally {
    $file.Dispose()
}

Write-Host "Generated $iconPath"
