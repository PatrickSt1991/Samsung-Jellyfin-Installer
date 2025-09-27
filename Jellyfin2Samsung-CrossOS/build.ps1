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
dotnet publish -c $Configuration -r win-x64 --self-contained -o "$OutputDir/win-x64"

Write-Host "Publishing for macOS x64..." -ForegroundColor Green
dotnet publish -c $Configuration -r osx-x64 --self-contained -o "$OutputDir/osx-x64"

Write-Host "Publishing for macOS arm64..." -ForegroundColor Green
dotnet publish -c $Configuration -r osx-arm64 --self-contained -o "$OutputDir/osx-arm64"

Write-Host "Publishing for Linux..." -ForegroundColor Green
dotnet publish -c $Configuration -r linux-x64 --self-contained -o "$OutputDir/linux-x64"

# -------------------------------
# Create macOS app bundles
# -------------------------------
function Create-MacOSAppBundle($arch) {
    $MacOSAppPath = "$OutputDir/$arch/$ProjectName.app"
    Write-Host "Creating macOS $arch app bundle..." -ForegroundColor Green

    New-Item -ItemType Directory -Path "$MacOSAppPath/Contents/MacOS" -Force
    New-Item -ItemType Directory -Path "$MacOSAppPath/Contents/Resources" -Force
    New-Item -ItemType Directory -Path "$MacOSAppPath/Contents/Frameworks" -Force

    # Move executable
    Move-Item "$OutputDir/$arch/$ProjectName" "$MacOSAppPath/Contents/MacOS/"

    # Copy Info.plist
    Copy-Item "Info.plist" "$MacOSAppPath/Contents/"

    # Copy Assets folder next to executable
    if (Test-Path "Assets") {
        Copy-Item "Assets" "$MacOSAppPath/Contents/MacOS/" -Recurse
    }

    # Copy dylibs into Frameworks
    $dylibs = Get-ChildItem "$OutputDir/$arch" -Filter "*.dylib"
    foreach ($lib in $dylibs) {
        Copy-Item $lib.FullName "$MacOSAppPath/Contents/Frameworks/"
    }

    $RunScriptSource = "./run_macos.sh"   # adjust if needed
    $RunScriptDest = "$OutputDir/$arch/run_macos.sh"
    if (Test-Path $RunScriptSource) {
        Copy-Item $RunScriptSource $RunScriptDest -Force
        Write-Host "Copied run_macos.sh to $RunScriptDest"
    } else {
        Write-Host "WARNING: run_macos.sh not found at $RunScriptSource" -ForegroundColor Yellow
    }
}

Create-MacOSAppBundle "osx-x64"
Create-MacOSAppBundle "osx-arm64"

# -------------------------------
# Copy Linux desktop file and icon
# -------------------------------
Write-Host "Setting up Linux package..." -ForegroundColor Green
Copy-Item "jellyfin2samsung.desktop" "$OutputDir/linux-x64/"
if (Test-Path "Assets/jelly2sams.png") {
    Copy-Item "Assets/jelly2sams.png" "$OutputDir/linux-x64/"
}

Write-Host "Build complete! Check the $OutputDir folder." -ForegroundColor Yellow
