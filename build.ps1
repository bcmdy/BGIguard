# BGIguard Build Script (PowerShell)
param(
    [string]$Version = "5.0.0",
    [switch]$SelfContained,
    [string]$Runtime = "win-x64",
    [string]$OutputDir = "publish"
)

$CONFIG = "Release"
$SOLUTION = "BGIguard.sln"
$PROJECT = "BGIguard.csproj"
$PUBLISH_MODE = if ($SelfContained) { "self-contained" } else { "framework-dependent" }
$SELF_CONTAINED_VALUE = if ($SelfContained) { "true" } else { "false" }

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "BGIguard Build Script v$Version" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Mode: $PUBLISH_MODE" -ForegroundColor Cyan
Write-Host "Runtime: $Runtime" -ForegroundColor Cyan
Write-Host "Output: ./$OutputDir/" -ForegroundColor Cyan
Write-Host ""

# Clean old files
Write-Host "Cleaning old files..." -ForegroundColor Yellow
if (Test-Path $OutputDir) {
    Remove-Item -Recurse -Force $OutputDir
}
if (Test-Path "bin") {
    Remove-Item -Recurse -Force "bin"
}

# Build and test the solution before publishing.
Write-Host ""
Write-Host "Building solution..." -ForegroundColor Yellow
dotnet build $SOLUTION -c $CONFIG

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "[ERROR] Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Running tests..." -ForegroundColor Yellow
dotnet test $SOLUTION -c $CONFIG --no-build

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "[ERROR] Tests failed!" -ForegroundColor Red
    exit 1
}

# Publish project (single file)
Write-Host ""
Write-Host "Publishing project ($PUBLISH_MODE, single file)..." -ForegroundColor Yellow
$publishArgs = @(
    "publish", $PROJECT,
    "-c", $CONFIG,
    "-p:PublishSingleFile=true",
    "--self-contained", $SELF_CONTAINED_VALUE,
    "-p:DebugType=none",
    "-p:DebugSymbols=false",
    "-p:Version=$Version",
    "-p:AssemblyVersion=$Version",
    "-p:FileVersion=$Version",
    "-o", "./$OutputDir",
    "-r", $Runtime
)

dotnet @publishArgs

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "[ERROR] Publish failed!" -ForegroundColor Red
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
Copy-Item "README.md" "$OutputDir/" -Force
Copy-Item "SPEC.md" "$OutputDir/" -Force

# Create ZIP archive in publish directory
Write-Host ""
Write-Host "Creating ZIP archive..." -ForegroundColor Yellow
$zipName = "BGIguard_v$Version.zip"
$zipPath = "$OutputDir/$zipName"

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Compress-Archive -Path "$OutputDir\*" -DestinationPath $zipPath -Force
Write-Host "Created: ./$OutputDir/$zipName" -ForegroundColor Green

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
Write-Host "Output: ./$OutputDir/" -ForegroundColor Green
Write-Host "ZIP: ./$OutputDir/$zipName" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
