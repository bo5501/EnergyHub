"""生成 Thunderstore 用的 icon.png（256x256）。

流程：
  1) 复用 render_orb_head 的几何（三段棱镜 + 三颗公转球 + 6 圈同心环）；
  2) 用本文件的 render() 做正交投影 + 画家算法，输出黑底 PNG（黑 = 之后 screen 混合时的透明）；
  3) PIL 合成：深蓝径向渐变背景 + 塔后柔光 + screen 叠加塔体 + 细描边；
  4) LANCZOS 缩到 256x256。

输出：EnergyHubMod/dist/EnergyHub/icon.png
"""

import math
import os
import sys

import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.patches import Polygon

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import render_orb_head as R

from PIL import Image, ImageChops, ImageDraw, ImageFilter


def render(ax, elev_deg, azim_deg, faces, xlim, ylim, center):
    el = math.radians(elev_deg)
    az = math.radians(azim_deg)
    fwd = -np.array([math.cos(el) * math.cos(az), math.sin(el), math.cos(el) * math.sin(az)])
    right = np.cross(np.array([0.0, 1.0, 0.0]), fwd)
    right /= np.linalg.norm(right)
    up = np.cross(fwd, right)
    c0 = np.asarray(center, dtype=float)

    items = []
    for f in faces:
        verts = f[0]
        kind = f[1]
        if isinstance(kind, str):
            col = np.array(R.BASE[kind])
            alpha = 0.52 if kind == "glass" else 1.0
        else:
            col = np.asarray(kind, dtype=float)
            alpha = f[2] if len(f) > 2 else 1.0
        n = np.cross(verts[1] - verts[0], verts[2] - verts[0])
        nl = np.linalg.norm(n)
        if nl < 1e-9:
            continue
        n = n / nl
        facing = float(np.dot(n, -fwd))
        depth = float(np.mean([np.dot(p - c0, fwd) for p in verts]))
        if facing <= 0.0 and not (isinstance(kind, str) and kind in ("glass", "ring", "ring2")):
            continue
        if isinstance(kind, str):
            shade = 0.45 + 0.55 * max(0.0, float(np.dot(n, R.LIGHT)))
            c = col * shade
            if kind == "glass":
                fres = (1.0 - min(1.0, abs(facing))) ** 2.6
                c = col * (0.55 + 0.45 * shade) + np.array([0.62, 0.52, 1.0]) * fres * 0.95
                alpha = 0.52 * (0.55 + 0.75 * fres)
        else:
            # 顶点色本身偏淡（加色混合会把颜色洗白），图标里提一点饱和度
            c = np.clip((np.asarray(col, dtype=float) - 0.5) * 1.30 + 0.54, 0, 1)
        xy = np.array([[float(np.dot(p - c0, right)), float(np.dot(p - c0, up))] for p in verts])
        items.append((depth, xy, np.clip(c, 0, 1), alpha))

    items.sort(key=lambda t: -t[0])
    for (depth, xy, c, a) in items:
        ax.add_patch(Polygon(xy, closed=True, facecolor=(c[0], c[1], c[2], min(1.0, a)),
                             edgecolor="none", linewidth=0))

    ax.set_xlim(*xlim)
    ax.set_ylim(*ylim)
    ax.set_aspect("equal")
    ax.set_facecolor("#000000")
    ax.set_xticks([])
    ax.set_yticks([])
    for s in ax.spines.values():
        s.set_visible(False)


def radial_bg(size, inner, outer, cx=0.5, cy=0.42):
    """径向渐变（RGB），中心 (cx,cy) 归一化。"""
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    u = (xx / (size - 1) - cx)
    v = (yy / (size - 1) - cy)
    r = np.sqrt(u * u + v * v) / 0.72
    r = np.clip(r, 0, 1) ** 0.85
    img = np.zeros((size, size, 3), dtype=np.float32)
    for i in range(3):
        img[:, :, i] = inner[i] * (1 - r) + outer[i] * r
    return Image.fromarray((np.clip(img, 0, 255)).astype(np.uint8), "RGB")


def main():
    out_dir = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                           "dist", "EnergyHub")
    os.makedirs(out_dir, exist_ok=True)
    tmp = os.path.join(out_dir, "_icon_raw.png")

    faces = R.build(t_phase=0.55, full=True)
    fig = plt.figure(figsize=(9, 9), dpi=120, facecolor="#000000")
    ax = fig.add_axes([0, 0, 1, 1])
    render(ax, 13, -60, faces, xlim=(-6.4, 6.4), ylim=(-6.4, 6.4), center=(0, 25.6, 0))
    fig.savefig(tmp, dpi=120, facecolor="#000000")
    plt.close(fig)

    S = 960
    bg = radial_bg(S, (26, 34, 66), (5, 7, 16), 0.5, 0.46)

    # 塔后的柔光：把渲染图的高光通道模糊后当泛光
    raw = Image.open(tmp).convert("RGB").resize((S, S), Image.LANCZOS)
    raw = Image.eval(raw, lambda v: min(255, int(v * 1.22 + 6)))
    glow = raw.filter(ImageFilter.GaussianBlur(22))
    glow = Image.eval(glow, lambda v: min(255, int(v * 0.70)))

    base = ImageChops.screen(bg, glow)
    base = ImageChops.screen(base, raw)
    # 高光泛光：把接近纯白的部分（球心、环的亮边）单独抽出来再糊一层
    hi = ImageChops.subtract(raw, Image.new("RGB", (S, S), (168, 168, 168)))
    hi = hi.filter(ImageFilter.GaussianBlur(11))
    base = ImageChops.screen(base, hi)

    # 淡淡的取景框
    d = ImageDraw.Draw(base, "RGBA")
    pad = 10
    d.rectangle([pad, pad, S - pad, S - pad], outline=(120, 170, 255, 34), width=2)
    d.rectangle([pad + 4, pad + 4, S - pad - 4, S - pad - 4], outline=(90, 130, 220, 16), width=1)

    icon = base.resize((256, 256), Image.LANCZOS)
    icon_path = os.path.join(out_dir, "icon.png")
    icon.save(icon_path, "PNG", optimize=True)
    os.remove(tmp)
    print("icon:", icon_path, icon.size)


if __name__ == "__main__":
    main()
