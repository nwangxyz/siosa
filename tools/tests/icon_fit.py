"""Fit how the game draws gem icons inside reward cells, then test discrimination."""
import struct
from PIL import Image
import numpy as np

RAW = r'D:/siosa/tools/raw/art/2ditems/gems/'
SHOT = r'D:/siosa/tools/testdata/tie_a1q4.png'


def dds(path):
    b = open(RAW + path, 'rb').read()
    h, w = struct.unpack_from('<II', b, 12)
    off = 148 if b[84:88] == b'DX10' else 128
    return Image.frombytes('RGBA', (w, h), b[off:off + w * h * 4])


def composite(path):
    im = dds(path)
    if im.width > im.height * 2:                     # 스킬 젬: [문양 | 빈칸 | 결정]
        fw = im.width / 3
        emblem = im.crop((0, 0, round(fw), im.height))
        crystal = im.crop((round(im.width - fw), 0, im.width, im.height)).resize(emblem.size)
        out = crystal.copy()
        out.alpha_composite(emblem)
        return out
    return im                                        # 보조 젬: 한 장


names = {
    "Sniper's Mark": 'projectileweakness.dds', 'Bear Trap': 'beartrap.dds', 'Puncture': 'puncture.dds',
    'Shrapnel Ballista': 'shotguntotembowskillgem.dds',
    'Pierce Support': 'support/pierce.dds', 'Mirage Archer Support': 'support/miragearcher.dds',
}
icons = {n: composite(p) for n, p in names.items()}

shot = Image.open(SHOT).convert('RGB')
slot, gap = 67, 34.25
cx = (1920 - 663) / 2 + 1.5
n = 4
x0 = cx - (n * slot + (n - 1) * gap) / 2
cells = [shot.crop((round(x0 + i * (slot + gap)), 490, round(x0 + i * (slot + gap)) + slot, 490 + slot)) for i in range(n)]
truth = ["Sniper's Mark", 'Bear Trap', 'Puncture', 'Shrapnel Ballista']

N = 24   # 비교 해상도


def prep(cell, icon, size, dx, dy):
    # 칸 이미지에서 아이콘이 그려졌다고 가정한 위치를 잘라 N x N 으로 줄이고, 아이콘 알파로 마스크
    x = (slot - size) / 2 + dx
    y = (slot - size) / 2 + dy
    c = np.asarray(cell.crop((round(x), round(y), round(x + size), round(y + size))).resize((N, N), Image.BILINEAR), dtype=float)
    ic = icon.resize((N, N), Image.BILINEAR)
    a = np.asarray(ic, dtype=float)
    mask = a[:, :, 3] > 160
    mask[: N // 3, 2 * N // 3:] = False              # 오른쪽 위 "!" 표시 영역 제외
    return c, a[:, :, :3], mask


def dist(cell, icon, size, dx, dy):
    c, a, m = prep(cell, icon, size, dx, dy)
    if m.sum() < 20:
        return 1e9
    return float(np.abs(c[m] - a[m]).mean())


# 1) 정답 짝으로 크기/위치 맞추기
best = None
for size in range(48, 72, 2):
    for dx in range(-4, 5, 2):
        for dy in range(-4, 5, 2):
            d = sum(dist(cells[i], icons[truth[i]], size, dx, dy) for i in range(n))
            if best is None or d < best[0]:
                best = (d, size, dx, dy)
print('best fit: size=%d dx=%d dy=%d  avg dist=%.1f' % (best[1], best[2], best[3], best[0] / n))
_, size, dx, dy = best

# 2) 각 칸을 모든 아이콘과 비교 → 정답이 가장 가까운지, 차이가 얼마인지
for i in range(n):
    ds = sorted((dist(cells[i], icons[k], size, dx, dy), k) for k in icons)
    print(f'cell {i + 1} (정답 {truth[i]}):', ', '.join(f'{k} {d:.1f}' for d, k in ds))
