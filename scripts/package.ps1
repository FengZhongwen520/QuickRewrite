param(
    [string]$Version,
    [switch]$SkipTests,
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\QuickRewrite\QuickRewrite.csproj"
$publishDir = Join-Path $root ".package\publish"
$distDir = Join-Path $root "dist"
$issPath = Join-Path $root "installer\QuickRewrite.iss"
$realLocalAppData = $env:LOCALAPPDATA
$realAppData = $env:APPDATA

$env:DOTNET_CLI_HOME = Join-Path $root ".dotnet"
$env:NUGET_PACKAGES = Join-Path $root ".nuget\packages"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_NOLOGO = "1"
New-Item -ItemType Directory -Path $env:DOTNET_CLI_HOME, $env:NUGET_PACKAGES -Force | Out-Null

if (-not $Version) {
    [xml]$projectXml = Get-Content -LiteralPath $project -Raw
    $Version = @($projectXml.Project.PropertyGroup.Version) |
        Where-Object { $_ } |
        Select-Object -First 1
}

if (-not $Version) {
    throw "无法从项目文件读取版本号，请使用 -Version 指定，例如：.\scripts\package.ps1 -Version 1.0.0"
}

if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') {
    throw "版本号格式应为 x.y.z 或 x.y.z.w，例如 1.2.0。"
}

if (-not $SkipTests) {
    Write-Host "正在运行冒烟测试..."
    & (Join-Path $PSScriptRoot "test.ps1")
    if ($LASTEXITCODE -ne 0) {
        throw "测试未通过，已停止打包。"
    }
}

$env:LOCALAPPDATA = $realLocalAppData
$env:APPDATA = $realAppData

if (-not $SkipPublish) {
    if (Test-Path -LiteralPath $publishDir) {
        Remove-Item -LiteralPath $publishDir -Recurse -Force
    }

    New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

    Write-Host "正在发布自包含版本 $Version ..."
    dotnet publish $project -c Release -r win-x64 --self-contained true `
        -p:Version=$Version -p:DebugType=None -p:DebugSymbols=false `
        -o $publishDir --source https://api.nuget.org/v3/index.json

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish 失败。"
    }
}

if (-not (Test-Path -LiteralPath (Join-Path $publishDir "QuickRewrite.exe"))) {
    throw "发布目录中没有 QuickRewrite.exe，请先执行发布。"
}

$candidates = [System.Collections.Generic.List[string]]::new()
if ($env:ISCC_PATH) { $candidates.Add($env:ISCC_PATH) }

$isccCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
if ($isccCommand) { $candidates.Add($isccCommand.Source) }
if ($realLocalAppData) {
    $candidates.Add((Join-Path $realLocalAppData "Programs\Inno Setup 6\ISCC.exe"))
}
if (${env:ProgramFiles(x86)}) {
    $candidates.Add((Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"))
}
if ($env:ProgramFiles) {
    $candidates.Add((Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"))
}

$iscc = $candidates |
    Where-Object { Test-Path -LiteralPath $_ } |
    Select-Object -First 1

if (-not $iscc) {
    throw "未找到 Inno Setup 编译器 ISCC.exe。请先安装：winget install -e --id JRSoftware.InnoSetup --scope user"
}

New-Item -ItemType Directory -Path $distDir -Force | Out-Null

Write-Host "正在编译安装包..."
& $iscc "/DAppVersion=$Version" "/DPublishDir=$publishDir" "/DOutputDir=$distDir" $issPath

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup 编译失败。"
}

$setupPath = Join-Path $distDir "QuickRewrite-Setup-$Version-x64.exe"
if (-not (Test-Path -LiteralPath $setupPath)) {
    throw "未生成安装包：$setupPath"
}

$hash = Get-FileHash -LiteralPath $setupPath -Algorithm SHA256
"$($hash.Hash)  $([System.IO.Path]::GetFileName($setupPath))" |
    Set-Content -LiteralPath "$setupPath.sha256" -Encoding ascii
$sizeMb = [math]::Round((Get-Item -LiteralPath $setupPath).Length / 1MB, 1)

Write-Host ""
Write-Host "安装包已生成：$setupPath（$sizeMb MB）"
Write-Host "SHA-256：$($hash.Hash)"
