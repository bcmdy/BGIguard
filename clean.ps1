# BGIguard Clean Script (PowerShell)
# 清理所有非必须文件，包括项目输出

# 设置输出编码为 UTF-8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "BGIguard Clean Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 需要删除的目录
$removeDirs = @(
    "bin",
    "obj",
    "publish",
    "src/BGIguard/bin",
    "src/BGIguard/obj",
    "tests/BGIguard.Tests/bin",
    "tests/BGIguard.Tests/obj",
    "BGIguard.Tests/bin",
    "BGIguard.Tests/obj"
)

Write-Host "Cleaning project output..." -ForegroundColor Yellow

foreach ($dir in $removeDirs) {
    if (Test-Path $dir) {
        Remove-Item -Recurse -Force $dir
        Write-Host "  Deleted: $dir" -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "Clean completed!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
