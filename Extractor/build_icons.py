"""보상 아이콘 → data/reward_icons.png (24x24 아이콘을 가로로 이어 붙인 한 장) + data/reward_icons.json

usage: python -I build_icons.py <rawDir> <schema.json> <dataDir> <Siosa.Extractor.exe> <Content.ggpk>

보상 창 칸 색만으로 어느 퀘스트의 창인지 확정할 수 없을 때(후보가 둘 이상), Siosa 가 칸 그림을 이 아이콘과 비교한다.
  - 스킬 젬: GGPK DDS 가 [문양 | 빈칸 | 결정] 세 칸 → 결정 위에 문양을 합성 (스킬마다 문양이 달라 같은 색끼리도 구분된다)
  - 보조 젬: DDS 한 장
  - 장비·플라스크: DDS 첫 프레임 (플라스크는 [빈 병 | 반사광 | 물약] 이고 보상 창에는 빈 병이 그려진다)
아이템 칸 비율(예: 1x2)과 상관없이 24x24 로 줄여 저장하고, Siosa 도 화면 칸을 같은 크기로 줄여 비교한다.
"""
import json
import os
import struct
import subprocess
import sys

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from dat import Dat, load_schema  # noqa: E402

raw, schema_path, data_dir, extractor, ggpk = sys.argv[1:6]
extractor = os.path.abspath(extractor)
SIZE = 24

S = load_schema(schema_path)
bi = Dat(os.path.join(raw, 'data', 'baseitemtypes.datc64'), S['baseitemtypes'])
iv = Dat(os.path.join(raw, 'data', 'itemvisualidentity.datc64'), S['itemvisualidentity'])
by_name = {bi.get(i, 'Name'): i for i in range(bi.n)}
by_id = {bi.get(i, 'Id'): i for i in range(bi.n)}   # 장비는 베이스 ID 로 (이름이 같은 쌍보석 반지 3종은 그림이 다르다)

rewards = json.load(open(os.path.join(data_dir, 'rewards.json'), encoding='utf8'))
items = {}   # 영문 이름 → (dds 경로, 젬 여부, 가로칸, 세로칸)
for offer in rewards:
    for lst in offer['rewards'].values():
        for r in lst:
            if r['en'] not in items:
                row = by_id[r['base']] if 'base' in r else by_name[r['en']]
                path = iv.get(bi.get(row, 'ItemVisualIdentity'), 'DDSFile').lower()
                items[r['en']] = (path, bool(r.get('gem')), r['w'], r['h'])

need = sorted({v[0] for v in items.values() if not os.path.exists(os.path.join(raw, v[0]))})
for i in range(0, len(need), 100):   # 명령줄 길이 제한 때문에 나눠서
    subprocess.run([extractor, 'dump', ggpk, raw] + need[i:i + 100], check=True, stdout=subprocess.DEVNULL)


def load(path):
    b = open(os.path.join(raw, path), 'rb').read()
    h, w = struct.unpack_from('<II', b, 12)
    if b[84:88] != b'DX10' or struct.unpack_from('<I', b, 128)[0] not in (28, 29):   # R8G8B8A8 만 지원
        return None
    return Image.frombytes('RGBA', (w, h), b[148:148 + w * h * 4])


def prepare(img, gem, w, h):
    if gem:
        if img.width > img.height * 2:          # 스킬 젬: [문양 | 빈칸 | 결정]
            fw = img.width / 3
            emblem = img.crop((0, 0, round(fw), img.height))
            crystal = img.crop((round(img.width - fw), 0, img.width, img.height)).resize(emblem.size)
            out = crystal.copy()
            out.alpha_composite(emblem)
            return out
        return img                              # 보조 젬
    fw = round(img.height * w / h)              # 장비·플라스크: 아이템 비율의 첫 프레임
    return img.crop((0, 0, fw, img.height)) if fw < img.width else img


names, gems, tiles, skipped = [], [], [], []
for en, (path, gem, w, h) in sorted(items.items()):
    img = load(path)
    if img is None:
        skipped.append(en)
        continue
    names.append(en)
    gems.append(gem)
    tiles.append(prepare(img, gem, w, h).resize((SIZE, SIZE), Image.BILINEAR))

atlas = Image.new('RGBA', (SIZE * len(tiles), SIZE))
for i, t in enumerate(tiles):
    atlas.paste(t, (i * SIZE, 0))
atlas.save(os.path.join(data_dir, 'reward_icons.png'), optimize=True)
with open(os.path.join(data_dir, 'reward_icons.json'), 'w', encoding='utf8') as f:
    json.dump({'size': SIZE, 'names': names, 'gem': gems}, f, ensure_ascii=False, separators=(',', ':'))

print(f'reward_icons.png: {len(names)}개 (젬 {sum(gems)}, 장비·플라스크 {len(names) - sum(gems)}), '
      f'{os.path.getsize(os.path.join(data_dir, "reward_icons.png"))} bytes'
      + (f' (형식 미지원으로 건너뜀: {skipped})' if skipped else ''))
