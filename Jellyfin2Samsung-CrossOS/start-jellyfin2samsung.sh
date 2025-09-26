#!/bin/bash

# Get the directory of this script, works even if cwd is different
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Binary is in the same folder
APP_BIN="$SCRIPT_DIR/Jellyfin2SamsungCrossOS"

# Check root
if [ "$(id -u)" -ne 0 ]; then
    if command -v pkexec >/dev/null 2>&1; then
        exec pkexec "$APP_BIN" "$@"
    else
        exec sudo "$APP_BIN" "$@"
    fi
else
    exec "$APP_BIN" "$@"
fi
