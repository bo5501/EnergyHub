#!/usr/bin/env bash
# 编译并部署到 r2modman 的 profile 目录。
set -e

ROOT="$(cd "$(dirname "$0")" && pwd)"
SRC="$ROOT/src/EnergyHub"
PROFILE="${PROFILE:-/c/Users/Administrator/AppData/Roaming/r2modmanPlus-local/DysonSphereProgram/profiles/lianji-mod}"
DEST="$PROFILE/BepInEx/plugins/EnergyHub"
DOTNET="/c/Program Files/dotnet/dotnet"
PY="/c/Users/Administrator/AppData/Local/Python/bin/python"

echo "== 编译（主模块）"
"$DOTNET" build "$SRC/EnergyHub.csproj" -c Release --nologo -v q

echo "== 编译（联机子模块，可选）"
NEBULA_SRC="$ROOT/src/EnergyHub.Nebula"
if "$DOTNET" build "$NEBULA_SRC/EnergyHub.Nebula.csproj" -c Release --nologo -v q 2>/dev/null; then
  NEBULA_OK=1
else
  NEBULA_OK=0
  echo "   ！联机子模块编译失败（多半是没装 NebulaAPI），本次只部署主模块"
fi

echo "== 部署到 $DEST"
mkdir -p "$DEST"
cp "$SRC/bin/Release/DSPEnergyHub.dll" "$DEST/"
cp "$SRC/bin/Release/DSPEnergyHub.pdb" "$DEST/" 2>/dev/null || true

if [ "$NEBULA_OK" = "1" ] && [ -f "$NEBULA_SRC/bin/Release/DSPEnergyHub.Nebula.dll" ]; then
  # 后缀故意用 .bin：BepInEx 不会把它当插件扫描，没装 Nebula 的玩家也不会被它影响
  cp "$NEBULA_SRC/bin/Release/DSPEnergyHub.Nebula.dll" "$DEST/DSPEnergyHub.Nebula.bin"
else
  rm -f "$DEST/DSPEnergyHub.Nebula.bin"
fi

"$PY" "$ROOT/tools/stamp_manifest.py" "$DEST/manifest.json" "$(grep -oP 'Version = "\K[0-9.]+' "$SRC/C.cs" | head -1)"

ls -l "$DEST"
echo "== 完成。启动游戏读取日志: $PROFILE/BepInEx/LogOutput.log"
