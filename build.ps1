# BGIguard Build Script (PowerShell)
param(
    [string]$Version = "1.0.0"
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

# Build project (single file, requires .NET 8 runtime)
Write-Host ""
Write-Host "Building project (single file)..." -ForegroundColor Yellow
dotnet publish -c $CONFIG -p:PublishSingleFile=true --self-contained false -p:DebugType=none -p:DebugSymbols=false -p:Version=$Version -o ./$OUTPUT_DIR

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "[ERROR] Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "Build completed successfully!" -ForegroundColor Green
Write-Host "Output: ./$OUTPUT_DIR/" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green

Write-Host ""
Write-Host "Files in output directory:" -ForegroundColor Yellow
Get-ChildItem $OUTPUT_DIR | ForEach-Object { Write-Host "  $($_.Name)" }