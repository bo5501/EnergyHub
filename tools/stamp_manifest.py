"""按版本号重写发布包的 manifest.json（版本 + 短描述单一来源）。

用法: python tools/stamp_manifest.py <manifest.json> <version>
"""
import json
import sys

DESC = ("玻璃棱镜输电塔：星球间按需自动输电，内置超大蓄电器（20 级 100 GJ→1 PJ，封顶 50 TW），"
        "升级材料阶梯递增，8 个传送带进口，三级增产剂提速 ×2。")

DEPS = [
    "xiaoye97-BepInEx-5.4.17",
    "CommonAPI-CommonAPI-1.6.7",
    "xiaoye97-LDBTool-3.0.3",
    "CommonAPI-DSPModSave-1.2.2",
]


def main():
    path, ver = sys.argv[1], sys.argv[2]
    m = {
        "name": "EnergyHub",
        "version_number": ver,
        "website_url": "https://github.com/bo5501/EnergyHub",
        "description": DESC,
        "dependencies": DEPS,
    }
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(m, f, ensure_ascii=False, indent=2)
        f.write("\n")
    print("  .. manifest.json 已重写: %s / 描述 %d 字符" % (ver, len(DESC)))


if __name__ == "__main__":
    main()
