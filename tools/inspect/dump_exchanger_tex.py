"""导出 energy-exchanger 的 4 张贴图到 PNG，并统计关键 texel 亮度。
用于标定 mod 里的常量 UV 与自发光强度。
"""
import os
import UnityPy
import numpy as np

P = r"D:/Program Files (x86)/Steam/steamapps/common/Dyson Sphere Program/DSPGAME_Data/resources.assets"
OUT = r"D:/戴森球计划反编译文件/hub_tex"
os.makedirs(OUT, exist_ok=True)

env = UnityPy.load(P)
want = {"energy-exchanger-a", "energy-exchanger-e", "energy-exchanger-s", "energy-exchanger-n"}

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
    try:
        img = t.image
    except Exception as e:
        print("img fail", t.m_Name, e)
        continue
    img.save(os.path.join(OUT, t.m_Name + ".png"))
    a = np.asarray(img.convert("RGB")).astype(np.float32) / 255.0
    h, w = a.shape[:2]
    print("=" * 70)
    print(f"{t.m_Name}  {w}x{h}  format={t.m_TextureFormat}")
    print("  mean rgb", a.reshape(-1, 3).mean(0).round(3), " max", a.reshape(-1, 3).max(0).round(3))
    lum = a.mean(2)
    print("  lum p50/p90/p99/max", round(float(np.percentile(lum, 50)), 3),
          round(float(np.percentile(lum, 90)), 3), round(float(np.percentile(lum, 99)), 3),
          round(float(lum.max()), 3))
    # UV (0.5,0.5) 处的 texel
    for u, v in ((0.5, 0.5), (0.5, 0.25), (0.25, 0.5), (0.75, 0.75), (0.5, 0.75), (0.5, 0.9)):
        px = min(w - 1, int(u * w))
        py = min(h - 1, int((1 - v) * h))
        print(f"   uv({u},{v}) -> rgb", a[py, px].round(3))
    found[t.m_Name] = (img, a)

print("\nfound:", sorted(found))
