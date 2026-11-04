"""玻璃棱镜塔 —— 软件渲染预览。

自带正交投影 + 朗伯光照 + 菲涅尔边缘光 + 画家算法，比 mplot3d 干净得多，
用来在写 C# 几何之前确认造型和配色。

几何与将写入 HubMesh.BuildPrism 的参数一致：
  · 6 棱，顶点角 0/60/.../300°，-Z（270°）落在平面中心 → 4 个传送带接口贴平面
  · 三段「上扩下收」的双锥棱镜叠置，段间暖白金属环过渡
  · 每段玻璃 6 条竖向棱骨 + 双锥最粗处一道细箍
  · 内部一根 6 棱发光柱，从底座通到冠顶
  · 顶端保留现有能量球（球心 29.55、r 2.15）

输出：hub_prism.png
"""

import math
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.patches import Polygon
from matplotlib.collections import PatchCollection

SEG = 6

SECTIONS = [
    (0.00, 0.60, 4.30, 4.18, 0.0, "hull"),
    (0.60, 1.60, 4.18, 3.55, 0.0, "dark"),
    (1.60, 3.40, 3.30, 3.05, 0.0, "glass"),
    (3.40, 6.60, 3.05, 3.85, 0.0, "glass"),
    (6.60, 11.00, 3.85, 2.50, 0.0, "glass"),
    (11.00, 11.70, 2.55, 2.82, 0.0, "hull"),
    (11.70, 14.20, 2.82, 3.10, 15.0, "glass"),
    (14.20, 18.60, 3.10, 1.90, 15.0, "glass"),
    (18.60, 19.25, 1.95, 2.18, 15.0, "hull"),
    (19.25, 21.60, 2.18, 2.46, 0.0, "glass"),
    (21.60, 24.60, 2.46, 1.60, 0.0, "glass"),
    (24.60, 25.30, 1.64, 1.30, 0.0, "hull"),
    (25.30, 26.60, 1.30, 0.90, 0.0, "glass"),
    (26.60, 27.05, 0.90, 1.55, 0.0, "hull"),
    (27.05, 28.35, 1.55, 2.05, 0.0, "dark"),
]

BASE = {
    "glass": (0.50, 0.44, 0.80),
    "hull": (0.93, 0.88, 0.79),
    "dark": (0.68, 0.64, 0.79),
    "emis": (0.74, 0.55, 1.00),
    "rim": (0.62, 0.52, 1.00),
}
ALPHA = {"glass": 0.30, "hull": 1.0, "dark": 1.0, "emis": 1.0}

LIGHT = np.array([0.45, 0.75, 0.50])
LIGHT = LIGHT / np.linalg.norm(LIGHT)


def pt(y, r, a):
    return np.array([r * math.cos(a), y, r * math.sin(a)])


def prism(y0, y1, r0, r1, kind, rot=0.0, seg=SEG):
    out = []
    for i in range(seg):
        a0 = rot + 2 * math.pi * i / seg
        a1 = rot + 2 * math.pi * (i + 1) / seg
        out.append(([pt(y0, r0, a0), pt(y1, r1, a0), pt(y1, r1, a1), pt(y0, r0, a1)], kind))
    return out


def disc(y, r, kind, rot=0.0, seg=SEG, up=True):
    out = []
    for i in range(seg):
        a0 = rot + 2 * math.pi * i / seg
        a1 = rot + 2 * math.pi * (i + 1) / seg
        tri = [np.array([0.0, y, 0.0]), pt(y, r, a0), pt(y, r, a1)]
        out.append((tri if up else tri[::-1], kind))
    return out


def sphere(cy, r, kind, rings=18, seg=24):
    out = []
    for i in range(rings):
        p0 = math.pi * i / rings
        p1 = math.pi * (i + 1) / rings
        for j in range(seg):
            t0 = 2 * math.pi * j / seg
            t1 = 2 * math.pi * (j + 1) / seg
            q = []
            for (pp, tt) in ((p0, t0), (p1, t0), (p1, t1), (p0, t1)):
                q.append(np.array([r * math.sin(pp) * math.cos(tt),
                                   cy + r * math.cos(pp),
                                   r * math.sin(pp) * math.sin(tt)]))
            out.append((q, kind))
    return out


def box(cx, x0, x1, y0, y1, z0, z1, kind):
    f = [[(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0)],
         [(x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)],
         [(x0, y0, z0), (x0, y0, z1), (x0, y1, z1), (x0, y1, z0)],
         [(x1, y0, z0), (x1, y0, z1), (x1, y1, z1), (x1, y1, z0)],
         [(x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1)],
         [(x0, y1, z0), (x1, y1, z0), (x1, y1, z1), (x0, y1, z1)]]
    return [([np.array(v) for v in q], kind) for q in f]


