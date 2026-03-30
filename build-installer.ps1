<#
.SYNOPSIS
    一键构建 FastTaskManager MSI 安装包

.PARAMETER Version
    版本号，默认 1.0.0

.PARAMETER Configuration
    构建配置，默认 Release

.EXAMPLE
    .\build-installer.ps1
    .\build-installer.ps1 -Version 1.2.0
#>
param(
    [string]$Version = "1.0.0",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root        = $PSScriptRoot
$appProj     = "$root\FastTaskManager.App\FastTaskManager.App.csproj"
$installerProj = "$root\FastTaskManager.Installer\FastTaskManager.Installer.wixproj"
$publishDir  = "$root\publish\win-x64"

# ── 注意 ──────────────────────────────────────────────────────────────────────
# dotnet build 通过 NuGet SDK (WixToolset.Sdk) 打包，无需 wix CLI 或 extension add
# 如果遇到 SDK 找不到，确保能访问 nuget.org 即可
Write-Host "[1/3] 检查环境..." -ForegroundColor DarkGray

# ── 发布应用 ─────────────────────────────────────────────────────────────────
Write-Host "[2/3] 发布应用 (self-contained, win-x64)..." -ForegroundColor Cyan

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

dotnet publish $appProj `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:Version="$Version.0" `
    -o $publishDir

if ($LASTEXITCODE -ne 0) { Write-Error "发布失败" }

# ── 打包 MSI ─────────────────────────────────────────────────────────────────
Write-Host "[3/3] 打包 MSI..." -ForegroundColor Cyan

dotnet build $installerProj `
    -c $Configuration `
    "-p:PublishDir=$publishDir\" `
    -p:Version="$Version.0"

if ($LASTEXITCODE -ne 0) { Write-Error "MSI 打包失败" }

# ── 输出结果 ─────────────────────────────────────────────────────────────────
$msi = Get-ChildItem "$root\FastTaskManager.Installer\bin\$Configuration\*.msi" |
       Sort-Object LastWriteTime -Descending |
       Select-Object -First 1

if ($msi) {
    Write-Host ""
    Write-Host "✅ 打包成功！" -ForegroundColor Green
    Write-Host "   $($msi.FullName)" -ForegroundColor Green
    Write-Host "   大小: $([math]::Round($msi.Length / 1MB, 1)) MB" -ForegroundColor Green
} else {
    Write-Error "未找到生成的 MSI 文件"
}
