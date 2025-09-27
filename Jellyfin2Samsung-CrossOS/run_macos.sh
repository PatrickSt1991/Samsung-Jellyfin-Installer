#!/usr/bin/env bash

APP_NAME="Jellyfin2SamsungCrossOS.app"
EXECUTABLE="$APP_NAME/Contents/MacOS/Jellyfin2SamsungCrossOS"
FRAMEWORKS="$APP_NAME/Contents/Frameworks"

# Ensure executable has the right permissions
chmod +x "$EXECUTABLE"

# Fix all .dylib IDs and rpaths
for dylib in "$FRAMEWORKS"/*.dylib; do
    install_name_tool -id "@executable_path/../Frameworks/$(basename "$dylib")" "$dylib"
done

install_name_tool -add_rpath "@executable_path/../Frameworks" "$EXECUTABLE"

# Run the actual app
"$EXECUTABLE"