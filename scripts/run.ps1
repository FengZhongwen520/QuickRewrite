$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "src\QuickRewrite\bin\Debug\net10.0-windows\QuickRewrite.exe"

if (-not (Test-Path -LiteralPath $exe)) {
    & (Join-Path $PSScriptRoot "build.ps1")
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$runtimeDir = Join-Path $root ".runtime"
New-Item -ItemType Directory -Path $runtimeDir -Force | Out-Null
$env:QUICKREWRITE_CONFIG = Join-Path $runtimeDir "settings.json"

Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe) -WindowStyle Hidden
Write-Host "QuickRewrite 已启动。选中文字后按 Ctrl+Alt+R，托盘图标中可打开设置或退出。"