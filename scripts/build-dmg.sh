#!/usr/bin/env bash
# 打包 macOS 磁盘映像：把 Native AOT 产物组装成 .app 后生成 .dmg。
# 用法：build-dmg.sh <版本> <发布目录> <输出目录> <架构后缀>
set -euo pipefail

VERSION="${1:?缺少版本号}"
PUBLISH_DIR="${2:?缺少发布目录}"
OUTPUT_DIR="${3:?缺少输出目录}"
ARCH_SUFFIX="${4:-arm64}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP="$OUTPUT_DIR/ShellX.app"
DMG_NAME="ShellX-$VERSION-osx-$ARCH_SUFFIX.dmg"

[ -x "$PUBLISH_DIR/ShellX" ] || { echo "发布目录缺少可执行文件 ShellX：$PUBLISH_DIR" >&2; exit 1; }
command -v hdiutil >/dev/null || { echo "hdiutil 只能在 macOS 上使用。" >&2; exit 1; }

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$PUBLISH_DIR/ShellX" "$APP/Contents/MacOS/ShellX"
chmod +x "$APP/Contents/MacOS/ShellX"

# 由 512×512 的应用图标生成多尺寸 icns。
ICONSET="$OUTPUT_DIR/ShellX.iconset"
rm -rf "$ICONSET"
mkdir -p "$ICONSET"
SRC="$ROOT/src/ShellX.Desktop/Assets/ShellX.png"
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" "$SRC" --out "$ICONSET/icon_${size}x${size}.png" >/dev/null
  sips -z "$((size * 2))" "$((size * 2))" "$SRC" --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" "$APP/Contents/Resources/ShellX.icns"
rm -rf "$ICONSET"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>ShellX</string>
  <key>CFBundleDisplayName</key><string>ShellX</string>
  <key>CFBundleIdentifier</key><string>io.github.coolqingcheng.shellx</string>
  <key>CFBundleExecutable</key><string>ShellX</string>
  <key>CFBundleIconFile</key><string>ShellX</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>LSApplicationCategoryType</key><string>public.app-category.developer-tools</string>
  <key>NSHumanReadableCopyright</key><string>MIT License</string>
</dict>
</plist>
PLIST

# 未做公证与开发者签名，先做一次 ad-hoc 签名，避免直接提示“已损坏”。
codesign --force --deep --sign - "$APP" >/dev/null 2>&1 || echo "警告：ad-hoc 签名失败，继续打包。"

STAGE="$OUTPUT_DIR/dmg-stage"
rm -rf "$STAGE"
mkdir -p "$STAGE"
cp -a "$APP" "$STAGE/ShellX.app"
ln -s /Applications "$STAGE/Applications"

rm -f "$OUTPUT_DIR/$DMG_NAME"
hdiutil create -volname "ShellX $VERSION" -srcfolder "$STAGE" -ov -format UDZO "$OUTPUT_DIR/$DMG_NAME"
rm -rf "$STAGE"

# 同时提供 .app 的 zip 压缩包，方便免安装使用。
ditto -c -k --sequesterRsrc --keepParent "$APP" "$OUTPUT_DIR/ShellX-$VERSION-osx-$ARCH_SUFFIX.zip"
rm -rf "$APP"

echo "DMG：$OUTPUT_DIR/$DMG_NAME"