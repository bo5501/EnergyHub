"""1.3.4 预览：塔顶圆球段裁掉 + 自建激光发射头 + 竖直激光束。

几何参数与 HubMesh.cs 里 AppendLaserHead 完全一致，用来离线确认造型/比例，
免得又要在游戏里来回改。输出两张：整塔远景 + 塔顶特写。
"""
import struct
import numpy as np
from PIL import Image

BIN = r"D:/戴森球计划反编译文件/EnergyHubMod/src/EnergyHub/Assets/hub_mesh.bin"
OUT_FAR = r"D:/戴森球计划反编译文件/hub_laser_far.png"
OUT_TOP = r"D:/戴森球计划反编译文件/hub_laser_top.png"
OUT_WORM = r"D:/戴森球计划反编译文件/hub_wormhole.png"

TRIM_Y = 24.2
SEG_HULL = {0: np.float32([.98, .93, .86])}
TINT = {0: np.float32([.98, .93, .86]), 1: np.float32([.82, .79, .93]),
        2: np.float32([.26, .18, .42]), 3: np.float32([.55, .30, .22])}
ALB = {0: np.float32([.545, .553, .561]), 1: np.float32([.80, .79, .80]),
       2: np.float32([.22, .22, .22]), 3: np.float32([.22, .22, .22])}
EMI = {0: np.zeros(3, np.float32), 1: np.zeros(3, np.float32),
       2: np.float32([.753, .565, .992]), 3: np.float32([1., .363, .297])}
EMUL = {0: 0., 1: 0., 2: 1.8, 3: 2.0}


# ---------- 读原模型 ----------
def load():
    d = open(BIN, "rb").read()
    o = 4
    n = struct.unpack_from("<i", d, o)[0]
    o += 4
    subs = []
    for _ in range(n):
        mat, vc, ic = struct.unpack_from("<iii", d, o)
        o += 12
        vs = np.frombuffer(d, "<f4", vc * 3, o).reshape(-1, 3).astype(np.float64)
        o += 12 * vc
        ns = np.frombuffer(d, "<f4", vc * 3, o).reshape(-1, 3).astype(np.float64)
        o += 12 * vc
        idx = np.frombuffer(d, "<i4", ic, o).reshape(-1, 3).astype(np.int64)
        o += 4 * ic
        subs.append([mat, vs, ns, idx])
    return subs


# ---------- 自建几何（复刻 AppendLaserHead） ----------
def newpart():
    return [[], [], []]


def addtri(p, a, b, c, na, nb, nc, want):
    n = np.cross(b - a, c - a)
    if np.dot(n, want) < 0:
        a, b, na, nb = a, c, na, nc
        b, c, nb, nc = c, b, nc, nb
        # 上面两行会破坏顺序，重新写清楚
        a, b, c = a, c, b
        na, nb, nc = na, nc, nb
    p[0] += [a, b, c]
    p[1] += [na, nb, nc]
    i = len(p[0]) - 3
    p[2].append([i, i + 1, i + 2])


def tri(p, a, b, c, na, nb, nc, want):
    a = np.asarray(a, float); b = np.asarray(b, float); c = np.asarray(c, float)
    na = np.asarray(na, float); nb = np.asarray(nb, float); nc = np.asarray(nc, float)
    if np.dot(np.cross(b - a, c - a), want) < 0:
        b, c = c, b
        nb, nc = nc, nb
    i = len(p[0])
    p[0] += [a, b, c]
    p[1] += [na, nb, nc]
    p[2].append([i, i + 1, i + 2])


