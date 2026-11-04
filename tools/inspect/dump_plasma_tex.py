"""导出近程电浆塔（turret-plasma-ground）的贴图，并统计 alpha / 平坦色块。

用途：给 HubMesh 换成电浆塔配色时，挑「原版贴图上的平坦色块」当常量 UV。
"""
import os
import UnityPy
import numpy as np
from PIL import Image

P = r"D:/Program Files (x86)/Steam/steamapps/common/Dyson Sphere Program/DSPGAME_Data/resources.assets"
OUT = r"D:/戴森球计划反编译文件/hub_tex"
os.makedirs(OUT, exist_ok=True)

BASE = "turret-plasma-ground"
want = {BASE + suffix for suffix in ("-a", "-e", "-s", "-n", "-sc")}

env = UnityPy.load(P)
found = {}
for obj in env.objects:
    if obj.type.name != "Texture2D":
        continue
    try:
        t = obj.read()
    except Exception:
        continue
    if t.m_Name not in want:
        continue
    img = t.image
    img.save(os.path.join(OUT, t.m_Name + ".png"))
    arr = np.asarray(img.convert("RGBA")).astype(np.float32) / 255.0
    rgb = arr[..., :3]
    print("=" * 70)
    print(f"{t.m_Name}  {img.size[0]}x{img.size[1]}  {t.m_TextureFormat}")
    print("   rgb mean", rgb.reshape(-1, 3).mean(0).round(3), " max", rgb.reshape(-1, 3).max(0).round(3))
    print("   alpha p5/50/95", np.percentile(arr[..., 3], [5, 50, 95]).round(3))
    found[t.m_Name] = arr

if len(found) == 5:
    a = found[BASE + "-a"]
    e = found[BASE + "-e"]
    h, w = a.shape[:2]
    cands = []
    for py in range(6, h - 6, 4):
        for px in range(6, w - 6, 4):
            blk_a = a[py - 4:py + 4, px - 4:px + 4, :3].reshape(-1, 3)
            blk_e = e[py - 4:py + 4, px - 4:px + 4, :3].reshape(-1, 3)
            if blk_a.std(0).max() > 0.015 or blk_e.std(0).max() > 0.015:
                continue
            u = (px + 0.5) / w
            v = 1.0 - (py + 0.5) / h
            cands.append((u, v, a[py, px].copy(), e[py, px].copy()))
    print(f"\n平坦色块候选 {len(cands)} 个（uv / 反照率 / alpha / 自发光）")
    A = np.array([c[2][:3] for c in cands])
    E = np.array([c[3][:3] for c in cands])
    la = A.mean(1)
    le = E.mean(1)

    def show(tag, cond, lim=4):
        idx = [i for i in range(len(cands)) if cond(i)]
        print("-" * 66)
        print(tag, len(idx))
        for i in idx[:lim]:
            u, v, ca, ce = cands[i]
            print(f"  ({u:.4f}f, {v:.4f}f) alb={tuple(round(float(x), 3) for x in ca[:3])} "
                  f"a={ca[3]:.2f} em={tuple(round(float(x), 3) for x in ce[:3])}")

    show("亮白装甲 0.72-0.95", lambda i: 0.72 < la[i] < 0.95 and le[i] < 0.01)
    show("中灰 0.42-0.62", lambda i: 0.42 < la[i] < 0.62 and le[i] < 0.01)
    show("深灰 0.18-0.32", lambda i: 0.18 < la[i] < 0.32 and le[i] < 0.01)
    show("紫光 em 0.3-0.8 且 b>r", lambda i: 0.3 < le[i] < 0.8 and E[i][2] > E[i][0] + 0.05)
    show("强紫光 em>0.8", lambda i: le[i] > 0.8 and E[i][2] > E[i][0])
