#!/usr/bin/env bash
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PYTHON_BIN="${PYTHON:-python3}"
SRC_MAIN="$PROJECT_ROOT/src/app.py"
FFMPEG_DIR="$PROJECT_ROOT/third_party/ffmpeg/mac-universal"
DIST_DIR="$PROJECT_ROOT/dist"
PACKAGE_DIR="$DIST_DIR/VideoSplitter-macos"

if [[ ! -f "$SRC_MAIN" ]]; then
  echo "Could not find $SRC_MAIN. Run this script from within the VideoSplitter directory." >&2
  exit 1
fi

if ! command -v pyinstaller >/dev/null 2>&1; then
  echo "PyInstaller is not installed. Run \"$PYTHON_BIN -m pip install --upgrade pyinstaller\" first." >&2
  exit 1
fi

if [[ ! -x "$FFMPEG_DIR/ffmpeg" ]] || [[ ! -x "$FFMPEG_DIR/ffprobe" ]]; then
  echo "FFmpeg binaries were not found under $FFMPEG_DIR." >&2
  echo "Download a macOS build (e.g. from https://evermeet.cx/ffmpeg/ or Brew) and place ffmpeg & ffprobe there." >&2
  exit 1
fi

ICON_ARGS=()
if [[ -f "$PROJECT_ROOT/assets/icon.icns" ]]; then
  ICON_ARGS=(--icon "$PROJECT_ROOT/assets/icon.icns")
elif [[ -f "$PROJECT_ROOT/assets/icon.ico" ]]; then
  ICON_ARGS=(--icon "$PROJECT_ROOT/assets/icon.ico")
fi

pyinstaller \
  --onefile \
  --windowed \
  --name VideoSplitter \
  --paths "$PROJECT_ROOT/src" \
  --add-binary "$FFMPEG_DIR/ffmpeg:." \
  --add-binary "$FFMPEG_DIR/ffprobe:." \
  "${ICON_ARGS[@]}" \
  "$SRC_MAIN"

rm -rf "$PACKAGE_DIR"
mkdir -p "$PACKAGE_DIR"
cp "$DIST_DIR/VideoSplitter" "$PACKAGE_DIR/VideoSplitter"
if [[ -f "$FFMPEG_DIR/LICENSE.txt" ]]; then
  cp "$FFMPEG_DIR/LICENSE.txt" "$PACKAGE_DIR/FFmpeg_LICENSE.txt"
fi
cp "$PROJECT_ROOT/README.md" "$PACKAGE_DIR/README.txt"

(cd "$DIST_DIR" && zip -r VideoSplitter-macos.zip VideoSplitter-macos >/dev/null)

echo "macOS package written to $DIST_DIR/VideoSplitter-macos.zip"