def quad(p, a, b, c, d, na, nb, nc, nd, want):
    a = np.asarray(a, float); b = np.asarray(b, float)
    c = np.asarray(c, float); d = np.asarray(d, float)
    na = np.asarray(na, float); nb = np.asarray(nb, float)
    nc = np.asarray(nc, float); nd = np.asarray(nd, float)
    if np.dot(np.cross(b - a, c - a), want) < 0:
        a, b, c, d = a, d, c, b
        na, nb, nc, nd = na, nd, nc, nb
    i = len(p[0])
    p[0] += [a, b, c, d]
    p[1] += [na, nb, nc, nd]
    p[2] += [[i, i + 1, i + 2], [i, i + 2, i + 3]]


def taper(p, off, y0, y1, r0, r1, seg, capb, capt):
    off = np.asarray(off, float)
    dr = (r1 - r0) / max(1e-4, y1 - y0)
    for i in range(seg):
        t0 = 2 * np.pi * i / seg
        t1 = 2 * np.pi * (i + 1) / seg
        c0, s0, c1, s1 = np.cos(t0), np.sin(t0), np.cos(t1), np.sin(t1)
        a = off + [c0 * r0, y0, s0 * r0]
        b = off + [c0 * r1, y1, s0 * r1]
        c = off + [c1 * r1, y1, s1 * r1]
        d = off + [c1 * r0, y0, s1 * r0]
        n0 = np.array([c0, -dr, s0]); n0 /= np.linalg.norm(n0)
        n1 = np.array([c1, -dr, s1]); n1 /= np.linalg.norm(n1)
        quad(p, a, b, c, d, n0, n0, n1, n1, (n0 + n1) / 2)
    if capb:
        cap(p, off, y0, r0, seg, -1)
    if capt:
        cap(p, off, y1, r1, seg, 1)


def cap(p, off, y, r, seg, sign):
    ctr = off + [0, y, 0]
    nrm = np.array([0, sign, 0.0])
    for i in range(seg):
        t0 = 2 * np.pi * i / seg
        t1 = 2 * np.pi * (i + 1) / seg
        a = off + [np.cos(t0) * r, y, np.sin(t0) * r]
        b = off + [np.cos(t1) * r, y, np.sin(t1) * r]
        tri(p, ctr, a, b, nrm, nrm, nrm, nrm)


def ring(p, y, ri, ro, seg):
    up = np.array([0, 1.0, 0])
    for i in range(seg):
        t0 = 2 * np.pi * i / seg
        t1 = 2 * np.pi * (i + 1) / seg
        a = [np.cos(t0) * ri, y, np.sin(t0) * ri]
        b = [np.cos(t0) * ro, y, np.sin(t0) * ro]
        c = [np.cos(t1) * ro, y, np.sin(t1) * ro]
        d = [np.cos(t1) * ri, y, np.sin(t1) * ri]
        quad(p, a, b, c, d, up, up, up, up, up)


def box(p, c, ax, ay, az):
    c = np.asarray(c, float)
    a0 = c - ax - ay - az; a1 = c + ax - ay - az
    a2 = c + ax + ay - az; a3 = c - ax + ay - az
    b0 = c - ax - ay + az; b1 = c + ax - ay + az
    b2 = c + ax + ay + az; b3 = c - ax + ay + az
    for q, n in (((a0, a1, a2, a3), -az), ((b0, b3, b2, b1), az),
                 ((a0, a3, b3, b0), -ax), ((a1, b1, b2, a2), ax),
                 ((a0, b0, b1, a1), -ay), ((a3, a2, b2, b3), ay)):
        quad(p, q[0], q[1], q[2], q[3], n, n, n, n, n)


def fin(p, ang, ri, ro, y0, y1, half):
    c, s = np.cos(ang), np.sin(ang)
    radial = np.array([c, 0, s])
    tang = np.array([-s, 0, c])
    ctr = radial * ((ri + ro) / 2) + np.array([0, (y0 + y1) / 2, 0])
    box(p, ctr, radial * ((ro - ri) / 2), np.array([0, (y1 - y0) / 2, 0]), tang * half)


