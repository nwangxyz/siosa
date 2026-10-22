"""Synthetic stress test: render every class x offer reward window from GGPK icons and run Siosa --detecttest.

Row-split rules tested (the real rule is only measured for 7 → 4+3):
  A: ≤5 one row, else balanced by ceil(n/5) rows, front-loaded   (13 → 5,4,4)
  B: same rows, back-loaded                                       (13 → 4,4,5)
  C: chunks of 6                                                   (13 → 6,6,1)
"""
import json, os, struct, subprocess, sys, collections
from PIL import Image, ImageDraw

sys.path.insert(0, r'D:/siosa/Extractor')
from dat import Dat, load_schema  # noqa

SCR = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(SCR, 'big'); os.makedirs(OUT, exist_ok=True)
RAW = r'D:/siosa/tools/raw/'
EXE = r'D:/siosa/Overlay/bin/out/Siosa.exe'

S = load_schema(r'D:/siosa/tools/schema/schema-poe1.min.json')
bi = Dat(RAW + 'data/baseitemtypes.datc64', S['baseitemtypes'])
iv = Dat(RAW + 'data/itemvisualidentity.datc64', S['itemvisualidentity'])
by_name = {bi.get(i, 'Name'): i for i in range(bi.n)}
by_id = {bi.get(i, 'Id'): i for i in range(bi.n)}   # 장비는 베이스 ID 로 (쌍보석 반지 3종)
rewards = json.load(open(r'D:/siosa/data/rewards.json', encoding='utf8'))
classes = [c['id'] for c in json.load(open(r'D:/siosa/data/classes.json', encoding='utf8'))]

paths = {}
for o in rewards:
    for lst in o['rewards'].values():
        for r in lst:
            row = by_id[r['base']] if 'base' in r else by_name[r['en']]
            paths[r['en']] = iv.get(bi.get(row, 'ItemVisualIdentity'), 'DDSFile').lower()
need = sorted({p for p in paths.values() if not os.path.exists(RAW + p)})
for i in range(0, len(need), 100):
    subprocess.run([r'D:/siosa/Extractor/bin/out/Siosa.Extractor.exe', 'dump', r'C:/Kakaogames/Path of Exile/Content.ggpk', RAW] + need[i:i + 100],
                   check=True, stdout=subprocess.DEVNULL)


def load(path):
    b = open(RAW + path, 'rb').read()
    h, w = struct.unpack_from('<II', b, 12)
    if b[84:88] != b'DX10' or struct.unpack_from('<I', b, 128)[0] not in (28, 29):
        return None
    return Image.frombytes('RGBA', (w, h), b[148:148 + w * h * 4])


cache = {}


def art(r):
    if r['en'] in cache: return cache[r['en']]
    im = load(paths[r['en']])
    if im is not None:
        if r.get('gem') and im.width > im.height * 2:        # 스킬 젬: 결정 + 문양
            fw = im.width / 3
            emb = im.crop((0, 0, round(fw), im.height)); cry = im.crop((round(im.width - fw), 0, im.width, im.height)).resize(emb.size)
            im = cry.copy(); im.alpha_composite(emb)
        elif not r.get('gem'):                                # 장비/플라스크: 아이템 비율의 첫 프레임
            fw = round(im.height * r['w'] / r['h'])
            if fw < im.width: im = im.crop((0, 0, fw, im.height))
    cache[r['en']] = im
    return im


base = Image.open(r'D:/siosa/tools/testdata/reward1.png').convert('RGB')
interior = base.crop((310, 560, 330, 575)).resize((1, 1)).getpixel((0, 0))
band = base.crop((296, 330, 958, 470))         # 명판 + 띠
SLOT, GAP, ROWGAP, UNIT, BORDER = 67, 34.25, 21, 60.5, 6.5
CX = (1920 - 663) / 2 + 1.5
size = lambda n: UNIT * max(1, n) + BORDER


