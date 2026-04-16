# BGIguard Build Script (PowerShell)
param(
    [string]$Version = "2.2.1"
)

$CONFIG = "Release"
$OUTPUT_DIR = "publish"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "BGIguard Build Script v$Version" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Clean old files
Write-Host "Cleaning old files..." -ForegroundColor Yellow
if (Test-Path $OUTPUT_DIR) {
    Remove-Item -Recurse -Force $OUTPUT_DIR
}
if (Test-Path "bin") {
    Remove-Item -Recurse -Force "bin"
}

# Build project (single file, requires .NET 8 runtime)
Write-Host ""
Write-Host "Building project (single file)..." -ForegroundColor Yellow
# 设置完整版本号 (4位) 用于程序集版本，Version 用于显示版本
dotnet publish -c $CONFIG -p:PublishSingleFile=true --self-contained false -p:DebugType=none -p:DebugSymbols=false -p:Version=$Version -p:AssemblyVersion=$Version -p:FileVersion=$Version -o ./$OUTPUT_DIR

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "[ERROR] Build failed!" -ForegroundColor Red
    exit 1
}

# 删除 bin 目录
if (Test-Path "bin") {
    Remove-Item -Recurse -Force "bin"
    Write-Host "已删除 bin 目录" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "Build completed successfully!" -ForegroundColor Green
Write-Host "Output: ./$OUTPUT_DIR/" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green

Write-Host ""
Write-Host "Files in output directory:" -ForegroundColor Yellow
Get-ChildItem $OUTPUT_DIR | ForEach-Object { Write-Host "  $($_.Name)" }