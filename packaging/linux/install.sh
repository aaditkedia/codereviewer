#!/usr/bin/env sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
DATA_ROOT=${XDG_DATA_HOME:-"${HOME}/.local/share"}
BIN_ROOT=${XDG_BIN_HOME:-"${HOME}/.local/bin"}
APP_DIR="${DATA_ROOT}/codeviewer"
DESKTOP_DIR="${DATA_ROOT}/applications"

mkdir -p "$APP_DIR" "$BIN_ROOT" "$DESKTOP_DIR"
cp "$SCRIPT_DIR/codeviewer" "$APP_DIR/codeviewer"
chmod +x "$APP_DIR/codeviewer"
ln -sf "$APP_DIR/codeviewer" "$BIN_ROOT/codeviewer"
sed "s|@EXECUTABLE@|$APP_DIR/codeviewer|g" \
  "$SCRIPT_DIR/io.github.aaditkedia.codeviewer.desktop" \
  > "$DESKTOP_DIR/io.github.aaditkedia.codeviewer.desktop"

if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database "$DESKTOP_DIR" >/dev/null 2>&1 || true
fi

echo "Installed codeviewer to $APP_DIR"
echo "Launch it from your application menu or run: $BIN_ROOT/codeviewer"
