"""底部传送带接口套筒 —— 几何预览。

复刻 HubMesh.AppendPortSockets 的数值，用来检查 4 个套筒与底座、与传送带的相对关系：
    · 套筒 x = -3/-1/+1/+3，外形半宽 0.97，高 0.94，向外凸 0.55（z 从 -(R-0.60) 到 -(R+0.55)）
    · 内孔半宽 0.83，孔底 0.14，孔顶 0.80
    · portPoses 在 z = -(R+0.05)、y = 0.2，正好落在孔内
输出：hub_ports.png
"""

import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from mpl_toolkits.mplot3d.art3d import Poly3DCollection

R = 4.30
OUT = 0.55
HW = 0.97
INW = 0.83
YTOP = 0.94
YINTOP = 0.80
YINBOT = 0.14
SPACING = 2.0

z_in = -(R - 0.60)
z_out = -(R + OUT)


def box(c, h):
    x0, x1 = c[0] - h[0], c[0] + h[0]
    y0, y1 = c[1] - h[1], c[1] + h[1]
    z0, z1 = c[2] - h[2], c[2] + h[2]
    v = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0),
         (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]
    f = [(0, 1, 2, 3), (4, 5, 6, 7), (0, 1, 5, 4),
         (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    return [[v[i] for i in q] for q in f]


def cyl(y0, y1, r, seg=56):
    a = np.linspace(0, 2 * np.pi, seg, endpoint=False)
    quads = []
    for i in range(seg):
        t0, t1 = a[i], a[(i + 1) % seg]
        quads.append([(r * np.cos(t0), y0, r * np.sin(t0)),
                      (r * np.cos(t1), y0, r * np.sin(t1)),
                      (r * np.cos(t1), y1, r * np.sin(t1)),
                      (r * np.cos(t0), y1, r * np.sin(t0))])
    return quads


def conv(faces):
    """matplotlib 的竖直轴是 Z：把模型的 y 换到第三位。"""
    return [[(v[0], v[2], v[1]) for v in f] for f in faces]


fig = plt.figure(figsize=(13, 7.2))
ax = fig.add_subplot(111, projection="3d")

ax.add_collection3d(Poly3DCollection(conv(cyl(0, 2.2, R)), facecolor="#c9c3d8",
                                     edgecolor="#8b839c", linewidths=0.3, alpha=0.95))

zc = (z_in + z_out) / 2
zh = abs(z_out - z_in) / 2
sideCx = (HW + INW) / 2
sideHx = (HW - INW) / 2

sock_faces, lamp_faces, belt_faces = [], [], []
for i in range(4):
    cx = -SPACING * 1.5 + SPACING * i
    sock_faces += box((cx - sideCx, (YINBOT + YTOP) / 2, zc), (sideHx, (YTOP - YINBOT) / 2, zh))
    sock_faces += box((cx + sideCx, (YINBOT + YTOP) / 2, zc), (sideHx, (YTOP - YINBOT) / 2, zh))
    sock_faces += box((cx, (YINTOP + YTOP) / 2, zc), (HW, (YTOP - YINTOP) / 2, zh))
    sock_faces += box((cx, YINBOT / 2, zc), (HW, YINBOT / 2, zh))

    lz, lhz = z_out - 0.03, 0.03
    sock_faces += box((cx - (HW + 0.03 + INW) / 2, (YINBOT + YTOP) / 2, lz),
                      ((HW + 0.03 - INW) / 2, (YTOP - YINBOT) / 2, lhz))
    sock_faces += box((cx + (HW + 0.03 + INW) / 2, (YINBOT + YTOP) / 2, lz),
                      ((HW + 0.03 - INW) / 2, (YTOP - YINBOT) / 2, lhz))
    sock_faces += box((cx, (YINTOP + YTOP + 0.03) / 2, lz),
                      (HW + 0.03, (YTOP + 0.03 - YINTOP) / 2, lhz))
    sock_faces += box((cx, (YINBOT - 0.03) / 2, lz),
                      (HW + 0.03, (YINBOT + 0.03) / 2, lhz))

    lamp_faces += box((cx, YINTOP - 0.035, z_out - 0.02), (INW - 0.06, 0.022, 0.012))

    belt_z0, belt_z1 = -(R + 0.05), -(R + 4.0)
    belt_faces.append([(cx - 1.0, 0.20, belt_z0), (cx + 1.0, 0.20, belt_z0),
                       (cx + 1.0, 0.20, belt_z1), (cx - 1.0, 0.20, belt_z1)])

ax.add_collection3d(Poly3DCollection(conv(sock_faces), facecolor="#a79ec0",
                                     edgecolor="#6f6788", linewidths=0.25, alpha=1.0))
ax.add_collection3d(Poly3DCollection(conv(lamp_faces), facecolor="#c08cff", edgecolor="none"))
ax.add_collection3d(Poly3DCollection(conv(belt_faces), facecolor="#3f3a4d",
                                     edgecolor="#2a2633", alpha=0.9))

ax.set_xlim(-5.4, 5.4)
ax.set_ylim(-8.6, 1.4)
ax.set_zlim(0, 3.0)
ax.set_box_aspect((10.8, 10.0, 3.0))
ax.set_xlabel("x")
ax.set_ylabel("z")
ax.set_zlabel("y")
ax.view_init(elev=22, azim=-62)
ax.set_title("energy-hub bottom belt ports (1.4.0)")
plt.tight_layout()
plt.savefig(r"D:\戴森球计划反编译文件\hub_ports.png", dpi=130)
print("ok")
