"""玻璃棱镜塔 —— 造型预览（1.5.0 方案，第二版：6 棱、三段棱镜叠置）。

几何约定（与 C# 侧一一对应）：
  · 6 棱，顶点角 0/60/.../300°，-Z（270°）正好落在平面中心 → 4 个传送带接口贴在平面上
  · 每段玻璃都是「上扩下收」的双锥棱镜，段间用暖白金属环过渡 → 三层水晶叠起来
  · 8 条竖向棱骨改为「每段 6 条、贴着棱线外侧」，共 6 段
  · 内部一根 6 棱发光柱，从底座一直通到冠顶
  · 顶端保留现有能量球（球心 29.55、r 2.15），托碗嵌住球下半部

输出：hub_prism.png（正视 + 3/4 视角）
"""

import math
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from mpl_toolkits.mplot3d.art3d import Poly3DCollection

SEG = 6

# (y0, y1, r0, r1, kind)
SECTIONS = [
    (0.00, 0.50, 4.30, 4.15, "hull"),
    (0.50, 1.15, 4.15, 3.55, "dark"),
    (1.15, 3.90, 3.35, 3.90, "glass"),
    (3.90, 6.80, 3.90, 2.55, "glass"),
    (6.80, 7.30, 2.60, 3.05, "hull"),
    (7.30, 9.90, 3.05, 3.55, "glass"),
    (9.90, 13.40, 3.55, 2.15, "glass"),
    (13.40, 13.90, 2.20, 2.55, "hull"),
    (13.90, 16.20, 2.55, 2.85, "glass"),
    (16.20, 19.40, 2.85, 1.45, "glass"),
    (19.40, 19.95, 1.50, 1.95, "hull"),
    (19.95, 23.00, 1.95, 2.75, "glass"),
    (23.00, 26.60, 2.75, 0.90, "glass"),
    (26.60, 27.05, 0.90, 1.55, "hull"),
    (27.05, 28.35, 1.55, 2.05, "dark"),
]

STYLE = {
    "glass": ("#7f6fd0", 0.26),
    "hull": ("#efe3cf", 1.0),
    "dark": ("#b0a6c9", 1.0),
}
EDGE = {"glass": "#a99af0", "hull": "#b3a48a", "dark": "#8b81a8"}


def pt(y, r, a):
    return (r * math.cos(a), y, r * math.sin(a))


def prism_faces(y0, y1, r0, r1, seg=SEG):
    faces = []
    for i in range(seg):
        a0 = 2 * math.pi * i / seg
        a1 = 2 * math.pi * (i + 1) / seg
        faces.append([pt(y0, r0, a0), pt(y1, r1, a0), pt(y1, r1, a1), pt(y0, r0, a1)])
    return faces


def sphere_faces(cy, r, rings=22, seg=26):
    faces = []
    for i in range(rings):
        p0 = math.pi * i / rings
        p1 = math.pi * (i + 1) / rings
        for j in range(seg):
            t0 = 2 * math.pi * j / seg
            t1 = 2 * math.pi * (j + 1) / seg
            faces.append([(r * math.sin(pp) * math.cos(tt), cy + r * math.cos(pp),
                           r * math.sin(pp) * math.sin(tt))
                          for (pp, tt) in ((p0, t0), (p1, t0), (p1, t1), (p0, t1))])
    return faces


def conv(faces):
    return [[(v[0], v[2], v[1]) for v in f] for f in faces]


def draw(ax, elev, azim):
    buckets = {"glass": [], "hull": [], "dark": []}
    for (y0, y1, r0, r1, kind) in SECTIONS:
        buckets[kind] += prism_faces(y0, y1, r0, r1)

    # 内部发光光柱
    core = prism_faces(1.15, 26.50, 0.58, 0.30)

    # 棱骨：每段玻璃 6 条，贴棱线外侧
    struts = []
    for (y0, y1, r0, r1, kind) in SECTIONS:
        if kind != "glass":
            continue
        for i in range(SEG):
            a = 2 * math.pi * i / SEG
            struts.append(((r0 * 1.012 * math.cos(a), y0, r0 * 1.012 * math.sin(a)),
                           (r1 * 1.012 * math.cos(a), y1, r1 * 1.012 * math.sin(a))))

    # 腰环细箍（双锥最粗处再压一道）
    for (yy, rr) in ((3.90, 3.94), (9.90, 3.59), (16.20, 2.89), (23.00, 2.79)):
        buckets["hull"] += prism_faces(yy - 0.07, yy + 0.07, rr + 0.04, rr + 0.04)

    for kind in ("glass", "dark", "hull"):
        ax.add_collection3d(Poly3DCollection(conv(buckets[kind]),
                                             facecolor=STYLE[kind][0],
                                             edgecolor=EDGE[kind],
                                             linewidths=0.2,
                                             alpha=STYLE[kind][1]))

    ax.add_collection3d(Poly3DCollection(conv(core), facecolor="#c9a4ff",
                                         edgecolor="#a97cf0", linewidths=0.15, alpha=0.95))

    for (a, b) in struts:
        ax.plot([a[0], b[0]], [a[1], b[1]], [a[2], b[2]],
                color="#ffe9c4", linewidth=1.3, alpha=0.95)

    # 顶端能量球 + 赤道环
    ax.add_collection3d(Poly3DCollection(conv(sphere_faces(29.55, 2.15)),
                                         facecolor="#b98cff", edgecolor="#8a5cf0",
                                         linewidths=0.08, alpha=0.96))
    ax.add_collection3d(Poly3DCollection(conv(prism_faces(29.46, 29.64, 2.54, 2.54, seg=24)),
                                         facecolor="#efe3cf", edgecolor="#b3a48a",
                                         linewidths=0.15, alpha=1.0))

    # 底部接口套筒（-Z 侧，4 个）
    for i in range(4):
        cx = -3 + 2 * i
        x0, x1 = cx - 0.97, cx + 0.97
        z0, z1 = -4.85, -3.70
        f = [[(x0, 0, z0), (x1, 0, z0), (x1, 0.94, z0), (x0, 0.94, z0)],
             [(x0, 0, z1), (x1, 0, z1), (x1, 0.94, z1), (x0, 0.94, z1)],
             [(x0, 0, z0), (x0, 0, z1), (x0, 0.94, z1), (x0, 0.94, z0)],
             [(x1, 0, z0), (x1, 0, z1), (x1, 0.94, z1), (x1, 0.94, z0)],
             [(x0, 0.94, z0), (x1, 0.94, z0), (x1, 0.94, z1), (x0, 0.94, z1)]]
        ax.add_collection3d(Poly3DCollection(conv(f), facecolor="#b0a6c9",
                                             edgecolor="#8b81a8", linewidths=0.3, alpha=1.0))

    ax.set_xlim(-4.8, 4.8)
    ax.set_ylim(-4.8, 4.8)
    ax.set_zlim(0, 33)
    ax.set_box_aspect((9.6, 9.6, 33))
    ax.view_init(elev=elev, azim=azim)
    ax.set_axis_off()


fig = plt.figure(figsize=(15, 9.2), facecolor="#0e0e14")
for k, (elev, azim, title) in enumerate([(4, -90, "front  (-Z = belt ports)"),
                                         (14, -56, "3/4 view")]):
    ax = fig.add_subplot(1, 2, k + 1, projection="3d")
    ax.set_facecolor("#0e0e14")
    draw(ax, elev, azim)
    ax.set_title(title, color="#cfc7e6", fontsize=11)

plt.tight_layout()
plt.savefig(r"D:\戴森球计划反编译文件\hub_prism.png", dpi=120, facecolor="#0e0e14")
print("ok")