def discring(p, y, ri, ro, th, seg):
    """水平环盘（上下两面 + 外侧壁），像炮管上的散热片。"""
    up = np.array([0, 1.0, 0]); dn = np.array([0, -1.0, 0])
    for i in range(seg):
        t0 = 2 * np.pi * i / seg
        t1 = 2 * np.pi * (i + 1) / seg
        c0, s0, c1, s1 = np.cos(t0), np.sin(t0), np.cos(t1), np.sin(t1)
        # 上表面
        quad(p, [c0 * ri, y + th / 2, s0 * ri], [c0 * ro, y + th / 2, s0 * ro],
             [c1 * ro, y + th / 2, s1 * ro], [c1 * ri, y + th / 2, s1 * ri], up, up, up, up, up)
        # 下表面
        quad(p, [c0 * ri, y - th / 2, s0 * ri], [c0 * ro, y - th / 2, s0 * ro],
             [c1 * ro, y - th / 2, s1 * ro], [c1 * ri, y - th / 2, s1 * ri], dn, dn, dn, dn, dn)
        # 外侧壁
        r0 = np.array([c0, 0, s0]); r1 = np.array([c1, 0, s1])
        quad(p, [c0 * ro, y - th / 2, s0 * ro], [c0 * ro, y + th / 2, s0 * ro],
             [c1 * ro, y + th / 2, s1 * ro], [c1 * ro, y - th / 2, s1 * ro],
             r0, r0, r1, r1, (r0 + r1) / 2)


def inner_bowl(p, yTop, rTop, yBot, rBot, seg):
    """朝上开口的碗状内腔（法线朝内朝上）。"""
    for i in range(seg):
        t0 = 2 * np.pi * i / seg
        t1 = 2 * np.pi * (i + 1) / seg
        c0, s0, c1, s1 = np.cos(t0), np.sin(t0), np.cos(t1), np.sin(t1)
        a = [c0 * rTop, yTop, s0 * rTop]
        b = [c0 * rBot, yBot, s0 * rBot]
        c = [c1 * rBot, yBot, s1 * rBot]
        d = [c1 * rTop, yTop, s1 * rTop]
        n0 = np.array([-c0, 0.9, -s0]); n0 /= np.linalg.norm(n0)
        n1 = np.array([-c1, 0.9, -s1]); n1 /= np.linalg.norm(n1)
        quad(p, a, b, c, d, n0, n0, n1, n1, (n0 + n1) / 2)



def ball(p, c, r, rings, seg):
    c = np.asarray(c, float)
    for i in range(rings):
        p0, p1 = np.pi * i / rings, np.pi * (i + 1) / rings
        y0, rr0 = np.cos(p0), np.sin(p0)
        y1, rr1 = np.cos(p1), np.sin(p1)
        for j in range(seg):
            t0, t1 = 2 * np.pi * j / seg, 2 * np.pi * (j + 1) / seg
            c0, s0, c1, s1 = np.cos(t0), np.sin(t0), np.cos(t1), np.sin(t1)
            d0 = np.array([c0 * rr0, y0, s0 * rr0])
            d1 = np.array([c0 * rr1, y1, s0 * rr1])
            d2 = np.array([c1 * rr1, y1, s1 * rr1])
            d3 = np.array([c1 * rr0, y0, s1 * rr0])
            quad(p, c + d0 * r, c + d1 * r, c + d2 * r, c + d3 * r,
                 d0, d1, d2, d3, (d0 + d1 + d2 + d3) / 4)


