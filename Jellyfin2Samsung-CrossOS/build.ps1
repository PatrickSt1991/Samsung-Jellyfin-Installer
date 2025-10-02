# Build script for cross-platform publishing
param(
    [string]$Configuration = "Release"
)

$ProjectName = "Jellyfin2SamsungCrossOS"
$OutputDir = "./publish"

# Clean previous builds
if (Test-Path $OutputDir) {
    Remove-Item $OutputDir -Recurse -Force
}

# -------------------------------
# Publish for all platforms
# -------------------------------
Write-Host "Publishing for Windows..." -ForegroundColor Green
dotnet publish -c $Configuration -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishTrimmed=false -o "$OutputDir/win-x64"

Write-Host "Publishing for macOS x64..." -ForegroundColor Green
dotnet publish -c $Configuration -r osx-x64 --self-contained -p:PublishSingleFile=true -p:PublishTrimmed=false -o "$OutputDir/osx-x64"

Write-Host "Publishing for macOS arm64..." -ForegroundColor Green
dotnet publish -c $Configuration -r osx-arm64 --self-contained -p:PublishSingleFile=true -p:PublishTrimmed=false -o "$OutputDir/osx-arm64"

Write-Host "Publishing for Linux..." -ForegroundColor Green
dotnet publish -c $Configuration -r linux-x64 --self-contained -p:PublishSingleFile=true -p:PublishTrimmed=false -o "$OutputDir/linux-x64"

# -------------------------------
# Setup macOS portable executables
# -------------------------------
function Setup-MacOSPortable($arch) {
    Write-Host "Setting up macOS $arch portable executable..." -ForegroundColor Green
    
    # Copy Assets folder if it exists
    if (Test-Path "Assets") {
        Copy-Item "Assets" "$OutputDir/$arch/" -Recurse -Force
        Write-Host "Copied Assets to $arch build"
    }
}

Setup-MacOSPortable "osx-x64"
Setup-MacOSPortable "osx-arm64"

# -------------------------------
# Copy Linux desktop file and icon
# -------------------------------
Write-Host "Setting up Linux package..." -ForegroundColor Green
Copy-Item "jellyfin2samsung.desktop" "$OutputDir/linux-x64/"
if (Test-Path "Assets/jelly2sams.png") {
    Copy-Item "Assets/jelly2sams.png" "$OutputDir/linux-x64/"
}

Write-Host "Build complete! Check the $OutputDir folder." -ForegroundColor Yellow
Write-Host "macOS users should run: chmod +x $ProjectName" -ForegroundColor Cyan