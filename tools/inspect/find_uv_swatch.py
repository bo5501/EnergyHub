"""在 energy-exchanger 的 a/e/s/n 贴图里寻找可用作「常量 UV 色块」的位置。
用途：材质参数全部保持原版不动，只靠每个 submesh 的 UV 取不同色块，
      从而 100% 复用原版曝光表现，不再瞎猜 shader 的属性语义。
"""
import os
import pickle
import numpy as np
from PIL import Image

T = r"D:/戴森球计划反编译文件/hub_tex"


def load(name, mode="RGB"):
    im = Image.open(os.path.join(T, name + ".png"))
    return np.asarray(im.convert(mode)).astype(np.float32) / 255.0


a = load("energy-exchanger-a")
e = load("energy-exchanger-e")
s = load("energy-exchanger-s")
n = load("energy-exchanger-n")
h, w = a.shape[:2]


def at(img, py, px):
    ih, iw = img.shape[:2]
    y = min(ih - 1, int(py * ih / h))
    x = min(iw - 1, int(px * iw / w))
    return img[y, x]


cands = []
step = 4
R = 4
for py in range(R + 2, h - R - 2, step):
    for px in range(R + 2, w - R - 2, step):
        blk_a = a[py - R:py + R, px - R:px + R].reshape(-1, 3)
        blk_e = e[py - R:py + R, px - R:px + R].reshape(-1, 3)
        if blk_a.std(0).max() > 0.012 or blk_e.std(0).max() > 0.012:
            continue
        u = (px + 0.5) / w
        v = 1.0 - (py + 0.5) / h
        cands.append((u, v,
                      a[py, px].copy(), e[py, px].copy(),
                      at(s, py, px).copy(), at(n, py, px).copy()))

print("候选数:", len(cands))
pickle.dump(cands, open(os.path.join(T, "cands.pkl"), "wb"))

A = np.array([c[2] for c in cands])
E = np.array([c[3] for c in cands])
la = A.mean(1)
le = E.mean(1)


def show(tag, idx, lim=4):
    print("-" * 74)
    print(tag, "命中", len(idx))
    for i in idx[:lim]:
        u, v, ca, ce, cs, cn = cands[i]
        print(f"  uv=({u:.4f},{v:.4f}) a={tuple(ca.round(2))} e={tuple(ce.round(3))} "
              f"s=({cs[0]:.2f},{cs[1]:.2f},{cs[2]:.2f}) n=({cn[0]:.2f},{cn[1]:.2f},{cn[2]:.2f})")


show("亮灰 0.72-0.95 / e<0.01", [i for i in range(len(cands)) if 0.72 < la[i] < 0.95 and le[i] < 0.01])
show("中灰 0.45-0.65 / e<0.01", [i for i in range(len(cands)) if 0.45 < la[i] < 0.65 and le[i] < 0.01])
show("深灰 0.20-0.35 / e<0.01", [i for i in range(len(cands)) if 0.20 < la[i] < 0.35 and le[i] < 0.01])
show("近黑 <0.15 / e<0.01", [i for i in range(len(cands)) if la[i] < 0.15 and le[i] < 0.01])
show("微光青 e 0.06-0.3", [i for i in range(len(cands)) if 0.06 < le[i] < 0.3 and E[i][2] >= E[i][0]])
show("中光青 e 0.3-0.6", [i for i in range(len(cands)) if 0.3 < le[i] < 0.6 and E[i][2] >= E[i][0]])
show("强光青 e>0.6", [i for i in range(len(cands)) if le[i] > 0.6 and E[i][2] >= E[i][0]])
show("暖色 a r>b+0.06", [i for i in range(len(cands)) if le[i] < 0.01 and A[i][0] > A[i][2] + 0.06])
