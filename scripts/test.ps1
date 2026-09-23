$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet-cli'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.nuget\packages'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

dotnet run --project (Join-Path $projectRoot 'tests\DragonDeskPet.SmokeTests\DragonDeskPet.SmokeTests.csproj')
