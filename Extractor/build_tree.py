"""패시브 트리 좌표 → data/tree.json (오버레이·편집 화면에 대략적인 모양을 그리는 데만 쓴다)

usage: python -I build_tree.py <data.json> <outDir> [버전]
  data.json: 카카오게임즈 공식 패시브 트리 페이지의 데이터 (fetch_tree_ko.py 로 받음, 한국어·카카오 클라이언트 버전)
             GGG 공식 데이터(https://github.com/grindinggear/skilltree-export 의 data.json)도 같은 형식이라 그대로 쓸 수 있다(영어)

남기는 것:
  - 본 트리 노드(위치·종류)와 연결선. 마스터리는 연결선 없이 위치만 (종류 5)
  - 전직(어센던시) 트리: 전직마다 노드·연결선을 따로 (공식 데이터에서는 본 트리 바깥 가장자리 여기저기에 있어서,
    그릴 때는 찍은 전직 하나만 구석의 작은 상자에 따로 그린다)
  - 클러스터 주얼·블라이트 노드는 뺀다
노드 위치 = 그룹 중심 + 궤도 반지름 · 궤도 위 각도 (PoB 와 같은 계산).
  궤도당 노드 수가 16·40 인 궤도는 각도가 고르지 않다 (게임과 같은 고정 각도 표).
종류: 0 일반 · 1 주요 · 2 키스톤 · 3 주얼 · 4 클래스/전직 시작 · 5 마스터리
"""
import json
import math
import os
import sys

src, out = sys.argv[1:3]
d = json.load(open(src, encoding='utf-8'))
version = sys.argv[3] if len(sys.argv) > 3 else d.get('_version', '')   # fetch_tree_ko.py 가 페이지 버전을 넣어 둠
C = d['constants']
radii, per_orbit = C['orbitRadii'], C['skillsPerOrbit']

ANGLES_16 = [0, 30, 45, 60, 90, 120, 135, 150, 180, 210, 225, 240, 270, 300, 315, 330]
ANGLES_40 = [0, 10, 20, 30, 40, 45, 50, 60, 70, 80, 90, 100, 110, 120, 130, 135, 140, 150, 160, 170,
             180, 190, 200, 210, 220, 225, 230, 240, 250, 260, 270, 280, 290, 300, 310, 315, 320, 330, 340, 350]


def angle(orbit, index):
    n = per_orbit[orbit]
    if n == 16:
        return ANGLES_16[index]
    if n == 40:
        return ANGLES_40[index]
    return 360.0 * index / n


def position(n):
    g = d['groups'].get(str(n['group']))   # 그룹 0(트리 중심 root)은 그룹 목록에 없다
    if g is None or g.get('isProxy'):
        return None
    a = math.radians(angle(n['orbit'], n['orbitIndex']))
    r = radii[n['orbit']]
    return round(g['x'] + r * math.sin(a)), round(g['y'] - r * math.cos(a))


def kind(n):
    if 'classStartIndex' in n or n.get('isAscendancyStart'):
        return 4
    if n.get('isMastery'):
        return 5
    if n.get('isKeystone'):
        return 2
    if n.get('isJewelSocket'):
        return 3
    return 1 if n.get('isNotable') else 0


trees = {}   # '' = 본 트리, 그 밖 = 전직 이름
for key, n in d['nodes'].items():
    if not key.isdigit() or 'group' not in n or n.get('isProxy') or n.get('isBlighted'):
        continue
    p = position(n)
    if p is None:
        continue
    t = trees.setdefault(n.get('ascendancyName') or '', {'nodes': [], 'index': {}})
    t['index'][int(key)] = len(t['nodes'])
    t['nodes'].append([int(key), p[0], p[1], kind(n)])

for name, t in trees.items():
    edges = set()
    for nid, i in t['index'].items():
        if t['nodes'][i][3] == 5:
            continue   # 마스터리는 선 없음
        for o in d['nodes'][str(nid)].get('out', []):
            j = t['index'].get(int(o))
            if j is not None and i != j and t['nodes'][j][3] != 5:
                edges.add((min(i, j), max(i, j)))
    t['edges'] = sorted(edges)

# 마스터리: 노드 번호 → 이름, 효과 번호 → 문구 (여러 줄이면 " / ")
mastery = {}
for key, n in d['nodes'].items():
    if key.isdigit() and n.get('isMastery') and n.get('masteryEffects'):
        mastery[key] = {'name': n.get('name', ''),
                        'effects': {str(e['effect']): ' / '.join(e.get('stats', [])) for e in n['masteryEffects']}}

main = trees.pop('')
result = {
    'version': version, 'nodes': main['nodes'], 'edges': main['edges'],
    'asc': {name: {'nodes': t['nodes'], 'edges': t['edges']} for name, t in sorted(trees.items())},
    'mastery': mastery,
}
os.makedirs(out, exist_ok=True)
path = os.path.join(out, 'tree.json')
with open(path, 'w', encoding='utf-8') as f:
    json.dump(result, f, separators=(',', ':'), ensure_ascii=False)
print(f'tree.json: 본 트리 노드 {len(main["nodes"])}개(마스터리 포함), 연결 {len(main["edges"])}개, '
      f'전직 {len(trees)}개, {os.path.getsize(path)} bytes')