def build_head():
    """与 HubMesh.AppendOrbHead 参数一一对应。"""
    hull, dark, emis = newpart(), newpart(), newpart()
    taper(hull, (0, 0, 0), 23.55, 24.40, 0.98, 1.60, 8, True, False)      # 接驳裙
    taper(dark, (0, 0, 0), 24.40, 26.35, 1.60, 0.95, 12, False, False)    # 立柱
    discring(hull, 25.10, 0.95, 1.52, 0.10, 16)                           # 环箍
    discring(hull, 25.95, 0.95, 1.52, 0.10, 16)
    discring(hull, 26.55, 0.95, 2.38, 0.26, 24)                           # 托盘
    taper(dark, (0, 0, 0), 26.68, 27.70, 1.90, 2.40, 24, True, False)     # 托碗
    ball(emis, (0, 29.55, 0), 2.15, 24, 36)                               # 球（自发光）
    discring(hull, 29.55, 2.16, 2.54, 0.18, 24)                           # 赤道机械环
    for i in range(4):
        a = np.pi * 2 * i / 4 + np.pi * 0.25
        taper(hull, (np.cos(a) * 2.42, 0, np.sin(a) * 2.42), 27.58, 29.55, 0.085, 0.085, 6, False, False)
    return {0: hull, 1: dark, 2: emis}


def laser_mesh(power, r0, r1, h, seg, rings):
    v, n, t, cols = [], [], [], []
    for r in range(rings + 1):
        f = r / rings
        y = h * f
        rad = r0 + (r1 - r0) * f
        c = 0.62 * (1 - f) ** power
        for i in range(seg + 1):
            a = 2 * np.pi * i / seg
            v.append([np.cos(a) * rad, y, np.sin(a) * rad])
            n.append([0, 1.0, 0])
            cols.append(c)
    st = seg + 1
    for r in range(rings):
        for i in range(seg):
            b = r * st + i
            t += [[b, b + st, b + 1], [b + 1, b + st, b + st + 1]]
    return np.array(v), np.array(n), np.array(t), np.array(cols)


def ring_mesh(ri, ro, seg):
    mid = (ri + ro) / 2
    v, c = [], []
    for i in range(seg + 1):
        a = 2 * np.pi * i / seg
        cs, sn = np.cos(a), np.sin(a)
        v += [[cs * ri, 0, sn * ri], [cs * mid, 0, sn * mid], [cs * ro, 0, sn * ro]]
        c += [0.05, 1.0, 0.05]
    t = []
    for i in range(seg):
        b = i * 3
        t += [[b, b + 3, b + 1], [b + 1, b + 3, b + 4],
              [b + 1, b + 4, b + 2], [b + 2, b + 4, b + 5]]
    return np.array(v, float), np.array(t), np.array(c)


def euler_xyz(x, y, z):
    rx, ry, rz = np.deg2rad(x), np.deg2rad(y), np.deg2rad(z)
    X = np.array([[1, 0, 0], [0, np.cos(rx), -np.sin(rx)], [0, np.sin(rx), np.cos(rx)]])
    Y = np.array([[np.cos(ry), 0, np.sin(ry)], [0, 1, 0], [-np.sin(ry), 0, np.cos(ry)]])
    Z = np.array([[np.cos(rz), -np.sin(rz), 0], [np.sin(rz), np.cos(rz), 0], [0, 0, 1]])
    return Y @ Z @ X


ORB_Y = 29.55
ORB_R = 2.15
RINGS = [(2.95, 3.12), (3.62, 3.78), (4.40, 4.62), (5.15, 5.42)]
WTILT = [2.5, -3.2, 3.8, -2.2]
WSPIN = [0.16, -0.21, 0.13, -0.18]
WY = [0.0, 0.06, -0.05, 0.10]
CORE = np.array([0.32, 0.78, 1.00])
EDGE = np.array([0.58, 0.66, 1.00])


def build_rings(t=0.0, phase=0.0):
    V, T, C = [], [], []
    for i, (ri, ro) in enumerate(RINGS):
        v, tr, cc = ring_mesh(ri, ro, 96)
        a = t * WSPIN[i] + phase * 2 + i * 1.3
        R = euler_xyz(np.sin(a) * WTILT[i], a * 6.0, np.cos(a) * WTILT[i])
        vv = v @ R.T + np.array([0, ORB_Y + WY[i], 0])
        base = EDGE if i in (1, 3) else CORE
        ccc = cc[:, None] * base[None, :] * 0.85
        off = len(V)
        V += vv.tolist(); C += ccc.tolist(); T += (tr + off).tolist()
    return np.array(V), np.array(T, int), np.array(C)


