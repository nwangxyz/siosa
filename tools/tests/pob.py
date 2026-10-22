"""PoB 불러오기 회귀 테스트: Siosa.exe --pobtest 로 샘플 빌드의 자동 선택 결과를 확인한다.

usage: python -I tools\tests\pob.py
샘플 코드는 tools\testdata\pob\*.txt (pobb.in / poe.ninja 에서 받은 코드).
"""
import os
import subprocess
import tempfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
EXE = os.path.join(ROOT, 'Overlay', 'bin', 'out', 'Siosa.exe')
DATA = os.path.join(ROOT, 'tools', 'testdata', 'pob')

# (파일, 클래스 줄에 들어갈 글자, 도적 선택, {보상 ID: 골라야 할 영문 젬 이름})
CASES = [
    # 스킬 세트 Act 1/2/3 + 엔드게임 세트: 1장 보상은 엔드게임 서리 폭탄이 아니라 레벨링 화염 벽
    ('witch_act_sets.txt', 'Witch', 'None', {'a1q4': 'Flame Wall', 'a1q1': 'Kinetic Bolt', 'a2q4': 'Trinity Support'}),
    # 레벨링 세트 + 맵 세트: 맵용 원소의 일격·폭풍우가 아니라 레벨링 화살비·포대 쇠뇌
    ('ranger_act_sets.txt', 'Ranger', 'Alira', {'a1q3': 'Rain of Arrows', 'a3q8': 'Artillery Ballista', 'a1q5b': 'Mirage Archer Support'}),
    # 한 세트 + 젬 없는 이름 묶음("1Lv", "8Lv" …) 구간: 1Lv 구간의 공격 속도 증가가 40Lv 구간 생명력 전환보다 먼저
    ('marauder_level_sections.txt', 'Marauder', 'None', {'a1q2b': 'Faster Attacks Support', 'a1q3': 'Sunder'}),
]

ok = total = 0
for f, cls, bandit, want in CASES:
    out = os.path.join(tempfile.gettempdir(), 'siosa_pobtest.txt')
    subprocess.run([EXE, '--pobtest', os.path.join(DATA, f), out], check=True)
    text = open(out, encoding='utf-8-sig').read()
    os.remove(out)
    picks = {}
    for line in text.split('\n'):
        parts = line.split()
        if len(parts) > 1 and parts[0].startswith('a') and 'q' in parts[0] and '(' in line:
            picks[parts[0]] = line[line.index('(') + 1:line.index(')')]
    total += 2 + len(want)
    good_cls = any(line.startswith('클래스') and cls in line for line in text.split('\n'))
    ok += good_cls
    print(f'{"OK  " if good_cls else "FAIL"} {f:30} 클래스 {cls}')
    good_bandit = f'도적 {bandit}' in [line.strip() for line in text.split(chr(10))]
    ok += good_bandit
    print(f'{"OK  " if good_bandit else "FAIL"} {f:30} 도적 {bandit}')
    for oid, gem in want.items():
        good = picks.get(oid) == gem
        ok += good
        print(f'{"OK  " if good else "FAIL"} {f:30} {oid:6} 기대 {gem:24} 결과 {picks.get(oid)}')
# 패시브 트리 단계: PoB 트리(Spec) 수와 노드 수 (머라우더 샘플은 8Lv ~ 60Lv 10단계, 노드가 단계마다 늘어남)
out = os.path.join(tempfile.gettempdir(), 'siosa_pobtest.txt')
subprocess.run([EXE, '--pobtest', os.path.join(DATA, 'marauder_level_sections.txt'), out], check=True)
text = open(out, encoding='utf-8-sig').read()
os.remove(out)
line = next((l for l in text.splitlines() if l.startswith('트리 ')), '')
counts = [int(x.split('(')[-1].rstrip(')')) for x in line.split(': ', 1)[-1].split(', ') if '(' in x]
good = line.startswith('트리 10단계') and counts == sorted(counts) and counts[0] > 0
total += 1
ok += good
print(f'{"OK  " if good else "FAIL"} marauder_level_sections.txt    트리 10단계, 노드 수 증가 {counts}')

# 처음 시작할 때 받는 젬은 구매 목록에 없어야 한다 (머라우더: 무자비 보조, 빌드 링크에는 있음)
out = os.path.join(tempfile.gettempdir(), 'siosa_pobtest.txt')
subprocess.run([EXE, '--pobtest', os.path.join(DATA, 'marauder_level_sections.txt'), out], check=True)
text = open(out, encoding='utf-8-sig').read()
os.remove(out)
buy = text.split('구매:')[1].split('링크:')[0]
good = '무자비 보조' not in buy and '묵직한 타격' not in buy and '무자비 보조' in text
total += 1
ok += good
print(f'{"OK  " if good else "FAIL"} marauder_level_sections.txt    시작 젬(묵직한 타격·무자비 보조)은 구매 목록에서 빠짐')
print(f'{ok}/{total} 통과')
