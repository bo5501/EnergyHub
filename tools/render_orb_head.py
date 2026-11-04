"""塔顶「三球 + 多重能量环」造型预览。

几何与将写入代码的参数一致：
  · 中心核：自发光小球 r=0.70，球心 y=29.55（模型层，HubMesh）
  · 三颗能量球：r=0.95，绕中心轴公转，公转半径 1.78，相位 120°
    球面顶点色 = 金(#ffd88a) / 翠(#6be8c8) 阴阳分割，赤道亮、两极暗 → 有球感
  · 托座/托碗：27.05→28.35，六棱（保留）
  · 同心能量环：6 圈，半径 2.95 / 3.62 / 3.95 / 4.40 / 4.75 / 5.15

输出：hub_orb_head.png
"""

import math
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.patches import Polygon

BASE = {
    "hull": (0.93, 0.88, 0.79),
    "dark": (0.60, 0.56, 0.72),
    "emis": (0.80, 0.58, 1.00),
    "glass": (0.50, 0.44, 0.80),
    "gold": (1.00, 0.80, 0.44),
    "jade": (0.36, 0.95, 0.78),
    "ring": (0.34, 0.80, 1.00),
    "ring2": (0.62, 0.74, 1.00),
}
LIGHT = np.array([0.42, 0.72, 0.55])
LIGHT = LIGHT / np.linalg.norm(LIGHT)

ORB_Y = 29.80
ORB_R = 0.90
ORB_RING = 2.00
CORE_R = 0.86


def prism(y0, y1, r0, r1, kind, rot=0.0, seg=6):
    out = []
    for i in range(seg):
        a0 = rot + 2 * math.pi * i / seg
        a1 = rot + 2 * math.pi * (i + 1) / seg
        p0 = np.array([r0 * math.cos(a0), y0, r0 * math.sin(a0)])
        p1 = np.array([r0 * math.cos(a1), y0, r0 * math.sin(a1)])
        p2 = np.array([r1 * math.cos(a1), y1, r1 * math.sin(a1)])
        p3 = np.array([r1 * math.cos(a0), y1, r1 * math.sin(a0)])
        out.append(([p0, p1, p2, p3], kind))
    return out


def disc(y, r, kind, rot=0.0, seg=6, up=True):
    out = []
    c = np.array([0.0, y, 0.0])
    for i in range(seg):
        a0 = rot + 2 * math.pi * i / seg
        a1 = rot + 2 * math.pi * (i + 1) / seg
        p0 = np.array([r * math.cos(a0), y, r * math.sin(a0)])
        p1 = np.array([r * math.cos(a1), y, r * math.sin(a1)])
        out.append((([c, p0, p1] if up else [c, p1, p0]), kind))
    return out


def sphere(c, r, kind_fn, rings=22, seg=34):
    out = []
    c = np.asarray(c, dtype=float)
    for i in range(rings):
        p0 = math.pi * i / rings
        p1 = math.pi * (i + 1) / rings
        y0, rr0 = math.cos(p0), math.sin(p0)
        y1, rr1 = math.cos(p1), math.sin(p1)
        for j in range(seg):
            t0 = 2 * math.pi * j / seg
            t1 = 2 * math.pi * (j + 1) / seg
            a = c + r * np.array([rr0 * math.cos(t0), y0, rr0 * math.sin(t0)])
            b = c + r * np.array([rr0 * math.cos(t1), y0, rr0 * math.sin(t1)])
            cc = c + r * np.array([rr1 * math.cos(t1), y1, rr1 * math.sin(t1)])
            d = c + r * np.array([rr1 * math.cos(t0), y1, rr1 * math.sin(t0)])
            lon = (t0 + t1) * 0.5
            lat = (y0 + y1) * 0.5
            col, alpha = kind_fn(lon, lat)
            out.append(([a, b, cc, d], col, alpha))
    return out


