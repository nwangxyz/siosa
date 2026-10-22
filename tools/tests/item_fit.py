"""Fit how equipment/flask art is drawn inside reward frames (flask.png: Dex a1q5)."""
import json, struct, sys
from PIL import Image
import numpy as np
sys.path.insert(0, r'D:/siosa/Extractor')
from dat import Dat, load_schema

RAW = r'D:/siosa/tools/raw/'
S = load_schema(r'D:/siosa/tools/schema/schema-poe1.min.json')
bi = Dat(RAW + 'data/baseitemtypes.datc64', S['baseitemtypes']); iv = Dat(RAW + 'data/itemvisualidentity.datc64', S['itemvisualidentity'])
by = {bi.get(i, 'Name'): i for i in range(bi.n)}
o = {x['id']: x for x in json.load(open(r'D:/siosa/data/rewards.json', encoding='utf8'))}
items = o['a1q5']['rewards']['Dex']
print([(r['en'], r['w'], r['h']) for r in items])


def art(r):
    p = iv.get(bi.get(by[r['en']], 'ItemVisualIdentity'), 'DDSFile').lower()
    b = open(RAW + p, 'rb').read(); h, w = struct.unpack_from('<II', b, 12)
    im = Image.frombytes('RGBA', (w, h), b[148:148 + w * h * 4])
    print(r['en'], p, im.size)
    fw = round(im.height * r['w'] / r['h'])
    return im.crop((0, 0, fw, im.height)) if fw < im.width else im


arts = [art(r) for r in items]
shot = Image.open(r'D:/siosa/tools/testdata/flask.png').convert('RGB')
fw, fh = 67, 127.5; cx = (1920 - 663) / 2 + 1.5; x0 = cx - (3 * fw + 2 * 34.25) / 2; top = 490 - 30.25
cells = [shot.crop((round(x0 + i * (fw + 34.25)), round(top), round(x0 + i * (fw + 34.25) + fw), round(top + fh))) for i in range(3)]
NW, NH = 24, 24
import os
ALPHA = int(os.environ.get('ALPHA','40'))


BG = shot.crop((310, 560, 330, 575)).resize((1, 1)).getpixel((0, 0))
print('bg', BG)


def dist(cell, a, sc, dx, dy):
    # 칸 전체를 비교: 아이콘을 창 안쪽 어두운 색 위에 합성해서 (투명한 유리병도 비교되도록)
    w, h = fw * sc, fh * sc
    x, y = (fw - w) / 2 + dx, (fh - h) / 2 + dy
    c = np.asarray(cell.crop((round(x), round(y), round(x + w), round(y + h))).resize((NW, NH), Image.BILINEAR), dtype=float)
    bgim = Image.new('RGBA', a.size, BG + (255,)); bgim.alpha_composite(a)
    t = np.asarray(bgim.convert('RGB').resize((NW, NH), Image.BILINEAR), dtype=float)
    al = np.asarray(a.resize((NW, NH), Image.BILINEAR), dtype=float)[:, :, 3] > ALPHA
    return float(np.abs(c[al] - t[al]).mean()) if al.sum() > 10 else 1e9


best = min(((sum(dist(cells[i], arts[i], sc / 100, dx, dy) for i in range(3)), sc, dx, dy)
            for sc in range(80, 101, 2) for dx in range(-3, 4) for dy in range(-4, 5, 2)))
print('best: scale %.2f dx %d dy %d avg %.1f' % (best[1] / 100, best[2], best[3], best[0] / 3))
_, sc, dx, dy = best
for i in range(3):
    print(items[i]['en'], ['%.1f' % dist(cells[i], arts[j], sc / 100, dx, dy) for j in range(3)])
