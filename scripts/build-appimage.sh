#!/usr/bin/env bash
# 打包 Linux AppImage：把 Native AOT 产物整理成 AppDir 后用 appimagetool 封装。
# 用法：build-appimage.sh <版本> <发布目录> <输出目录>
set -euo pipefail

VERSION="${1:?缺少版本号}"
PUBLISH_DIR="${2:?缺少发布目录}"
OUTPUT_DIR="${3:?缺少输出目录}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APPDIR="$OUTPUT_DIR/AppDir"
APPIMAGE_NAME="ShellX-$VERSION-linux-x86_64.AppImage"
APPIMAGETOOL_URL="https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage"

command -v mksquashfs >/dev/null || { echo "缺少 mksquashfs，请先安装 squashfs-tools。" >&2; exit 1; }
[ -x "$PUBLISH_DIR/ShellX" ] || { echo "发布目录缺少可执行文件 ShellX：$PUBLISH_DIR" >&2; exit 1; }

rm -rf "$APPDIR"
mkdir -p "$APPDIR" "$OUTPUT_DIR"
cp -a "$PUBLISH_DIR/." "$APPDIR/"

cat > "$APPDIR/AppRun" <<'APPRUN'
#!/bin/sh
# AppImage 入口：始终以脚本所在目录为工作目录解析可执行文件。
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/ShellX" "$@"
APPRUN
chmod +x "$APPDIR/AppRun"

cat > "$APPDIR/shellx.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=ShellX
Comment=SSH 桌面工作台：终端 / 文件传输 / 端口转发 / 服务器监控
Exec=ShellX
Icon=shellx
Categories=Development;TerminalEmulator;Network;
Keywords=SSH;Terminal;SFTP;Remote;Monitor;
Terminal=false
StartupNotify=true
DESKTOP

cp "$ROOT/src/ShellX.Desktop/Assets/ShellX.png" "$APPDIR/shellx.png"

if command -v desktop-file-validate >/dev/null; then
  desktop-file-validate "$APPDIR/shellx.desktop" || echo "警告：desktop 文件存在非致命问题。"
fi

# appimagetool 放在临时目录，避免打包失败时残留在发布产物里。
TOOL_DIR="$(mktemp -d)"
trap 'rm -rf "$TOOL_DIR"' EXIT
TOOL="$TOOL_DIR/appimagetool.AppImage"
echo "下载 appimagetool…"
curl -fsSL --retry 3 --retry-delay 2 -o "$TOOL" "$APPIMAGETOOL_URL"
chmod +x "$TOOL"

# CI 环境通常没有 FUSE，用 APPIMAGE_EXTRACT_AND_RUN 让 appimagetool 自解压运行。
export APPIMAGE_EXTRACT_AND_RUN=1
export ARCH=x86_64
"$TOOL" "$APPDIR" "$OUTPUT_DIR/$APPIMAGE_NAME"
rm -rf "$APPDIR"

echo "AppImage：$OUTPUT_DIR/$APPIMAGE_NAME"