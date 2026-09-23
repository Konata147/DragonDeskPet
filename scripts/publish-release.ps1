[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64',

    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $projectRoot 'src\DragonDeskPet\DragonDeskPet.csproj'
$distRoot = Join-Path $projectRoot 'dist'
$packageName = "DragonDeskPet-v$Version-$Runtime"
$publishDirectory = Join-Path $distRoot $packageName
$archivePath = Join-Path $distRoot "$packageName.zip"
$checksumPath = Join-Path $distRoot "$packageName.sha256.txt"

$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet-cli'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.nuget\packages'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

function Assert-PathInsideDist {
    param([Parameter(Mandatory = $true)][string]$Path)

    $distPrefix = [System.IO.Path]::GetFullPath($distRoot).TrimEnd('\') + '\'
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($distPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside the dist directory: $fullPath"
    }
}

foreach ($target in @($publishDirectory, $archivePath, $checksumPath)) {
    Assert-PathInsideDist -Path $target
}

New-Item -ItemType Directory -Path $distRoot -Force | Out-Null

$existingTargets = @($publishDirectory, $archivePath, $checksumPath) |
    Where-Object { Test-Path -LiteralPath $_ }

if ($existingTargets.Count -gt 0 -and -not $Force) {
    $existingList = $existingTargets -join [Environment]::NewLine
    throw "Release output already exists. Re-run with -Force only after checking these exact paths:$([Environment]::NewLine)$existingList"
}

if ($Force) {
    foreach ($target in $existingTargets) {
        Remove-Item -LiteralPath $target -Recurse -Force
    }
}

dotnet publish $projectPath `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    --output $publishDirectory `
    "-p:Version=$Version" `
    '-p:IncludeSourceRevisionInInformationalVersion=false' `
    '-p:DebugType=None' `
    '-p:DebugSymbols=false'

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$executablePath = Join-Path $publishDirectory 'DragonDeskPet.exe'
if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "Published executable was not found: $executablePath"
}

$assemblyPath = Join-Path $publishDirectory 'DragonDeskPet.dll'
$executable = Get-Item -LiteralPath $executablePath
$productVersion = $executable.VersionInfo.ProductVersion
$fileVersion = $executable.VersionInfo.FileVersion
$assemblyVersion = [System.Reflection.AssemblyName]::GetAssemblyName($assemblyPath).Version.ToString()
$numericVersion = $Version.Split('-', 2)[0]
$expectedBinaryVersion = "$numericVersion.0"

if ($productVersion -ne $Version) {
    throw "Product version mismatch. Expected '$Version', found '$productVersion'."
}

if ($fileVersion -ne $expectedBinaryVersion) {
    throw "File version mismatch. Expected '$expectedBinaryVersion', found '$fileVersion'."
}

if ($assemblyVersion -ne $expectedBinaryVersion) {
    throw "Assembly version mismatch. Expected '$expectedBinaryVersion', found '$assemblyVersion'."
}

Compress-Archive `
    -Path (Join-Path $publishDirectory '*') `
    -DestinationPath $archivePath `
    -CompressionLevel Optimal

$hash = Get-FileHash -LiteralPath $archivePath -Algorithm SHA256
$checksumLine = "$($hash.Hash)  $([System.IO.Path]::GetFileName($archivePath))$([Environment]::NewLine)"
[System.IO.File]::WriteAllText(
    $checksumPath,
    $checksumLine,
    [System.Text.UTF8Encoding]::new($false))

$publishedFiles = Get-ChildItem -LiteralPath $publishDirectory -Recurse -File
$publishedBytes = ($publishedFiles | Measure-Object -Property Length -Sum).Sum
$archiveBytes = (Get-Item -LiteralPath $archivePath).Length

Write-Host "Release directory: $publishDirectory"
Write-Host "Release files: $($publishedFiles.Count)"
Write-Host "Release size: $([math]::Round($publishedBytes / 1MB, 2)) MB"
Write-Host "Product version: $productVersion"
Write-Host "File version: $fileVersion"
Write-Host "Assembly version: $assemblyVersion"
Write-Host "Archive: $archivePath"
Write-Host "Archive size: $([math]::Round($archiveBytes / 1MB, 2)) MB"
Write-Host "SHA-256: $($hash.Hash)"
Write-Host "Checksum file: $checksumPath"
