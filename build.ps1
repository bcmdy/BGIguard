# BGIguard Build Script (PowerShell)
param(
    [string]$Version = "5.0.0",
    [switch]$SelfContained,
    [string]$Runtime = "win-x64",
    [string]$OutputDir = "publish",
    [switch]$SkipSelfCheck
)

$CONFIG = "Release"
$SOLUTION = "BGIguard.sln"
$PROJECT = "src/BGIguard/BGIguard.csproj"
$PUBLISH_MODE = if ($SelfContained) { "self-contained" } else { "framework-dependent" }
$SELF_CONTAINED_VALUE = if ($SelfContained) { "true" } else { "false" }

function Fail-Build {
    param([string]$Message)

    Write-Host ""
    Write-Host "[ERROR] $Message" -ForegroundColor Red
    exit 1
}

function Assert-FileExists {
    param(
        [string]$Path,
        [string]$Description
    )

    if (!(Test-Path $Path -PathType Leaf)) {
        Fail-Build "$Description not found: $Path"
    }

    $item = Get-Item $Path
    if ($item.Length -le 0) {
        Fail-Build "$Description is empty: $Path"
    }
}

function Remove-DirectoryWithRetry {
    param([string]$Path)

    for ($attempt = 1; $attempt -le 5; $attempt++) {
        try {
            if (Test-Path $Path) {
                Remove-Item -Recurse -Force $Path -ErrorAction Stop
            }
            return
        }
        catch {
            if ($attempt -eq 5) {
                Write-Host "[WARN] Failed to remove temporary directory: $Path" -ForegroundColor Yellow
                return
            }

            Start-Sleep -Milliseconds 200
        }
    }
}

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Invoke-PublishSelfCheck {
    param(
        [string]$OutputDir,
        [string]$Version,
        [string]$ZipPath
    )

    Write-Host ""
    Write-Host "Running publish self-check..." -ForegroundColor Yellow

    if (!(Test-Path $OutputDir -PathType Container)) {
        Fail-Build "Output directory not found: $OutputDir"
    }

    $exePath = Join-Path $OutputDir "BGIguard.exe"
    $readmePath = Join-Path $OutputDir "README.md"
    $specPath = Join-Path $OutputDir "SPEC.md"
    Assert-FileExists $exePath "Published executable"
    Assert-FileExists $readmePath "README"
    Assert-FileExists $specPath "SPEC"

    if (Test-Path (Join-Path $OutputDir "BGIguard_config.json")) {
        Fail-Build "Publish output should not contain BGIguard_config.json"
    }

    if (Get-ChildItem $OutputDir -Filter "BGI_guard*.log" -ErrorAction SilentlyContinue) {
        Fail-Build "Publish output should not contain log files"
    }

    if (Test-IsAdministrator) {
        $smokeDir = Join-Path ([System.IO.Path]::GetTempPath()) ("BGIguard.PublishSmoke." + [Guid]::NewGuid().ToString("N"))
        New-Item -ItemType Directory -Path $smokeDir | Out-Null
        try {
            Copy-Item "$OutputDir\*" "$smokeDir\" -Recurse -Force
            $smokeExe = Join-Path $smokeDir "BGIguard.exe"
            $helpOutput = & $smokeExe help 2>&1

            if ($LASTEXITCODE -ne 0) {
                Fail-Build "Help smoke test failed with exit code $LASTEXITCODE"
            }

            $helpText = $helpOutput -join "`n"
            if ($helpText -notmatch "BGIguard") {
                Fail-Build "Help smoke test output did not contain expected text"
            }

            if (Test-Path (Join-Path $smokeDir "BGIguard_config.json")) {
                Fail-Build "Help smoke test unexpectedly created BGIguard_config.json"
            }

            if (Get-ChildItem $smokeDir -Filter "BGI_guard*.log" -ErrorAction SilentlyContinue) {
                Fail-Build "Help smoke test unexpectedly created log files"
            }
        }
        finally {
            Remove-DirectoryWithRetry $smokeDir
        }
    }
    else {
        Write-Host "Help smoke test skipped because BGIguard.exe requires administrator privileges" -ForegroundColor Yellow
    }

    Assert-FileExists $ZipPath "ZIP archive"

    $zipCheckDir = Join-Path ([System.IO.Path]::GetTempPath()) ("BGIguard.ZipCheck." + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $zipCheckDir | Out-Null
    try {
        Expand-Archive -Path $ZipPath -DestinationPath $zipCheckDir -Force
        Assert-FileExists (Join-Path $zipCheckDir "BGIguard.exe") "ZIP executable"
        Assert-FileExists (Join-Path $zipCheckDir "README.md") "ZIP README"
        Assert-FileExists (Join-Path $zipCheckDir "SPEC.md") "ZIP SPEC"

        if (Test-Path (Join-Path $zipCheckDir "BGIguard_config.json")) {
            Fail-Build "ZIP archive should not contain BGIguard_config.json"
        }

        if (Get-ChildItem $zipCheckDir -Filter "BGI_guard*.log" -ErrorAction SilentlyContinue) {
            Fail-Build "ZIP archive should not contain log files"
        }

        if (Test-Path (Join-Path $zipCheckDir (Split-Path $ZipPath -Leaf))) {
            Fail-Build "ZIP archive should not contain itself"
        }
    }
    finally {
        Remove-DirectoryWithRetry $zipCheckDir
    }

    Write-Host "Publish self-check passed" -ForegroundColor Green
}

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
if (Test-Path "src/BGIguard/bin") {
    Remove-Item -Recurse -Force "src/BGIguard/bin"
}
if (Test-Path "tests/BGIguard.Tests/bin") {
    Remove-Item -Recurse -Force "tests/BGIguard.Tests/bin"
}

# Build and test the solution before publishing.
Write-Host ""
Write-Host "Building solution..." -ForegroundColor Yellow
dotnet build $SOLUTION -c $CONFIG

if ($LASTEXITCODE -ne 0) {
    Fail-Build "Build failed!"
}

Write-Host ""
Write-Host "Running tests..." -ForegroundColor Yellow
dotnet test $SOLUTION -c $CONFIG --no-build

if ($LASTEXITCODE -ne 0) {
    Fail-Build "Tests failed!"
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
    Fail-Build "Publish failed!"
}

# Delete bin directory
if (Test-Path "bin") {
    Remove-Item -Recurse -Force "bin"
    Write-Host "Deleted bin directory" -ForegroundColor Yellow
}
if (Test-Path "src/BGIguard/bin") {
    Remove-Item -Recurse -Force "src/BGIguard/bin"
    Write-Host "Deleted src/BGIguard/bin directory" -ForegroundColor Yellow
}
if (Test-Path "tests/BGIguard.Tests/bin") {
    Remove-Item -Recurse -Force "tests/BGIguard.Tests/bin"
    Write-Host "Deleted tests/BGIguard.Tests/bin directory" -ForegroundColor Yellow
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

try {
    Compress-Archive -Path "$OutputDir\*" -DestinationPath $zipPath -Force
}
catch {
    Fail-Build "ZIP archive creation failed: $($_.Exception.Message)"
}
Write-Host "Created: ./$OutputDir/$zipName" -ForegroundColor Green

if (!$SkipSelfCheck) {
    Invoke-PublishSelfCheck -OutputDir $OutputDir -Version $Version -ZipPath $zipPath
}
else {
    Write-Host "Publish self-check skipped" -ForegroundColor Yellow
}

# Update version in project files
Write-Host ""
Write-Host "Updating version in project files..." -ForegroundColor Yellow

# Update BGIguard.csproj
$csprojPath = "src/BGIguard/BGIguard.csproj"
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
