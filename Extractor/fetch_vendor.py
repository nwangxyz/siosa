"""PoE 위키(poewiki.net)에서 퀘스트 상점 젬 판매 목록(Cargo 표 vendor_rewards)을 받는다.

usage: python -I fetch_vendor.py <outFile>
  퀘스트를 깨면 상점에서 파는 젬 목록은 게임 파일(GGPK)에 없어서 위키 자료를 쓴다.
  데이터를 갱신할 때(update_data.bat)만 실행한다. Siosa 실행 중에는 접속하지 않는다.
"""
import json
import sys
import time
import urllib.request

out = sys.argv[1]
URL = ("https://www.poewiki.net/w/api.php?action=cargoquery&tables=vendor_rewards"
       "&fields=_pageName=page,act,class_ids,npc,quest,quest_id&limit=500&offset={}&format=json")

rows, offset = [], 0
while True:
    req = urllib.request.Request(URL.format(offset), headers={"User-Agent": "Siosa-data-build/1.0 (personal leveling overlay)"})
    with urllib.request.urlopen(req, timeout=30) as r:
        page = [x["title"] for x in json.load(r).get("cargoquery", [])]
    rows += page
    if len(page) < 500:
        break
    offset += 500
    time.sleep(1)

with open(out, "w", encoding="utf8") as f:
    json.dump(rows, f, ensure_ascii=False)
print(f"vendor_rewards: {len(rows)} rows → {out}")
