"""실제 게임 스크린샷 회귀 테스트: Siosa.exe --detecttest 로 판별해서 기대값과 비교한다.

usage: python -I tools\\tests\\screens.py
새 스크린샷을 tools\\testdata 에 넣고 아래 CASES 에 (파일, 클래스, 액트, 기대 결과)를 추가하면 된다.
기대 결과: 보상 ID (예: a1q3) / 'closed' = 보상 창이 없는 화면
         / 'held:보상 ID' = 띠가 툴팁에 가려졌지만 판별해 둔 창이 유지되어야 하는 화면
"""
import os
import subprocess

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
EXE = os.path.join(ROOT, 'Overlay', 'bin', 'out', 'Siosa.exe')
DATA = os.path.join(ROOT, 'tools', 'testdata')

CASES = [
    # 파일             클래스  액트  기대        설명
    ('reward1.png',  'Dex', 1, 'a1q1'),    # 힐록 보상, 젬 5개 한 줄
    ('flask.png',    'Dex', 1, 'a1q5'),    # 자비로운 임무 플라스크 3개 (1x2, 창 30px 위로)
    ('now.png',      'Dex', 1, 'a1q5b'),   # 자비로운 임무 보조 젬 4개 (로아 알과 색 배열 같음 → 아이콘)
    ('tie_a1q4.png', 'Dex', 1, 'a1q4'),    # 로아 알 스킬 젬 4개 (자비로운 임무와 색 배열 같음 → 아이콘)
    ('a1q2b.png',    'Dex', 1, 'a1q2b'),   # 감금된 덩치 네사 보조 젬 7개 (4+3, 창 44px 위로)
    ('a1q3.png',     'Dex', 1, 'a1q3'),    # 사이렌의 마침곡 젬 8개 (4+4)
    ('a3q13.png',    'Dex', 3, 'a3q13'),   # 희망 한 모금 반지 3개 (끈 감개와 색 배열 같음 → 아이콘)
    ('a9q3.png',     'Dex', 9, 'a9q3'),    # 태풍의 칼 무기 6개 (4+2, 키 큰 것 → 넓은 것 순서)
    ('a3q4.png',     'Dex', 3, 'a3q4'),    # 디알라 끈 감개 (솔라리스 사원 2층, 마을 밖)
    ('hover1.png',   'Dex', 1, 'closed'),  # 힐록 보상 창 위에 툴팁 (띠가 가려져 "열림"은 아님 → 실제로는 칸 확인으로 유지)
    ('screen.png',   'Dex', 1, 'closed'),  # NPC 대화창 (보상 창 없음)
    ('hover_a3q1_doom.png',     'Int', 3, 'held:a3q1'),  # 떠나보낸 연인 파멸 툴팁: 테두리 선 3/13칸뿐 → 칸 색으로 유지
    ('hover_a3q1_zealotry.png', 'Int', 3, 'held:a3q1'),  # 같은 창 열광 툴팁 (테두리 선 7/13칸)
    ('hover_a3q8_storm.png',    'Int', 3, 'held:a3q8'),  # 그라비시우스 폭풍 연대 툴팁: 불투명한 보조 젬 제목 띠가 한 칸을 덮음
]


def judge(path, cls, act, want):
    """스크린샷 하나를 판별해서 기대값과 같은 형식의 결과를 돌려준다."""
    subprocess.run([EXE, '--detecttest', path, cls, str(act)], check=True)
    lines = open(path + '.txt', encoding='utf8').read().splitlines()
    os.remove(path + '.txt')
    opened = '창 열림: True' in lines[0]
    held = [l for l in lines if l.startswith('가려진 창:')]
    if want.startswith('held:'):
        return 'held:' + held[0].split()[2] if held and held[0].endswith('유지') else ('closed' if not opened else 'open')
    if not opened:
        return 'closed'
    got = lines[1].split(' ')[0]
    if '아이콘 비교' in lines[1]:
        got = lines[1].split('→')[-1].strip()
    return got


if __name__ == '__main__':
    ok = 0
    for png, cls, act, want in CASES:
        got = judge(os.path.join(DATA, png), cls, act, want)
        good = got == want
        ok += good
        print(f'{"OK  " if good else "FAIL"} {png:24} 기대 {want:9} 결과 {got}')
    print(f'{ok}/{len(CASES)} 통과')
