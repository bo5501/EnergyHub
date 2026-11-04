"""用原版贴图色块的实测值，离线渲染一版预览，检查配色是否合理（不是纯白糊掉）。
   只做最粗的 Lambert + 自发光 + 简易色调映射，足够判断明暗关系。
"""
import struct
import numpy as np
from PIL import Image, ImageDraw

BIN = r"D:/戴森球计划反编译文件/EnergyHubMod/src/EnergyHub/Assets/hub_mesh.bin"
OUT = r"D:/戴森球计划反编译文件/hub_preview_plasma_c.png"

d = open(BIN, "rb").read()
o = 4
n = struct.unpack_from("<i", d, o)[0]
o += 4
subs = []
for i in range(n):
    mat, vc, ic = struct.unpack_from("<iii", d, o)
    o += 12
    vs = np.frombuffer(d, "<f4", vc * 3, o).reshape(-1, 3).astype(np.float64)
    o += 12 * vc
    ns = np.frombuffer(d, "<f4", vc * 3, o).reshape(-1, 3).astype(np.float64)
    o += 12 * vc
    idx = np.frombuffer(d, "<i4", ic, o).reshape(-1, 3).astype(np.int64)
    o += 4 * ic
    subs.append((mat, vs, ns, idx))

# 实测色块（sRGB 反照率 / 自发光 sRGB，自发光乘 _EmissionMultiplier=10 后为 HDR）
# 近程电浆塔配色：装甲 #E0D6EE 系（_TintColor 暖白 + _EmColor 紫/橙）
alb = {0: np.float32([.545, .553, .561]), 1: np.float32([.80, .79, .80]),
       2: np.float32([.22, .22, .22]), 3: np.float32([.22, .22, .22])}
tint = {0: np.float32([.98, .93, .86]), 1: np.float32([.82, .79, .93]),
        2: np.float32([.26, .18, .42]), 3: np.float32([.55, .30, .22])}
emi = {0: np.zeros(3, np.float32), 1: np.zeros(3, np.float32),
       2: np.float32([.753, .565, .992]), 3: np.float32([1.0, .363, .297])}
emul = {0: 0.0, 1: 0.0, 2: 1.8, 3: 2.0}
SKIP = {3}                     # 3 = 玻璃/辊道筒；置空集合可看带筒版本


def s2l(c):
    c = np.asarray(c, np.float32)
    return np.where(c <= .04045, c / 12.92, ((c + .055) / 1.055) ** 2.4)


def l2s(c):
    c = np.clip(np.asarray(c, np.float32), 0, None)
    return np.where(c <= .0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - .055)


W, H = 520, 900
img = np.zeros((H, W, 3), np.float32)
img[:] = np.float32([.05, .06, .08])
zbuf = np.full((H, W), 1e9, np.float32)

yaw = np.deg2rad(35)
pitch = np.deg2rad(-18)
cy, sy = np.cos(yaw), np.sin(yaw)
cp, sp = np.cos(pitch), np.sin(pitch)
R = np.array([[cy, 0, -sy], [0, 1, 0], [sy, 0, cy]]) @ np.array([[1, 0, 0], [0, cp, -sp], [0, sp, cp]])

light = np.array([.45, .78, .44])
light /= np.linalg.norm(light)

center = np.array([0, 12.5, 0])
scale = 26.0

for mat, vs, ns, idx in subs:
    if mat in SKIP:
        continue
    P = (vs - center) @ R.T
    N = ns @ R.T
    sx = W * .5 + P[:, 0] * scale
    sy_ = H * .5 - P[:, 1] * scale
    z = P[:, 2]
    base = s2l(alb[mat]) * s2l(tint[mat]) * 1.3
    e = s2l(emi[mat]) * emul[mat]
    for tri in idx:
        a, b, c = tri
        if z[a] > 200 or z[b] > 200 or z[c] > 200:
            continue
        x0, x1, x2 = sx[a], sx[b], sx[c]
        y0, y1, y2 = sy_[a], sy_[b], sy_[c]
        minx, maxx = int(max(0, min(x0, x1, x2))), int(min(W - 1, max(x0, x1, x2)))
        miny, maxy = int(max(0, min(y0, y1, y2))), int(min(H - 1, max(y0, y1, y2)))
        if minx > maxx or miny > maxy:
            continue
        area = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0)
        if abs(area) < 1e-9:
            continue
        nrm = (N[a] + N[b] + N[c])
        ln = np.linalg.norm(nrm)
        if ln < 1e-6:
            continue
        nrm /= ln
        ndl = max(0.0, float(nrm @ light))
        col = base * (0.35 + 0.75 * ndl) + e
        rgbi = l2s(col / (1.0 + col * 0.55))   # 简易 tonemap
        xs = np.arange(minx, maxx + 1)
        ys = np.arange(miny, maxy + 1)
        X, Y = np.meshgrid(xs + .5, ys + .5)
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
        tgt = img[miny:maxy + 1, minx:maxx + 1]
        tgt[upd] = rgbi

Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8)).save(OUT)
print("written", OUT)
