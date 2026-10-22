"""해상도별 실제 게임 캡처 회귀 테스트 (Siosa.exe --detecttest).

캡처: 보상 창을 연 채 Ctrl+Shift+D (debug\\reward_*.png = 게임 화면 영역 전체). 인벤토리가 열린 상태.
파일은 tools\\testdata\\res 에 둔다 (용량이 커서 git 에는 넣지 않음, 2026-10-22 쉐도우로 촬영).
usage: python -I tools\\tests\\resolutions.py
"""
import os
import subprocess

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
EXE = os.path.join(ROOT, 'Overlay', 'bin', 'out', 'Siosa.exe')
DATA = os.path.join(ROOT, 'tools', 'testdata', 'res')

# 파일 이름: <게임 화면 크기>_<보상 ID>.png  (창 모드는 테두리를 뺀 크기)
#   a1q1 힐록 젬 6개 한 줄 / a1q5 자비로운 임무 플라스크 3개(1x2) / a4q1 휴거 장치 젬 14개(6+6+2)
#   a9q3 태풍의 칼 무기 6개(4+2, 크기 섞임) / a10q2 죽음과 부활 갑옷 3개(2x3)
CASES = [
    ('1904x1001', 'a1q1', 1), ('1904x1001', 'a1q5', 1), ('1904x1001', 'a4q1', 4),   # 1080p 창 모드
    ('2544x1361', 'a1q1', 1), ('2544x1361', 'a4q1', 4),                             # 1440p 창 모드
    ('2560x1440', 'a1q1', 1), ('2560x1440', 'a10q2', 10),
    ('1366x768', 'a1q1', 1), ('1366x768', 'a1q5', 1), ('1366x768', 'a4q1', 4), ('1366x768', 'a10q2', 10),
    ('1280x720', 'a1q1', 1), ('1280x720', 'a1q5', 1), ('1280x720', 'a4q1', 4), ('1280x720', 'a9q3', 9),
    ('1680x720', 'a1q1', 1), ('1680x720', 'a1q5', 1), ('1680x720', 'a4q1', 4), ('1680x720', 'a9q3', 9),   # 21:9
    ('1440x900', 'a1q1', 1), ('1440x900', 'a1q5', 1), ('1440x900', 'a4q1', 4), ('1440x900', 'a9q3', 9),   # 16:10
]


def judge(path, cls, act):
    subprocess.run([EXE, '--detecttest', path, cls, str(act)], check=True)
    lines = open(path + '.txt', encoding='utf8').read().splitlines()
    os.remove(path + '.txt')
    if '창 열림: True' not in lines[0]:
        return 'closed'
    got = lines[1].split(' ')[0]
    if '아이콘 비교' in lines[1]:
        got = lines[1].split('→')[-1].strip()
    return got


if __name__ == '__main__':
    ok = 0
    for size, want, act in CASES:
        got = judge(os.path.join(DATA, f'{size}_{want}.png'), 'DexInt', act)
        good = got == want
        ok += good
        print(f'{"OK  " if good else "FAIL"} {size:10} 기대 {want:6} 결과 {got}')
    print(f'{ok}/{len(CASES)} 통과')
