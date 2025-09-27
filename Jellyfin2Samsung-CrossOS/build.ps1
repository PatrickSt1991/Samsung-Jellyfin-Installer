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

Write-Host "Publishing for Windows..." -ForegroundColor Green
dotnet publish -c $Configuration -r win-x64 --self-contained -o "$OutputDir/win-x64"

Write-Host "Publishing for macOS x64..." -ForegroundColor Green
dotnet publish -c $Configuration -r osx-x64 --self-contained -o "$OutputDir/osx-x64"

Write-Host "Publishing for macOS arm64..." -ForegroundColor Green
dotnet publish -c $Configuration -r osx-arm64 --self-contained -o "$OutputDir/osx-arm64"

Write-Host "Publishing for Linux..." -ForegroundColor Green
dotnet publish -c $Configuration -r linux-x64 --self-contained -o "$OutputDir/linux-x64"

# Create macOS osx64 app bundle
Write-Host "Creating macOS app bundle..." -ForegroundColor Green
$MacOSAppPath = "$OutputDir/osx-x64/$ProjectName.app"
New-Item -ItemType Directory -Path "$MacOSAppPath/Contents/MacOS" -Force
New-Item -ItemType Directory -Path "$MacOSAppPath/Contents/Resources" -Force

Move-Item "$OutputDir/osx-x64/$ProjectName" "$MacOSAppPath/Contents/MacOS/"
Copy-Item "Info.plist" "$MacOSAppPath/Contents/"
if (Test-Path "Assets/jelly2sams.icns") {
    Copy-Item "Assets/jelly2sams.icns" "$MacOSAppPath/Contents/Resources/"
}

# Create macOS osx-arm64 app bundle
Write-Host "Creating macOS osx-arm64 app bundle..." -ForegroundColor Green
$MacOSAppPath = "$OutputDir/osx-arm64/$ProjectName.app"
New-Item -ItemType Directory -Path "$MacOSAppPath/Contents/MacOS" -Force
New-Item -ItemType Directory -Path "$MacOSAppPath/Contents/Resources" -Force

Move-Item "$OutputDir/osx-arm64/$ProjectName" "$MacOSAppPath/Contents/MacOS/"
Copy-Item "Info.plist" "$MacOSAppPath/Contents/"
if (Test-Path "Assets/jelly2sams.icns") {
    Copy-Item "Assets/jelly2sams.icns" "$MacOSAppPath/Contents/Resources/"
}

# Copy Linux desktop file and icon
Write-Host "Setting up Linux package..." -ForegroundColor Green
Copy-Item "jellyfin2samsung.desktop" "$OutputDir/linux-x64/"
if (Test-Path "Assets/jelly2sams.png") {
    Copy-Item "Assets/jelly2sams.png" "$OutputDir/linux-x64/"
}

Write-Host "Build complete! Check the $OutputDir folder." -ForegroundColor Yellow