def build():
    faces = []
    for (y0, y1, r0, r1, rot, kind) in SECTIONS:
        rot = math.radians(rot)
        faces += prism(y0, y1, r0, r1, kind, rot)
        faces += disc(y0, r0, kind, rot, up=False)

    # 内部光柱
    faces += prism(1.15, 26.50, 0.58, 0.30, "emis")
    faces += disc(26.50, 0.30, "emis")

    # 双锥最粗处的细箍
    for (yy, rr, rr_deg) in ((6.60, 3.89, 0.0), (14.20, 3.14, 15.0),
                             (21.60, 2.50, 0.0)):
        rt = math.radians(rr_deg)
        faces += prism(yy - 0.07, yy + 0.07, rr + 0.04, rr + 0.04, "hull", rt)
        faces += disc(yy + 0.07, rr + 0.04, "hull", rt)
        faces += disc(yy - 0.07, rr + 0.04, "hull", rt, up=False)

    # 棱骨（细四棱柱）
    for (y0, y1, r0, r1, rot, kind) in SECTIONS:
        if kind != "glass":
            continue
        for i in range(SEG):
            a = math.radians(rot) + 2 * math.pi * i / SEG
            faces += tube(pt(y0, r0 * 1.012, a), pt(y1, r1 * 1.012, a), 0.075, "hull")

    # 顶端球 + 赤道环
    faces += sphere(29.55, 2.15, "emis")
    faces += prism(29.46, 29.64, 2.56, 2.56, "hull", 0.0)
    faces += disc(29.64, 2.56, "hull", 0.0)
    faces += disc(29.46, 2.56, "hull", 0.0, up=False)

    # 底部接口套筒
    for i in range(4):
        cx = -3 + 2 * i
        faces += box(cx, cx - 0.97, cx + 0.97, 0.0, 0.94, -4.85, -3.70, "dark")
    return faces


def tube(a, b, r, kind, seg=4):
    d = b - a
    L = np.linalg.norm(d)
    if L < 1e-6:
        return []
    d = d / L
    up = np.array([0.0, 1.0, 0.0]) if abs(d[1]) < 0.95 else np.array([1.0, 0.0, 0.0])
    u = np.cross(d, up)
    u /= np.linalg.norm(u)
    v = np.cross(d, u)
    out = []
    ang = [2 * math.pi * k / seg + math.pi / seg for k in range(seg)]
    ring_a = [a + (u * math.cos(t) + v * math.sin(t)) * r for t in ang]
    ring_b = [b + (u * math.cos(t) + v * math.sin(t)) * r for t in ang]
    for k in range(seg):
        k2 = (k + 1) % seg
        out.append(([ring_a[k], ring_b[k], ring_b[k2], ring_a[k2]], kind))
    return out


def render(ax, elev_deg, azim_deg, faces, title):
    el = math.radians(elev_deg)
    az = math.radians(azim_deg)
    fwd = np.array([math.cos(el) * math.cos(az), math.sin(el), math.cos(el) * math.sin(az)])
    right = np.cross(np.array([0.0, 1.0, 0.0]), fwd)
    right /= np.linalg.norm(right)
    up = np.cross(fwd, right)

    items = []
    for (verts, kind) in faces:
        n = np.cross(verts[1] - verts[0], verts[2] - verts[0])
        nl = np.linalg.norm(n)
        if nl < 1e-9:
            continue
        n = n / nl
        to_cam = -fwd
        facing = float(np.dot(n, to_cam))
        depth = float(np.mean([np.dot(p, fwd) for p in verts]))
        if facing <= 0.0 and kind != "glass":
            continue
        shade = 0.42 + 0.58 * max(0.0, float(np.dot(n, LIGHT)))
        c = np.array(BASE[kind]) * shade
        if kind == "glass":
            fres = (1.0 - min(1.0, abs(facing))) ** 2.6
            c = c * (0.55 + 0.45 * shade) + np.array(BASE["rim"]) * fres * 0.85
            a = ALPHA["glass"] * (0.55 + 0.75 * fres)
        else:
            a = ALPHA[kind]
        xy = np.array([[float(np.dot(p, right)), float(np.dot(p, up))] for p in verts])
        items.append((depth, xy, np.clip(c, 0, 1.6), min(1.0, a), kind))

    items.sort(key=lambda t: -t[0])

    for (depth, xy, c, a, kind) in items:
        alpha = a if a >= 0.999 else max(0.05, a)
        c = np.clip(c, 0.0, 1.0)
        poly = Polygon(xy, closed=True, facecolor=(c[0], c[1], c[2], alpha),
                       edgecolor=(min(1, c[0] * 1.25), min(1, c[1] * 1.25), min(1, c[2] * 1.3), alpha * 0.9),
                       linewidth=0.35)
        ax.add_patch(poly)

    ax.set_xlim(-5.4, 5.4)
    ax.set_ylim(0, 33)
    ax.set_aspect("equal")
    ax.set_facecolor("#0c0c12")
    ax.set_title(title, color="#cfc7e6", fontsize=11)
    ax.set_xticks([])
    ax.set_yticks([])
    for s in ax.spines.values():
        s.set_visible(False)


faces = build()
fig, axes = plt.subplots(1, 2, figsize=(14, 9.5), facecolor="#0c0c12")
render(axes[0], 4, -90, faces, "front  (-Z = belt ports)")
render(axes[1], 14, -58, faces, "3/4 view")
plt.tight_layout()
plt.savefig(r"D:\戴森球计划反编译文件\hub_prism.png", dpi=125, facecolor="#0c0c12")
print("faces", len(faces), "ok")
