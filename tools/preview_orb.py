# -*- coding: utf-8 -*-
"""按 HubEffect.BuildOrbTexture / RebuildBolt 的同一套算法离线出图，
用于快速确认顶端能量球的观感（游戏内做的是加色混合，这里用 dst + src*alpha 模拟）。"""
import math
import random
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

N = 256


def build_orb_texture(n=N, seed=20261103):
    rnd = random.Random(seed)
    star_count = 96
    sx = [0.0] * star_count
    sy = [0.0] * star_count
    sbr = [0.0] * star_count
    for i in range(star_count):
        a = rnd.random() * math.pi * 2
        rr = math.sqrt(rnd.random()) * 0.84
        sx[i] = math.cos(a) * rr
        sy[i] = math.sin(a) * rr
        sbr[i] = 0.40 + rnd.random() * 0.60

    img = np.zeros((n, n, 4), dtype=np.float64)
    for y in range(n):
        v = (y + 0.5) / n * 2 - 1
        for x in range(n):
            u = (x + 0.5) / n * 2 - 1
            r = math.sqrt(u * u + v * v)
            cr = cg = cb = ca = 0.0
            if r < 0.94:
                rim = max(0.0, min(1.0, (r - 0.50) / 0.42)) ** 2.6
                core = max(0.0, min(1.0, 1 - r / 0.78)) ** 1.3
                cr = 0.12 + 0.22 * core + 0.30 * rim
                cg = 0.04 + 0.12 * core + 0.72 * rim
                cb = 0.36 + 0.40 * core + 0.52 * rim
                neb = 0.5 + 0.5 * math.sin(u * 9.3 + math.sin(v * 6.7) * 2.1)
                k = 0.80 + 0.20 * neb
                cr *= k
                cg *= k
                cb *= k
                for i in range(star_count):
                    dx = u - sx[i]
                    dy = v - sy[i]
                    d2 = dx * dx + dy * dy
                    if d2 > 0.0010:
                        continue
                    f = 1 - d2 / 0.0010
                    f = f * f * sbr[i]
                    cr += f
                    cg += f
                    cb += f
                ca = 1.0
            elif r < 1.30:
                g = 1 - (r - 0.94) / 0.36
                g = g * g * 0.72
                cr = g * 0.30
                cg = g * 0.56
                cb = g * 1.00
                ca = g
            img[y, x] = (min(cr, 1), min(cg, 1), min(cb, 1), max(0.0, min(ca, 1)))
    return img


def bolt_layer(size, seed, tint=(1.0, 0.94, 0.74)):
    """模拟 RebuildBolt：3~4 道软边折线（坐标单位 = 球半径）。"""
    ss = 4  # 超采样，抗锯齿
    W = size * ss
    rnd = random.Random(seed)
    layer = Image.new("RGB", (W, W), (0, 0, 0))
    dr = ImageDraw.Draw(layer)
    cx = cy = W / 2
    for _ in range(5):
        ang = rnd.random() * math.pi * 2
        dx, dy = math.cos(ang), math.sin(ang)
        px, py = -dy, dx
        p0 = (dx * 0.08, dy * 0.08)
        segs = 8
        ln = 0.52 + rnd.random() * 0.38
        pts = []
        for i in range(segs + 1):
            f = i / segs
            qx = p0[0] + dx * ln * f
            qy = p0[1] + dy * ln * f
            jit = 0.20 * (1 - f * 0.75)
            qx += px * (rnd.random() * 2 - 1) * jit
            qy += py * (rnd.random() * 2 - 1) * jit
            pts.append((cx + qx * cx, cy + qy * cy))
        for i in range(segs):
            f = i / segs
            w = max(1, int(0.042 * (1 - 0.55 * f) * (W / 2)))
            a = (0.30 * (1 - 0.5 * f))
            col = (int(255 * tint[0] * a * 0.55), int(255 * tint[1] * a * 0.42), int(255 * tint[2] * a))
            dr.line([pts[i], pts[i + 1]], fill=col, width=w * 2)
            # 中心亮线
            dr.line([pts[i], pts[i + 1]], fill=(255, 246, 210), width=max(1, w // 2))
    layer = layer.filter(ImageFilter.GaussianBlur(ss * 0.8))
    return layer.resize((size, size), Image.LANCZOS)


def render(size=360, seed=7, tint=(0.94, 0.92, 1.06), bg=(6, 8, 16)):
    orb = build_orb_texture(N)
    img = Image.new("RGBA", (N, N))
    a = np.clip(orb[:, :, 3:4], 0, 1)
    rgb = np.clip(orb[:, :, :3] * a, 0, 1)
    img = Image.fromarray((np.concatenate([rgb, a], axis=2) * 255).astype(np.uint8), "RGBA")
    img = img.resize((size, size), Image.LANCZOS)

    canvas = Image.new("RGB", (size, size), bg)
    # 加色混合：dst + src*alpha
    src = np.asarray(img).astype(np.float64)
    dst = np.asarray(canvas).astype(np.float64)
    t = np.array(tint).reshape(1, 1, 3)
    src_rgb = np.clip(src[:, :, :3] / 255.0 * t, 0, 1)
    alpha = src[:, :, 3:4] / 255.0
    out = np.clip(dst / 255.0 + src_rgb * alpha, 0, 1)

    bol = np.asarray(bolt_layer(size, seed)).astype(np.float64) / 255.0
    out = np.clip(out + bol, 0, 1)
    return Image.fromarray((out * 255).astype(np.uint8), "RGB")


if __name__ == "__main__":
    out = render()
    path = r"D:\戴森球计划反编译文件\hub_orb_preview.png"
    out.save(path)
    print("saved", path)
