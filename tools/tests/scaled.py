"""해상도 모의 테스트: 1080p 스크린샷을 다른 16:9 해상도 크기로 늘리고 줄여서 screens.py 와 같은 기대값으로 판별한다.

usage: python -I tools\\tests\\scaled.py [WxH ...]   (기본: 3840x2160 2560x1440 1600x900 1366x768 1280x720)
실제 게임 화면이 아니라 늘리고 줄인 그림이라(선이 흐려짐) 참고용. 결과가 나쁘면 그 해상도는 실제 화면으로 확인한다.
"""
import os
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from PIL import Image  # noqa: E402
from screens import CASES, DATA, judge  # noqa: E402

sizes = sys.argv[1:] or ['3840x2160', '2560x1440', '1600x900', '1366x768', '1280x720']
tmp = tempfile.mkdtemp(prefix='siosa_scaled_')
total_ok = total = 0
for size in sizes:
    w, h = map(int, size.split('x'))
    ok = 0
    fails = []
    for png, cls, act, want in CASES:
        src = Image.open(os.path.join(DATA, png)).convert('RGB')
        path = os.path.join(tmp, f'{w}x{h}_{png}')
        src.resize((w, h), Image.LANCZOS).save(path)
        got = judge(path, cls, act, want)
        os.remove(path)
        if got == want:
            ok += 1
        else:
            fails.append(f'{png} 기대 {want} 결과 {got}')
    total_ok += ok
    total += len(CASES)
    print(f'{size:>9}: {ok}/{len(CASES)}' + ('' if not fails else '   실패: ' + ' / '.join(fails)))
os.rmdir(tmp)
print(f'합계 {total_ok}/{total}')
