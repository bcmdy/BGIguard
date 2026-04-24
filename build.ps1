# BGIguard Build Script (PowerShell)
param(
    [string]$Version = "3.0.0"
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
dotnet publish -c $CONFIG -p:PublishSingleFile=true --self-contained false -p:DebugType=none -p:DebugSymbols=false -p:Version=$Version -p:AssemblyVersion=$Version -p:FileVersion=$Version -o ./$OUTPUT_DIR

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "[ERROR] Build failed!" -ForegroundColor Red
    exit 1
}

# Delete bin directory
if (Test-Path "bin") {
    Remove-Item -Recurse -Force "bin"
    Write-Host "Deleted bin directory" -ForegroundColor Yellow
}

# Copy README and SPEC to output directory
Write-Host ""
Write-Host "Copying documentation files..." -ForegroundColor Yellow
Copy-Item "README.md" "$OUTPUT_DIR/" -Force
Copy-Item "SPEC.md" "$OUTPUT_DIR/" -Force

# Create ZIP archive in publish directory
Write-Host ""
Write-Host "Creating ZIP archive..." -ForegroundColor Yellow
$zipName = "BGIguard_v$Version.zip"
$zipPath = "$OUTPUT_DIR/$zipName"

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Compress-Archive -Path "$OUTPUT_DIR\*" -DestinationPath $zipPath -Force
Write-Host "Created: ./$OUTPUT_DIR/$zipName" -ForegroundColor Green

# Update version in project files
Write-Host ""
Write-Host "Updating version in project files..." -ForegroundColor Yellow

# Update BGIguard.csproj
$csprojPath = "BGIguard.csproj"
if (Test-Path $csprojPath) {
    $csprojContent = Get-Content $csprojPath -Raw
    $csprojContent = $csprojContent -replace '<Version>\d+\.\d+\.\d+</Version>', "<Version>$Version</Version>"
    $csprojContent = $csprojContent -replace '<AssemblyVersion>\d+\.\d+\.\d+\.0</AssemblyVersion>', "<AssemblyVersion>$Version.0</AssemblyVersion>"
    $csprojContent = $csprojContent -replace '<FileVersion>\d+\.\d+\.\d+\.0</FileVersion>', "<FileVersion>$Version.0</FileVersion>"
    Set-Content -Path $csprojPath -Value $csprojContent -NoNewline -Encoding UTF8
    Write-Host "Updated: $csprojPath" -ForegroundColor Green
}

# Update this script
$scriptPath = $MyInvocation.MyCommand.Path
$scriptContent = Get-Content $scriptPath -Raw
$scriptContent = $scriptContent -replace '(?<=Version\s*=\s*")[\d.]+(?=")', $Version
Set-Content -Path $scriptPath -Value $scriptContent -NoNewline -Encoding UTF8
Write-Host "Updated: $scriptPath" -ForegroundColor Green

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "Build completed successfully!" -ForegroundColor Green
Write-Host "Output: ./$OUTPUT_DIR/" -ForegroundColor Green
Write-Host "ZIP: ./$OUTPUT_DIR/$zipName" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green