def split(n, rule):
    if rule == 'C': return [min(6, n - i) for i in range(0, n, 6)]
    if n <= 5: return [n]
    rows = (n + 4) // 5; q, r = divmod(n, rows)
    return [q + (1 if (i < r if rule == 'A' else i >= rows - r) else 0) for i in range(rows)]


def render(items, rule, path):
    sizes = split(len(items), rule)
    rows, k = [], 0
    for s in sizes: rows.append(items[k:k + s]); k += s
    block = sum(max(size(it['h']) for it in row) for row in rows) + ROWGAP * (len(rows) - 1)
    shift = (block - SLOT) / 2
    im = base.copy()
    # 창 영역을 어두운 안쪽 색으로 덮고, 명판+띠를 높이만큼 올려 붙인다
    top_win = round(330 - shift)
    im.paste(interior, (296, top_win, 958, round(490 - shift + block + 40)))
    im.paste(band, (296, top_win))
    y = 490 - shift
    for row in rows:
        w = sum(size(it['w']) for it in row) + GAP * (len(row) - 1)
        x = CX - w / 2; rh = max(size(it['h']) for it in row)
        for it in row:
            fw, fh = size(it['w']), size(it['h'])
            fx, fy = x, y + (rh - fh) / 2
            frame = Image.new('RGB', (round(fw), round(fh)), (25, 20, 17))
            ImageDraw.Draw(frame).rectangle((0, 0, round(fw) - 1, round(fh) - 1), outline=(70, 58, 48))   # 실측: 칸 테두리 1px 선 (주변보다 약 35 밝음)
            im.paste(frame, (round(fx), round(fy)))
            a = art(it)
            if a is not None:
                if it.get('gem'):
                    s62 = 62 / 67 * fw
                    ic = a.resize((round(s62), round(s62)), Image.BILINEAR)
                    px, py = fx + (fw - s62) / 2 + 2 / 67 * fw, fy + (fh - s62) / 2 + 4 / 67 * fh
                else:   # 실측: 칸의 94%, 아래 +2/127.5
                    ic = a.resize((round(fw * 0.94), round(fh * 0.94)), Image.BILINEAR)
                    px, py = fx + fw * 0.03 + fw / 67, fy + fh * 0.03 + fh * 2 / 127.5
                layer = Image.new('RGBA', im.size, (0, 0, 0, 0)); layer.paste(ic, (round(px), round(py)))
                im = Image.alpha_composite(im.convert('RGBA'), layer).convert('RGB')
            x += fw + GAP
        y += rh + ROWGAP
    im.save(path)
    return sizes


stats = collections.Counter()
fails = []
for rule in 'ABC':
    for cls in classes:
        for o in rewards:
            items = sorted(o['rewards'].get(cls) or [], key=lambda it: (-it.get('h', 1), -it.get('w', 1)))   # 게임과 같은 순서 (키 큰 것 → 넓은 것)
            if len(items) < 1: continue
            f = os.path.join(OUT, f'{rule}_{cls}_{o["id"]}.png')
            sizes = render(items, rule, f)
            subprocess.run([EXE, '--detecttest', f, cls, str(o['act'])], check=True)
            txt = open(f + '.txt', encoding='utf8').read().split('\n')
            os.remove(f + '.txt'); os.remove(f)
            line = txt[1]
            got = line.split(' ')[0]
            if '아이콘 비교' in line:
                tail = line.split('→')[-1].strip()
                got = tail if (tail != '확정 못 함' and not tail.startswith('구분불가')) else 'TIE:' + tail
            elif '구분불가' in line:
                got = 'TIE:' + got
            key = 'ok' if got == o['id'] else ('tie' if got.startswith('TIE') else 'wrong')
            stats[(rule, key)] += 1
            if key != 'ok':
                fails.append(f'{rule} {cls:9} {o["id"]:6} n={len(items):2} {"+".join(map(str, sizes)):8} → {line}')

for rule in 'ABC':
    print(rule, {k: stats[(rule, k)] for k in ('ok', 'tie', 'wrong')})
print('\n'.join(fails[:60]))