def build_bolts(seed=7):
    """与 HubEffect.RebuildBolt 同参数：12 道，每道 5~7 段，两条交叉细带。"""
    rng = np.random.RandomState(seed)
    V, T, C = [], [], []
    for b in range(16):
        th = rng.rand() * 2 * np.pi
        ph = (rng.rand() - 0.5) * 1.4
        d = np.array([np.cos(th) * np.cos(ph), np.sin(ph), np.sin(th) * np.cos(ph)])
        d /= np.linalg.norm(d)
        perp = np.cross(d, [0, 1.0, 0])
        if np.linalg.norm(perp) < 0.1:
            perp = np.cross(d, [1.0, 0, 0])
        perp /= np.linalg.norm(perp)
        perp2 = np.cross(d, perp)
        perp2 /= np.linalg.norm(perp2)
        segs = 6 + rng.randint(4)
        ln = 2.2 + rng.rand() * 3.4
        r0 = ORB_R * 0.92
        prev = d * r0
        for i in range(1, segs + 1):
            f = i / segs
            f0 = (i - 1) / segs
            cur = d * (r0 + ln * f)
            amp = 0.30 * (1 - f) + 0.05
            cur = cur + perp * (rng.rand() * 2 - 1) * amp + perp2 * (rng.rand() * 2 - 1) * amp
            w0 = 0.060 + (0.012 - 0.060) * f0
            w1 = 0.060 + (0.012 - 0.060) * f
            def fade(ff):
                k = 1 - ff * 0.72
                return np.array([k, k * (0.90 - 0.18 * ff), 1.0]) * (1.05 - ff * 0.85)
            c0, c1 = fade(f0), fade(f)
            for wd in (perp, perp2):
                i0 = len(V)
                V += [prev + wd * w0, prev - wd * w0, cur - wd * w1, cur + wd * w1]
                C += [c0, c0, c1, c1]
                T += [[i0, i0 + 1, i0 + 2], [i0, i0 + 2, i0 + 3]]
            prev = cur
    V = np.array(V, float) + np.array([0, ORB_Y, 0])
    return V, np.array(T, int), np.array(C)


def glow_sprite(img, cx, cy, rad, col, strength=0.85):
    """在屏幕空间叠一层径向渐变（模拟球心 billboard 光晕）。"""
    H, W = img.shape[:2]
    y0, y1 = max(0, int(cy - rad)), min(H, int(cy + rad) + 1)
    x0, x1 = max(0, int(cx - rad)), min(W, int(cx + rad) + 1)
    if y0 >= y1 or x0 >= x1:
        return
    yy, xx = np.mgrid[y0:y1, x0:x1]
    r = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2) / rad
    k = np.clip(1 - r, 0, 1) ** 2 * 0.75 + np.clip(1 - r / 0.30, 0, 1) ** 2
    img[y0:y1, x0:x1] += np.clip(k, 0, 1)[..., None] * np.asarray(col, np.float32) * strength


# ---------- 光栅化 ----------
def s2l(c):
    c = np.asarray(c, np.float32)
    return np.where(c <= .04045, c / 12.92, ((c + .055) / 1.055) ** 2.4)


def l2s(c):
    c = np.clip(np.asarray(c, np.float32), 0, None)
    return np.where(c <= .0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - .055)