def orb_color(lon, lat):
    """阴阳分割：sin(lon + 0.85*sin(lat)) 决定金/翠，赤道亮两极暗。"""
    s = math.sin(lon + 0.85 * math.sin(lat * 1.6))
    t = 0.5 + 0.5 * np.tanh(s * 2.4)          # 0=翠 1=金
    g = np.array(BASE["jade"])
    o = np.array(BASE["gold"])
    c = g * (1 - t) + o * t
    # 赤道最亮、两极压暗 → 侧看时轮廓暗、中间亮，读成球
    c = c * (0.46 + 0.54 * ((1.0 - abs(lat)) ** 0.75))
    # 阴阳两个「眼」：各压暗一小块
    for eye in (1.9, 1.9 + math.pi):
        d = math.atan2(math.sin(lon - eye), math.cos(lon - eye))
        dl = abs(lat - 0.15 * math.copysign(1, math.sin(eye)))
        k = math.exp(-(d * d) / 0.10 - (dl * dl) / 0.10)
        c = c * (1.0 - 0.55 * k)
    return c, 1.0


def ring(cy, r, width, kind, tilt_deg=0.0, phase=0.0, seg=96):
    """水平环，绕某条水平轴倾斜 tilt_deg 度。"""
    out = []
    tilt = math.radians(tilt_deg)
    # 环法线：从 +Y 绕水平方向 (cos phase, 0, sin phase) 倾斜
    axis = np.array([math.cos(phase), 0.0, math.sin(phase)])
    nrm = np.array([0.0, 1.0, 0.0]) * math.cos(tilt) + axis * math.sin(tilt)
    u = np.cross(nrm, np.array([0.0, 0.0, 1.0]))
    if np.linalg.norm(u) < 1e-6:
        u = np.cross(nrm, np.array([1.0, 0.0, 0.0]))
    u /= np.linalg.norm(u)
    v = np.cross(nrm, u)
    for i in range(seg):
        a0 = 2 * math.pi * i / seg
        a1 = 2 * math.pi * (i + 1) / seg
        p0 = u * math.cos(a0) * r + v * math.sin(a0) * r
        p1 = u * math.cos(a1) * r + v * math.sin(a1) * r
        rad0 = u * math.cos(a0) + v * math.sin(a0)
        rad1 = u * math.cos(a1) + v * math.sin(a1)
        w = width * 0.5
        q = [np.array([p0[0], cy + p0[1], p0[2]]) + rad0 * w,
             np.array([p1[0], cy + p1[1], p1[2]]) + rad1 * w,
             np.array([p1[0], cy + p1[1], p1[2]]) - rad1 * w,
             np.array([p0[0], cy + p0[1], p0[2]]) - rad0 * w]
        out.append((q, kind))
    return out


TOWER_SECTIONS = [
    (0.00, 0.60, 4.30, 4.18, 0.0, "hull"),
    (0.60, 1.60, 4.18, 3.55, 0.0, "dark"),
    (1.60, 3.40, 3.30, 3.05, 0.0, "glass"),
    (3.40, 6.60, 3.05, 3.85, 0.0, "glass"),
    (6.60, 11.00, 3.85, 2.50, 0.0, "glass"),
    (11.00, 11.70, 2.50, 2.82, 0.0, "hull"),
    (11.70, 14.20, 2.82, 3.10, 15.0, "glass"),
    (14.20, 18.60, 3.10, 1.90, 15.0, "glass"),
    (18.60, 19.25, 1.90, 2.18, 15.0, "hull"),
    (19.25, 21.60, 2.18, 2.46, 0.0, "glass"),
    (21.60, 24.60, 2.46, 1.60, 0.0, "glass"),
]


def build(t_phase=0.0, full=False):
    faces = []
    if full:
        for (y0, y1, r0, r1, rot, kind) in TOWER_SECTIONS:
            faces += prism(y0, y1, r0, r1, kind, math.radians(rot))
        faces += disc(0.0, 4.30, "hull", up=False)
    # 塔身上部（只画到 28.35，给比例参照）
    faces += prism(24.60, 25.30, 1.60, 1.30, "hull")
    faces += prism(25.30, 26.60, 1.30, 0.90, "glass")
    faces += prism(26.60, 27.05, 0.90, 1.55, "hull")
    faces += prism(27.05, 28.35, 1.55, 2.05, "dark")
    faces += disc(28.35, 2.05, "dark")
    faces += prism(28.18, 28.42, 0.24, 0.20, "dark")

    # 中心核
    faces += sphere((0, ORB_Y, 0), CORE_R,
                    lambda lo, la: (np.array(BASE["emis"]) * (0.72 + 0.28 * (1 - la * la)),
                                    1.0), 16, 26)

    # 三球（相位 120°）
    for i in range(3):
        a = t_phase + 2 * math.pi * i / 3
        c = (math.cos(a) * ORB_RING, ORB_Y + 0.10 * math.sin(t_phase * 2 + i), math.sin(a) * ORB_RING)
        faces += sphere(c, ORB_R, orb_color, 22, 34)

    # 同心环 6 圈
    rings = [
        (3.15, 0.16, "ring", 2.5, 0.0),
        (3.78, 0.15, "ring2", -3.2, 0.9),
        (4.12, 0.055, "ring", 3.6, 1.7),
        (4.58, 0.20, "ring", 3.8, 2.4),
        (4.95, 0.050, "ring2", -2.2, 3.1),
        (5.35, 0.26, "ring2", -2.2, 3.9),
    ]
    for (r, w, k, tilt, ph) in rings:
        faces += ring(ORB_Y, r, w, k, tilt, ph)
    return faces


