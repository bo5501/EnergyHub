#!/usr/bin/env bash
# 编译并部署到 r2modman 的 profile 目录。
#
# 部署产物只有一个文件：
#   DSPEnergyHub.dll           主模块（必需）
# 不再产/不再拷 DSPEnergyHub.pdb，也不再有 DSPEnergyHub.Nebula.bin
# （1.19.3 起移除联机桥接子模块，Thunderstore 只接受单个插件 dll）。
#
# 用法：
#   bash build.sh                    # 默认 profile = chuangshi-lianji
#   PROFILE=/path/to/profile bash build.sh
#   bash build.sh --no-deploy        # 只编译，不部署
set -e

ROOT="$(cd "$(dirname "$0")" && pwd)"
SRC="$ROOT/src/EnergyHub"
PROFILE="${PROFILE:-/c/Users/Administrator/AppData/Roaming/r2modmanPlus-local/DysonSphereProgram/profiles/chuangshi-lianji}"
# r2modman（Thunderstore 布局）把包放在「作者-模组名/」下，dll 在里面的 EnergyHub/ 子目录
PKG="$PROFILE/BepInEx/plugins/BOBO550-EnergyHub"
DEST="$PKG/EnergyHub"
DOTNET="${DOTNET:-/c/Program Files/dotnet/dotnet}"
PY="$(command -v python || command -v python3 || echo python)"

if [ ! -f "$ROOT/.buildref/Managed/Assembly-CSharp.dll" ]; then
  echo "!! 缺少 .buildref/Managed/Assembly-CSharp.dll"
  echo "   本机没装 DSP 时，从游戏或反编译工程拷一份进来："
  echo "     mkdir -p .buildref/Managed"
  echo "     cp <Managed目录>/*.dll .buildref/Managed/"
fi

echo "== 编译"
"$DOTNET" build "$SRC/EnergyHub.csproj" -c Release --nologo -v q

if [ "$1" = "--no-deploy" ]; then
  echo "== 跳过部署"
  exit 0
fi

echo "== 部署到 $DEST"
mkdir -p "$DEST"

cp "$SRC/bin/Release/DSPEnergyHub.dll" "$DEST/"
# 调试符号与旧的联机桥接一律清掉：插件目录只留一个 dll
rm -f "$DEST/DSPEnergyHub.pdb" "$DEST/DSPEnergyHub.Nebula.bin" "$DEST/DSPEnergyHub.Nebula.dll"

"$PY" "$ROOT/tools/stamp_manifest.py" "$PKG/manifest.json" "$(grep -oP 'Version = "\K[0-9.]+' "$SRC/C.cs" | head -1)"

ls -l "$DEST"
echo "== 完成。启动游戏读取日志: $PROFILE/BepInEx/LogOutput.log"