def render(tris_src, W, H, center, scale, add_tris=None, add_col=None, pitch_deg=-10):
    img = np.zeros((H, W, 3), np.float32)
    img[:] = np.float32([.06, .07, .09])
    zbuf = np.full((H, W), 1e9, np.float32)

    yaw = np.deg2rad(32)
    pitch = np.deg2rad(pitch_deg)
    cy, sy = np.cos(yaw), np.sin(yaw)
    cp, sp = np.cos(pitch), np.sin(pitch)
    R = np.array([[cy, 0, -sy], [0, 1, 0], [sy, 0, cy]]) @ np.array([[1, 0, 0], [0, cp, -sp], [0, sp, cp]])
    light = np.array([.45, .78, .44]); light /= np.linalg.norm(light)

    def proj(vs):
        P = (vs - center) @ R.T
        return W * .5 + P[:, 0] * scale, H * .5 - P[:, 1] * scale, P[:, 2]

    for mat, vs, ns, idx in tris_src:
        sx, sy_, z = proj(vs)
        N = ns @ R.T
        base = s2l(ALB[mat]) * s2l(TINT[mat]) * 1.3
        e = s2l(EMI[mat]) * EMUL[mat]
        for a, b, c in idx:
            x0, x1, x2 = sx[a], sx[b], sx[c]
            y0, y1, y2 = sy_[a], sy_[b], sy_[c]
            minx, maxx = int(max(0, min(x0, x1, x2))), int(min(W - 1, max(x0, x1, x2)))
            miny, maxy = int(max(0, min(y0, y1, y2))), int(min(H - 1, max(y0, y1, y2)))
            if minx > maxx or miny > maxy:
                continue
            area = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0)
            if abs(area) < 1e-9:
                continue
            nrm = N[a] + N[b] + N[c]
            ln = np.linalg.norm(nrm)
            if ln < 1e-6:
                continue
            nrm /= ln
            ndl = max(0.0, float(nrm @ light))
            col = base * (0.30 + 0.80 * ndl) + e
            rgbi = l2s(col / (1.0 + col * 0.55))
            X, Y = np.meshgrid(np.arange(minx, maxx + 1) + .5, np.arange(miny, maxy + 1) + .5)
            w0 = ((x1 - X) * (y2 - Y) - (x2 - X) * (y1 - Y)) / area
            w1 = ((x2 - X) * (y0 - Y) - (x0 - X) * (y2 - Y)) / area
            w2 = 1 - w0 - w1
            mask = (w0 >= 0) & (w1 >= 0) & (w2 >= 0)
            if not mask.any():
                continue
            zz = w0 * z[a] + w1 * z[b] + w2 * z[c]
            sub = zbuf[miny:maxy + 1, minx:maxx + 1]
            upd = mask & (zz < sub)
            if not upd.any():
                continue
            sub[upd] = zz[upd]
            img[miny:maxy + 1, minx:maxx + 1][upd] = rgbi

    # 激光：加色叠加（不做深度，粒子 shader 就是这样）
    if add_tris is not None:
        vs, idx, cols = add_tris
        sx, sy_, z = proj(vs)
        for a, b, c in idx:
            x0, x1, x2 = sx[a], sx[b], sx[c]
            y0, y1, y2 = sy_[a], sy_[b], sy_[c]
            minx, maxx = int(max(0, min(x0, x1, x2))), int(min(W - 1, max(x0, x1, x2)))
            miny, maxy = int(max(0, min(y0, y1, y2))), int(min(H - 1, max(y0, y1, y2)))
            if minx > maxx or miny > maxy:
                continue
            area = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0)
            if abs(area) < 1e-9:
                continue
            X, Y = np.meshgrid(np.arange(minx, maxx + 1) + .5, np.arange(miny, maxy + 1) + .5)
            w0 = ((x1 - X) * (y2 - Y) - (x2 - X) * (y1 - Y)) / area
            w1 = ((x2 - X) * (y0 - Y) - (x0 - X) * (y2 - Y)) / area
            w2 = 1 - w0 - w1
            mask = (w0 >= 0) & (w1 >= 0) & (w2 >= 0)
            if not mask.any():
                continue
            cc = (w0[..., None] * cols[a] + w1[..., None] * cols[b]
                  + w2[..., None] * cols[c])
            add = np.zeros((maxy - miny + 1, maxx - minx + 1, 3), np.float32)
            add[:] = np.asarray(add_col, np.float32) * cc * 2.0
            tgt = img[miny:maxy + 1, minx:maxx + 1]
            tgt += add * mask[..., None]

    return (np.clip(img, 0, 1) * 255).astype(np.uint8)