def render(ax, elev_deg, azim_deg, faces, title, ylim=None, center=(0.0, 0.0, 0.0)):
    el = math.radians(elev_deg)
    az = math.radians(azim_deg)
    # 摄像机在 center + R*(...) 处看向中心，fwd 是「视线方向」
    fwd = -np.array([math.cos(el) * math.cos(az), math.sin(el), math.cos(el) * math.sin(az)])
    right = np.cross(np.array([0.0, 1.0, 0.0]), fwd)
    right /= np.linalg.norm(right)
    up = np.cross(fwd, right)

    items = []
    for f in faces:
        verts = f[0]
        kind = f[1]
        if isinstance(kind, str):
            col = np.array(BASE[kind])
            alpha = 0.30 if kind == "glass" else 1.0
        else:
            col = np.asarray(kind, dtype=float)
            alpha = f[2] if len(f) > 2 else 1.0
        n = np.cross(verts[1] - verts[0], verts[2] - verts[0])
        nl = np.linalg.norm(n)
        if nl < 1e-9:
            continue
        n = n / nl
        facing = float(np.dot(n, -fwd))
        depth = float(np.mean([np.dot(p - np.asarray(center), fwd) for p in verts]))
        if facing <= 0.0 and not (isinstance(kind, str) and kind in ("glass", "ring", "ring2")):
            continue
        if isinstance(kind, str):
            shade = 0.45 + 0.55 * max(0.0, float(np.dot(n, LIGHT)))
            c = col * shade
            if kind == "glass":
                fres = (1.0 - min(1.0, abs(facing))) ** 2.6
                c = col * (0.55 + 0.45 * shade) + np.array([0.62, 0.52, 1.0]) * fres * 0.85
                alpha = 0.30 * (0.55 + 0.75 * fres)
        else:
            c = col
        xy = np.array([[float(np.dot(p - np.asarray(center), right)),
                        float(np.dot(p - np.asarray(center), up))] for p in verts])
        items.append((depth, xy, np.clip(c, 0, 1), alpha))

    items.sort(key=lambda t: -t[0])
    for (depth, xy, c, a) in items:
        ax.add_patch(Polygon(xy, closed=True, facecolor=(c[0], c[1], c[2], min(1.0, a)),
                             edgecolor="none", linewidth=0))

    ax.set_xlim(-6.4, 6.4)
    ax.set_ylim(*(ylim if ylim else (26.4, 36.0)))
    ax.set_aspect("equal")
    ax.set_facecolor("#080a14")
    ax.set_title(title, color="#cfc7e6", fontsize=10)
    ax.set_xticks([])
    ax.set_yticks([])
    for s in ax.spines.values():
        s.set_visible(False)


faces = build(t_phase=0.7)
fig, axes = plt.subplots(1, 3, figsize=(18.5, 7.6), facecolor="#080a14")
render(axes[0], 6, -90, faces, "front", ylim=(-3.6, 6.4), center=(0, 29.8, 0))
render(axes[1], 20, -56, faces, "3/4 view", ylim=(-3.6, 6.4), center=(0, 29.8, 0))
render(axes[2], 72, -90, faces, "player view (looking down)", ylim=(-6.2, 6.2), center=(0, 29.8, 0))
plt.tight_layout()
plt.savefig(r"D:\戴森球计划反编译文件\hub_orb_head.png", dpi=130, facecolor="#080a14")
print("ok")
