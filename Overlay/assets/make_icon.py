"""Siosa 아이콘(시안 A: 강조된 보상 칸)을 여러 크기로 그려 siosa.ico 를 만든다.

usage: python -I make_icon.py
  큰 크기는 96 단위 설계를 그대로 키우고, 16·24px 는 금색 모서리를 굵게·젬을 크게 한 단순한 모양으로 따로 그린다.
  각 크기를 8배로 그린 뒤 줄여서 가장자리를 부드럽게 한다. ICO 안에는 PNG 로 넣는다 (Windows Vista 이상).
"""
import io
import os
import struct

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
BG, SLOT, SLOT_EDGE = (30, 31, 34, 255), (43, 45, 49, 255), (58, 61, 67, 255)
GOLD = (232, 190, 92, 255)
GEM, GEM_HI, GEM_LO = (92, 140, 232, 255), (143, 178, 242, 255), (63, 108, 196, 255)
SS = 8   # 슈퍼샘플링 배율


def bracket(d, pts, w):
    """둥근 끝·둥근 꺾임의 굵은 선 (금색 모서리)"""
    d.line(pts, fill=GOLD, width=int(w), joint="curve")
    r = w / 2
    for x, y in (pts[0], pts[-1], pts[1]):
        d.ellipse((x - r, y - r, x + r, y + r), fill=GOLD)


def draw(size, simple):
    S = size * SS
    u = S / 96.0   # 설계 단위(96) → 픽셀
    im = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle((0, 0, S - 1, S - 1), radius=20 * u, fill=BG)
    if simple:
        # 작은 크기: 칸 테두리 없이 굵은 모서리 + 큰 젬
        w = 11 * u
        a, b = 13 * u, 83 * u
        L = 22 * u
        for pts in (((a, a + L), (a, a), (a + L, a)), ((b - L, a), (b, a), (b, a + L)),
                    ((b, b - L), (b, b), (b - L, b)), ((a + L, b), (a, b), (a, b - L))):
            bracket(d, pts, w)
        g = [(48 * u, 26 * u), (68 * u, 46 * u), (48 * u, 72 * u), (28 * u, 46 * u)]
        d.polygon(g, fill=GEM)
        d.polygon([g[0], g[1], (48 * u, 49 * u)], fill=GEM_HI)
    else:
        d.rounded_rectangle((26 * u, 26 * u, 70 * u, 70 * u), radius=5 * u, fill=SLOT, outline=SLOT_EDGE, width=int(2 * u))
        g = [(48 * u, 34 * u), (60 * u, 46 * u), (48 * u, 62 * u), (36 * u, 46 * u)]
        d.polygon(g, fill=GEM)
        d.polygon([g[0], g[1], (48 * u, 48 * u)], fill=GEM_HI)
        d.polygon([g[3], (48 * u, 48 * u), g[2]], fill=GEM_LO)
        w = 6 * u
        for pts in (((16 * u, 30 * u), (16 * u, 16 * u), (30 * u, 16 * u)), ((66 * u, 16 * u), (80 * u, 16 * u), (80 * u, 30 * u)),
                    ((80 * u, 66 * u), (80 * u, 80 * u), (66 * u, 80 * u)), ((30 * u, 80 * u), (16 * u, 80 * u), (16 * u, 66 * u))):
            bracket(d, pts, w)
    return im.resize((size, size), Image.LANCZOS)


def write_ico(path, images):
    """PNG 를 담은 ICO (여러 크기)"""
    blobs = []
    for im in images:
        buf = io.BytesIO()
        im.save(buf, format="PNG")
        blobs.append(buf.getvalue())
    header = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    entries = b""
    for im, blob in zip(images, blobs):
        w = im.width if im.width < 256 else 0
        entries += struct.pack("<BBBBHHII", w, w, 0, 0, 1, 32, len(blob), offset)
        offset += len(blob)
    with open(path, "wb") as f:
        f.write(header + entries + b"".join(blobs))


sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
images = [draw(s, simple=s <= 24) for s in sizes]
write_ico(os.path.join(HERE, "siosa.ico"), images)
# 미리보기 (README·확인용)
sheet = Image.new("RGBA", (sum(sizes) + 10 * len(sizes) + 10, 276), (0, 0, 0, 0))
x = 10
for s, im in zip(sizes, images):
    sheet.paste(im, (x, 266 - s), im)
    x += s + 10
sheet.save(os.path.join(HERE, "siosa_preview.png"))
draw(512, simple=False).save(os.path.join(HERE, "siosa_512.png"))
print("siosa.ico:", os.path.getsize(os.path.join(HERE, "siosa.ico")), "bytes,", sizes)
