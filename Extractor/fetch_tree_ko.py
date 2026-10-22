"""카카오게임즈 공식 패시브 트리 페이지에서 트리 데이터(한국어)를 꺼내 저장한다.

usage: python -I fetch_tree_ko.py <저장할 data.json>
페이지 안의 `var passiveSkillTreeData = {...};` 가 GGG 공식 트리 데이터(skilltree-export)와 같은 형식이고,
노드 이름·마스터리 효과 문구가 한국어다. 페이지 끝의 `version: '3.29.3b'` 를 함께 저장한다(`_version`).
"""
import json
import re
import sys
import urllib.request

URL = 'https://poe.kakaogames.com/passive-skill-tree'
out = sys.argv[1]

req = urllib.request.Request(URL, headers={'User-Agent': 'Mozilla/5.0 (Siosa tree fetch)'})
html = urllib.request.urlopen(req, timeout=30).read().decode('utf-8')

key = 'var passiveSkillTreeData = '
start = html.index(key) + len(key)
# 중괄호 짝을 세어 JSON 객체 끝을 찾는다 (문자열 안의 중괄호는 건너뜀)
depth, i, in_str, esc = 0, start, False, False
while i < len(html):
    c = html[i]
    if in_str:
        if esc:
            esc = False
        elif c == '\\':
            esc = True
        elif c == '"':
            in_str = False
    elif c == '"':
        in_str = True
    elif c == '{':
        depth += 1
    elif c == '}':
        depth -= 1
        if depth == 0:
            i += 1
            break
    i += 1
data = json.loads(html[start:i])
m = re.search(r"version:\s*'([^']+)'", html[i:])
data['_version'] = m.group(1) if m else ''
with open(out, 'w', encoding='utf-8') as f:
    json.dump(data, f, ensure_ascii=False)
print(f'{out}: 노드 {len(data["nodes"])}개, 버전 {data["_version"]}')
