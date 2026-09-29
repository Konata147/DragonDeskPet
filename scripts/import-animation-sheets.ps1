param(
    [Parameter(Mandatory = $true)][string]$SourceDirectory,
    [string[]]$Only = @(),
    [switch]$ReplaceExisting
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -Path (Join-Path $PSScriptRoot 'AnimationFrameCleaner.cs') -ReferencedAssemblies @(
    [Drawing.Bitmap].Assembly.Location, [Drawing.Rectangle].Assembly.Location)
$projectRoot = Split-Path -Parent $PSScriptRoot
$animationRoot = Join-Path $projectRoot 'assets\character\animations'
$sourceRoot = [IO.Path]::GetFullPath($SourceDirectory)
if (-not [IO.Directory]::Exists($sourceRoot)) { throw "Missing source directory: $sourceRoot" }
$manifestExists = [IO.File]::Exists((Join-Path $animationRoot 'clips.json'))
if ($Only.Count -eq 0 -and $manifestExists) { throw 'Animation output exists; refusing to overwrite it.' }
if ($Only.Count -gt 0 -and (-not $ReplaceExisting -or -not $manifestExists)) {
    throw 'Partial reimport requires -ReplaceExisting and an existing manifest.'
}

$durations = [ordered]@{
    Blink = @(75, 60, 75, 75, 60, 85)
    Greet = @(180, 200, 220, 280, 260, 360)
    Pet = @(220, 260, 340, 420, 300, 360)
    Feed = @(300, 350, 400, 450, 350, 350)
    Cuddle = @(300, 320, 350, 500, 350, 380)
    Hop = @(200, 220, 340, 240, 300, 500)
    Dance = @(300, 350, 330, 350, 340, 730)
    Stretch = @(300, 340, 430, 480, 300, 350)
    LookAround = @(260, 300, 360, 370, 280, 330)
    Land = @(160, 190, 180, 220, 180, 170)
    Wake = @(300, 350, 430, 360, 300, 260)
    Celebrate = @(300, 350, 380, 520, 390, 460)
    Tail = @(180, 220, 260, 260, 220, 260)
    Sleep = @(300, 350, 450, 470, 450, 480)
}

function Get-CellInfo($bitmap, [int]$index) {
    $column = $index % 3
    $row = [int][Math]::Floor($index / 3)
    $left = [int][Math]::Round($bitmap.Width * $column / 3)
    $top = [int][Math]::Round($bitmap.Height * $row / 2)
    $right = [int][Math]::Round($bitmap.Width * ($column + 1) / 3)
    $bottom = [int][Math]::Round($bitmap.Height * ($row + 1) / 2)
    $width = $right - $left
    $height = $bottom - $top
    $minX = $width; $minY = $height; $maxX = -1; $maxY = -1
    $feetXSum = 0.0; $feetCount = 0; $feetBottom = 0
    for ($y = 0; $y -lt $height; $y += 2) {
        for ($x = 0; $x -lt $width; $x += 2) {
            $alpha = $bitmap.GetPixel($left + $x, $top + $y).A
            if ($alpha -lt 48) { continue }
            $minX = [Math]::Min($minX, $x); $maxX = [Math]::Max($maxX, $x)
            $minY = [Math]::Min($minY, $y); $maxY = [Math]::Max($maxY, $y)
            if ($alpha -ge 96 -and $x -ge $width * .35 -and $x -le $width * .65 -and $y -ge $height * .75) {
                $feetXSum += $x; $feetCount++
                $feetBottom = [Math]::Max($feetBottom, $y)
            }
        }
    }
    if ($maxX -lt 0 -or $feetCount -eq 0) { throw "Empty or damaged animation cell $index" }
    return [pscustomobject]@{ Left = $left; Top = $top; Width = $width; Height = $height;
        MinX = $minX; MinY = $minY; MaxX = $maxX; MaxY = $maxY;
        FootX = $feetXSum / $feetCount; FootBottom = $feetBottom }
}

$clips = [Collections.Generic.List[object]]::new()
foreach ($entry in $durations.GetEnumerator()) {
    $id = [string]$entry.Key
    if ($Only.Count -gt 0 -and $id -notin $Only) { continue }
    $sheetPath = Join-Path $sourceRoot "$id.png"
    if (-not [IO.File]::Exists($sheetPath)) { throw "Missing generated sheet: $sheetPath" }
    $output = Join-Path $animationRoot $id
    if ([IO.Directory]::Exists($output) -and -not $ReplaceExisting) { throw "Animation frames exist; refusing to overwrite: $output" }
    $bitmap = [Drawing.Bitmap]::FromFile($sheetPath)
    try {
        $cells = @(0..5 | ForEach-Object { Get-CellInfo $bitmap $_ })
        $maxHeight = ($cells | ForEach-Object { $_.MaxY - $_.MinY + 1 } | Measure-Object -Maximum).Maximum
        $scale = 480.0 / $maxHeight
        if ($scale * ($cells | ForEach-Object { $_.Width } | Measure-Object -Maximum).Maximum -gt 510) {
            $scale = 510.0 / ($cells | ForEach-Object { $_.Width } | Measure-Object -Maximum).Maximum
        }
        [IO.Directory]::CreateDirectory($output) | Out-Null
        $footBaseline = $cells[0].FootBottom
        $frames = [Collections.Generic.List[string]]::new()
        for ($i = 0; $i -lt 6; $i++) {
            $cell = $cells[$i]
            $result = [Drawing.Bitmap]::new(512, 512, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $graphics = [Drawing.Graphics]::FromImage($result)
            try {
                $graphics.Clear([Drawing.Color]::Transparent)
                $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
                $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $footY = if ($id -eq 'Hop') { $footBaseline } else { $cell.FootBottom }
                $x = [single](256 - $cell.FootX * $scale)
                $y = [single](498 - $footY * $scale)
                $destination = [Drawing.RectangleF]::new($x, $y, [single]($cell.Width * $scale), [single]($cell.Height * $scale))
                $source = [Drawing.Rectangle]::new($cell.Left, $cell.Top, $cell.Width, $cell.Height)
                $graphics.DrawImage($bitmap, $destination, $source, [Drawing.GraphicsUnit]::Pixel)
                $graphics.Flush(); $graphics.Dispose(); $graphics = $null
                [AnimationFrameCleaner]::KeepMainFigure($result)
                $fileName = '{0:D2}.png' -f $i
                $framePath = Join-Path $output $fileName
                $writePath = if ($ReplaceExisting) { "$framePath.new" } else { $framePath }
                $result.Save($writePath, [Drawing.Imaging.ImageFormat]::Png)
                if ($ReplaceExisting) { Move-Item -LiteralPath $writePath -Destination $framePath -Force }
                $frames.Add("$id/$fileName")
            }
            finally { if ($null -ne $graphics) { $graphics.Dispose() }; $result.Dispose() }
        }
        $clips.Add([ordered]@{ Id = $id; Frames = @($frames); DurationsMs = $entry.Value;
            PosterFrame = if ($id -eq 'Blink') { 2 } elseif ($id -eq 'Sleep') { 4 } else { 3 } })
        Write-Host "Imported $id (6 frames, anchored canvas 512x512)."
    }
    finally { $bitmap.Dispose() }
}
if ($Only.Count -eq 0) {
    $clips.Add([ordered]@{ Id = 'SleepBreath'; Frames = @('Sleep/02.png', 'Sleep/03.png', 'Sleep/02.png');
        DurationsMs = @(900, 900, 900); PosterFrame = 1 })
    $clips | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $animationRoot 'clips.json') -Encoding utf8
}
