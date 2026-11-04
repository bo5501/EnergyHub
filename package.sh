#!/usr/bin/env bash
# 打 Thunderstore 发布包：编译 → 图标 → 组装 → zip。
# 产物：dist/EnergyHub-<version>.zip
set -e

ROOT="$(cd "$(dirname "$0")" && pwd)"
PKG="$ROOT/dist/EnergyHub"
VERSION="$(grep -oP 'Version = "\K[0-9.]+' "$ROOT/src/EnergyHub/C.cs" | head -1)"
PY="/c/Users/Administrator/AppData/Local/Python/bin/python"

echo "== 版本 $VERSION"

# 1) 编译（不部署：游戏开着时 plugins 里的 dll 会被锁住，打包只需要 bin/Release）
DOTNET="/c/Program Files/dotnet/dotnet"
echo "== 编译"
"$DOTNET" build "$ROOT/src/EnergyHub/EnergyHub.csproj" -c Release --nologo -v q

NEBULA_SRC="$ROOT/src/EnergyHub.Nebula"
if "$DOTNET" build "$NEBULA_SRC/EnergyHub.Nebula.csproj" -c Release --nologo -v q 2>/dev/null; then
  NEBULA_OK=1
else
  NEBULA_OK=0
  echo "   ！联机子模块未编译（没装 NebulaAPI），发布包将不含 DSPEnergyHub.Nebula.bin"
fi

# 2) 图标（256x256 PNG）
"$PY" "$ROOT/tools/make_icon.py"

# 3) 组装包目录
mkdir -p "$PKG/BepInEx/plugins/EnergyHub"
cp "$ROOT/src/EnergyHub/bin/Release/DSPEnergyHub.dll" "$PKG/BepInEx/plugins/EnergyHub/"
# 只发 dll：pdb 是调试符号，对玩家没用还占体积

if [ "$NEBULA_OK" = "1" ] && [ -f "$NEBULA_SRC/bin/Release/DSPEnergyHub.Nebula.dll" ]; then
  cp "$NEBULA_SRC/bin/Release/DSPEnergyHub.Nebula.dll" \
     "$PKG/BepInEx/plugins/EnergyHub/DSPEnergyHub.Nebula.bin"
else
  rm -f "$PKG/BepInEx/plugins/EnergyHub/DSPEnergyHub.Nebula.bin"
fi

# 3.5) manifest：版本号与短描述统一由 tools/stamp_manifest.py 生成，避免每版手改漏字段
"$PY" "$ROOT/tools/stamp_manifest.py" "$PKG/manifest.json" "$VERSION"

# 4) 校验：manifest 版本 / 图标尺寸 / 文件齐全
"$PY" - "$PKG" "$VERSION" <<'PY'
import json, os, sys
from PIL import Image

pkg, ver = sys.argv[1], sys.argv[2]
need = ["manifest.json", "icon.png", "README.md",
        "BepInEx/plugins/EnergyHub/DSPEnergyHub.dll"]
for f in need:
    p = os.path.join(pkg, f)
    assert os.path.isfile(p), "缺文件: " + f
    print("  ok  %-46s %8d B" % (f, os.path.getsize(p)))

# 联机子模块是可选文件：有就打进包，没有也能正常发布（只影响联机同步）
neb = os.path.join(pkg, "BepInEx/plugins/EnergyHub/DSPEnergyHub.Nebula.bin")
if os.path.isfile(neb):
    print("  ok  %-46s %8d B" % ("BepInEx/plugins/EnergyHub/DSPEnergyHub.Nebula.bin",
                                 os.path.getsize(neb)))
else:
    print("  --  联机子模块缺失：本包不含联机同步（单机不受影响）")

m = json.load(open(os.path.join(pkg, "manifest.json"), encoding="utf-8"))
assert m["name"] == "EnergyHub", m["name"]
assert m["version_number"] == ver, (m["version_number"], ver)
assert len(m["description"]) <= 250, len(m["description"])
assert m["dependencies"], "dependencies 为空"
for d in m["dependencies"]:
    assert d.count("-") >= 2, "依赖格式不对: " + d
img = Image.open(os.path.join(pkg, "icon.png"))
assert img.size == (256, 256) and img.format == "PNG", (img.size, img.format)
print("  ok  manifest %s / icon %s / deps %d" % (m["version_number"], img.size, len(m["dependencies"])))
PY

# 5) 打包（zip 根目录直接是 manifest/icon/README/BepInEx）
OUT="$ROOT/dist/EnergyHub-$VERSION.zip"
rm -f "$OUT"
"$PY" - "$PKG" "$OUT" <<'PY'
import os, sys, zipfile
pkg, out = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for root, _, files in os.walk(pkg):
        for f in sorted(files):
            full = os.path.join(root, f)
            rel = os.path.relpath(full, pkg).replace("\\", "/")
            z.write(full, rel)
print("zip:", out)
with zipfile.ZipFile(out) as z:
    for i in z.infolist():
        print("   %-52s %8d B" % (i.filename, i.file_size))
PY

echo "== 完成: $OUT"
