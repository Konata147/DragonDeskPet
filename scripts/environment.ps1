$projectRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet-cli'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.nuget\packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $projectRoot '.nuget\http-cache'
$env:NUGET_PLUGINS_CACHE_PATH = Join-Path $projectRoot '.nuget\plugins-cache'
$env:TEMP = Join-Path $projectRoot '.dotnet-cli\temp'
$env:TMP = $env:TEMP
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
New-Item -ItemType Directory -Force -Path $env:TEMP | Out-Null
