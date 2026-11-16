$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

$env:DOTNET_CLI_HOME = Join-Path $root ".dotnet"
$env:APPDATA = Join-Path $root ".appdata\Roaming"
$env:LOCALAPPDATA = Join-Path $root ".appdata\Local"
$env:NUGET_PACKAGES = Join-Path $root ".nuget\packages"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_NOLOGO = "1"

New-Item -ItemType Directory -Path $env:APPDATA, $env:LOCALAPPDATA -Force | Out-Null
dotnet build (Join-Path $root "QuickRewrite.slnx") -c Debug --configfile (Join-Path $root "NuGet.Config")
exit $LASTEXITCODE