$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

& (Join-Path $PSScriptRoot "build.ps1")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = Join-Path $root "tests\QuickRewrite.SmokeTests\bin\Debug\net10.0-windows\QuickRewrite.SmokeTests.exe"
$env:LOCALAPPDATA = Join-Path $root ".appdata\Local"
New-Item -ItemType Directory -Path $env:LOCALAPPDATA -Force | Out-Null
& $exe
exit $LASTEXITCODE
