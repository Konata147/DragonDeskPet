$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'environment.ps1')
. (Join-Path $PSScriptRoot 'get-ocr-models.ps1')
$projectRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet-cli'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.nuget\packages'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$outputDirectory = Join-Path $projectRoot 'dist\DragonDeskPet'

dotnet publish (Join-Path $projectRoot 'src\DragonDeskPet\DragonDeskPet.csproj') `
    --configuration Release `
    --output $outputDirectory `
    --no-self-contained
if ($LASTEXITCODE -ne 0) { throw "Publish failed ($LASTEXITCODE)" }

Write-Host "Published to $outputDirectory"
