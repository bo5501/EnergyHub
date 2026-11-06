"""按版本号重写发布包的 manifest.json（版本 + 短描述单一来源）。

用法: python tools/stamp_manifest.py <manifest.json> <version>
环境变量 TS_PACKAGE_NAME 可覆盖包名（默认 EnergyHub，即原始 listing）。
"""
import json
import os
import re
import sys

# Thunderstore 包名只能用 [A-Za-z0-9_]（8041 个包实测无一例外）
# 默认用原始包名 EnergyHub：版主解锁原 listing 后，只有这个包名才会更新到原条目，
# 换成别的名字会被当成新包（Team 下会多出一条 listing）。
NAME = os.environ.get("TS_PACKAGE_NAME") or "EnergyHub"
assert re.fullmatch(r"[A-Za-z0-9_]+", NAME), "非法包名: %r" % NAME

DESC = ("星际能量枢纽塔：星球间按需自动输电，内置 20 级蓄电器（100 GJ→1 PJ，封顶 50 TW），"
        "三级增产剂提速 ×2。")

DEPS = [
    "xiaoye97-BepInEx-5.4.17",
    "CommonAPI-CommonAPI-1.6.7",
    "xiaoye97-LDBTool-3.0.3",
    "CommonAPI-DSPModSave-1.2.2",
]


def main():
    path, ver = sys.argv[1], sys.argv[2]
    m = {
        "name": NAME,
        "version_number": ver,
        "website_url": "https://github.com/bo5501/EnergyHub",
        "description": DESC,
        "dependencies": DEPS,
    }
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(m, f, ensure_ascii=False, indent=2)
        f.write("\n")
    print("  .. manifest.json 已重写: %s %s / 描述 %d 字符" % (NAME, ver, len(DESC)))


if __name__ == "__main__":
    main()