def main():
    subs = load()
    trimmed = []
    for mat, vs, ns, idx in subs:
        if mat == 3:
            continue
        keep = [t for t in idx if not (vs[t[0], 1] > TRIM_Y or vs[t[1], 1] > TRIM_Y or vs[t[2], 1] > TRIM_Y)]
        if not keep:
            continue
        trimmed.append([mat, vs, ns, np.array(keep)])

    head = build_head()
    for mat, (v, n, t) in head.items():
        if v:
            trimmed.append([mat, np.array(v), np.array(n), np.array(t)])

    # 发送状态：球心往上打 90 m 激光
    lay_v, lay_n, lay_t, lay_c = laser_mesh(1.7, 0.26, 1.15, 90, 20, 14)
    lay_v = lay_v + np.array([0, ORB_Y, 0])
    cor_v, cor_n, cor_t, cor_c = laser_mesh(0.35, 0.085, 0.055, 90, 10, 6)
    cor_v = cor_v + np.array([0, ORB_Y, 0])

    rv, rt, rc = build_rings(1.2)
    bv, bt, bc = build_bolts(7)

    lv = np.vstack([lay_v, cor_v, rv, bv])
    lt = np.vstack([lay_t, cor_t + len(lay_v), rt + len(lay_v) + len(cor_v),
                    bt + len(lay_v) + len(cor_v) + len(rv)])
    lc = np.vstack([np.tile((lay_c * 0.50)[:, None], (1, 3)),
                   np.tile((cor_c * 1.00)[:, None], (1, 3)), rc, bc])

    # 1) 整塔远景
    Image.fromarray(render(trimmed, 420, 900, np.array([0, 44., 0]), 8.5,
                           (lv, lt, lc), (0.72, 0.60, 1.00))).save(OUT_FAR)

    # 2) 塔顶特写（无光环，纯看模型）
    img = render(trimmed, 520, 640, np.array([0, 28.4, 0]), 74.0, pitch_deg=-8)
    Image.fromarray(img).save(OUT_TOP)

    # 3) 塔顶「英雄图」：模型 + 4 圈光环 + 12 道电弧 + 球心光晕
    img3 = render(trimmed, 720, 720, np.array([0, ORB_Y - 0.6, 0]), 62.0,
                  (lv, lt, lc), (0.72, 0.60, 1.00), pitch_deg=-24)
    # 球心屏幕坐标（与 render 同一套相机）
    yaw = np.deg2rad(32); pitch = np.deg2rad(-24)
    R = (np.array([[np.cos(yaw), 0, -np.sin(yaw)], [0, 1, 0], [np.sin(yaw), 0, np.cos(yaw)]])
         @ np.array([[1, 0, 0], [0, np.cos(pitch), -np.sin(pitch)], [0, np.sin(pitch), np.cos(pitch)]]))
    P = (np.array([0, ORB_Y, 0.0]) - np.array([0, ORB_Y - 0.6, 0])) @ R.T
    cx = 720 * .5 + P[0] * 62.0
    cy = 720 * .5 - P[1] * 62.0
    f = img3.astype(np.float32) / 255.0
    glow_sprite(f, cx, cy, ORB_R * 62.0 * 2.4, (0.55, 0.72, 1.0), 0.55)
    Image.fromarray((np.clip(f, 0, 1) * 255).astype(np.uint8)).save(OUT_WORM)
    print("written", OUT_FAR, OUT_TOP, OUT_WORM)


if __name__ == "__main__":
    main()
