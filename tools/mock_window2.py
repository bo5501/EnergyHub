"""按 HubWindow.cs 的坐标离线渲染窗口布局示意图（用于检查重叠，不是游戏截图）。"""
from PIL import Image, ImageDraw, ImageFont

S = 1.6
W, H = 640, 668
BG = (14, 21, 28)
PANEL = (26, 34, 44)
LINE = (52, 87, 112)
TEXT = (219, 235, 247)
DIM = (143, 168, 189)
ACC = (87, 219, 255)
OK = (115, 242, 158)
WARN = (255, 171, 77)
VIOLET = (184, 140, 255)
ORANGE = (255, 166, 71)
ROW = (23, 36, 48)
ROWON = (36, 77, 97)

FP = r"C:/Windows/Fonts/msyh.ttc"


def font(sz):
    return ImageFont.truetype(FP, int(sz * S))


def T(d, x, y, w, h, s, sz, col, anchor="lm"):
    d.rectangle([x * S, y * S, (x + w) * S, (y + h) * S], outline=(40, 46, 52))
    tw = d.textlength(s, font=font(sz))
    fx = x * S + 2
    if anchor == "lm":
        pass
    elif anchor == "mm":
        fx = x * S + ((w * S) - tw) / 2
    else:
        fx = (x + w) * S - tw
    fy = y * S + (h * S - sz * S * 1.32) / 2
    d.text((fx, fy), s, font=font(sz), fill=col)


def R(d, x, y, w, h, col, outline=None):
    d.rectangle([x * S, y * S, (x + w) * S, (y + h) * S], fill=col, outline=outline)


img = Image.new("RGB", (int(W * S), int(H * S)), (6, 9, 12))
d = ImageDraw.Draw(img)
R(d, 0, 0, W, H, BG)
R(d, 0, 0, W, 2, ACC)

T(d, 20, 6, 460, 40, "星际能量枢纽", 20, TEXT)
R(d, W - 48, 10, 32, 30, PANEL, LINE)
T(d, W - 48, 10, 32, 30, "×", 20, DIM, "mm")
R(d, 18, 52, W - 36, 1, LINE)

T(d, 20, 62, 120, 24, "本地电网", 13, DIM)
T(d, 116, 62, 500, 24, "已接入电网  #12", 13, TEXT)

T(d, 20, 92, 120, 24, "能量池", 13, DIM)
R(d, 116, 97, 400, 14, (26, 41, 54))
R(d, 116, 97, 260, 14, ACC)
T(d, 526, 92, 100, 24, "65%  65.0 MJ", 12, TEXT)

T(d, 20, 126, 120, 24, "工作模式", 13, DIM)
for i, (nm, col, on) in enumerate([("待机", ACC, False), ("发送能量", VIOLET, False), ("接收能量", ORANGE, True)]):
    x = 116 + 118 * i
    R(d, x, 122, 112, 36, (46, 63, 78) if on else (20, 32, 41), LINE)
    T(d, x, 122, 112, 36, nm, 14, (255, 255, 255) if on else TEXT, "mm")
    R(d, x + 8, 158, 96, 3, col if on else None)

T(d, 20, 172, 120, 24, "对接状态", 13, DIM)
T(d, 116, 172, 500, 24, "已对接发送终端 #37，正在接收", 13, OK)

R(d, 18, 200, W - 36, 1, LINE)
T(d, 20, 204, 380, 26, "可用发送终端（点「连接」建立链路）", 13, DIM)
T(d, 400, 204, 56, 26, "1/2", 11, DIM, "rm")
for x, s in ((462, "◀"), (494, "▶")):
    R(d, x, 204, 28, 24, (20, 32, 41), LINE)
    T(d, x, 204, 28, 24, s, 11, TEXT, "mm")

rows = [
    ("海洋星  ·  终端 #37", "远程  累计 1.24 GJ", True, "断开", True),
    ("本星球  ·  终端 #12", "在线  池 65.0 MJ", True, "连接", False),
    ("戈壁星  ·  终端 #8", "远程  累计 402.1 MJ", True, "连接", False),
    ("冰原星  ·  终端 #21", "远程  累计 0 J", True, "连接", False),
    ("本星球  ·  终端 #19", "未通电", True, "连接", False),
]
for i, (nm, note, sel, btn, on) in enumerate(rows):
    y = 234 + 40 * i
    R(d, 20, y, 600, 36, ROWON if on else ROW)
    T(d, 20 + 8, y, 300, 36, nm, 13, OK if on else TEXT)
    T(d, 312, y, 158, 36, note, 11, WARN if note.startswith("未通电") else DIM)
    if sel:
        R(d, 476, y + 3, 110, 30, (41, 112, 148) if on else (33, 51, 66), LINE)
        T(d, 476, y + 3, 110, 30, btn, 12, TEXT, "mm")

R(d, 18, 438, W - 36, 1, LINE)
T(d, 20, 444, 600, 24, "接收速率  6.6 MW    →  本地电网", 13, OK)
T(d, 20, 468, 600, 22, "累计发送  0 J        累计接收  1.24 GJ", 12, DIM)

R(d, 18, 494, W - 36, 1, LINE)
T(d, 20, 498, 300, 24, "消耗物品卡槽", 13, DIM)
T(d, 320, 498, 280, 24, "本星球待接收  12.0 MJ", 11, ACC, "rm")

cells = [
    ("信息矩阵", "同星系", 120, True),
    ("空间翘曲器", "跨星系", 0, False),
    ("黑雾引力透镜", "跨星系", 0, False),
]
for i, (nm, tag, cnt, used) in enumerate(cells):
    y = 528 + 32 * i
    R(d, 20, y, 600, 28, ROW)
    R(d, 22, y + 1, 26, 26, (30, 44, 58), LINE)
    T(d, 22, y + 1, 26, 26, "i", 12, ACC if used else DIM, "mm")
    T(d, 56, y, 150, 28, nm, 12, TEXT)
    T(d, 210, y, 100, 28, tag, 10, ACC if used else DIM)
    T(d, 312, y, 120, 28, "× %d" % cnt, 12, TEXT if cnt else WARN)
    R(d, 436, y + 2, 82, 24, (33, 51, 66), LINE)
    T(d, 436, y + 2, 82, 24, "放入 20", 10, TEXT, "mm")
    R(d, 524, y + 2, 66, 24, (33, 51, 66), LINE)
    T(d, 524, y + 2, 66, 24, "取出", 10, DIM, "mm")

T(d, 20, 626, 600, 34, "发送端：从本地电网抽电存池，必须有接收端点「连接」连上我才会发送。同恒星系内扣「信息矩阵」；跨星系扣「空间翘曲器 + 黑雾引力透镜」，每 200.0 MJ 各扣 1 个。", 10, DIM)

img.save(r"D:/戴森球计划反编译文件/hub_window_v111.png")
print("ok")
