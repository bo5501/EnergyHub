#!/usr/bin/env bash
# 编译并部署到 r2modman 的 profile 目录。
set -e

ROOT="$(cd "$(dirname "$0")" && pwd)"
SRC="$ROOT/src/EnergyHub"
PROFILE="${PROFILE:-/c/Users/Administrator/AppData/Roaming/r2modmanPlus-local/DysonSphereProgram/profiles/lianji-mod}"
DEST="$PROFILE/BepInEx/plugins/EnergyHub"
DOTNET="/c/Program Files/dotnet/dotnet"

echo "== 编译"
"$DOTNET" build "$SRC/EnergyHub.csproj" -c Release --nologo -v q

echo "== 部署到 $DEST"
mkdir -p "$DEST"
cp "$SRC/bin/Release/DSPEnergyHub.dll" "$DEST/"
cp "$SRC/bin/Release/DSPEnergyHub.pdb" "$DEST/" 2>/dev/null || true

cat > "$DEST/manifest.json" <<'JSON'
{
  "name": "EnergyHub",
  "version_number": "1.13.0",
  "website_url": "",
  "description": "玻璃棱镜输电塔：星球间按需自动输电，内置超大蓄电器（20 级 100 GJ→1 PJ，封顶 50 TW），升级材料阶梯递增，8 个传送带进口，三级增产剂提速 ×2。",
  "dependencies": [
    "xiaoye97-BepInEx-5.4.17",
    "CommonAPI-CommonAPI-1.6.7",
    "xiaoye97-LDBTool-3.0.3",
    "CommonAPI-DSPModSave-1.2.2"
  ]
}
JSON

ls -l "$DEST"
echo "== 完成。启动游戏读取日志: $PROFILE/BepInEx/LogOutput.log"
