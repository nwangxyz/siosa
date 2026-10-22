"""raw datc64 → Siosa data/*.json

usage: python -I build_data.py <rawDir> <schema.json> <outDir>
  rawDir 아래에 data/*.datc64 (영문) 와 data/korean/*.datc64 (한국어) 가 있어야 한다.
"""
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from dat import Dat, load_schema  # noqa: E402

raw, schema_path, out = sys.argv[1:4]
S = load_schema(schema_path)

TABLES = ["characters", "quest", "questrewards", "questrewardoffers",
          "baseitemtypes", "itemclasses", "skillgems", "worldareas",
          "npctalk", "npcs", "queststaticrewards"]

# 퀘스트 완료 판정 지역(수동 관리): (지역 ID, 확실 여부)
#   확실(True)  = 퀘스트를 깨야만 들어갈 수 있는 다음 지역 (예: 브루투스를 잡아야 죄수의 문)
#   추정(False) = 퀘스트 목표 지역에 들어간 적이 있음 (아마 깼을 것)
# 없는 퀘스트는 자동 판정하지 않는다.
COMPLETE = {
    "a1q1": ("1_1_town", False), "a1q5": ("1_1_2a", False), "a1q5b": ("1_1_2a", False),
    "a1q4": ("1_1_4_1", True), "a1q4b": ("1_1_4_1", True),
    "a1q2": ("1_1_8", True), "a1q2b": ("1_1_8", True),
    # 머베일·말라카이 보상은 다음 액트로 넘어가기 전에 마을에서 받으므로 보스 지역 방문(추정)으로 본다
    "a1q3": ("1_1_11_2", False),
    "a2q6": ("1_2_6_2", False), "a2q10": ("1_2_2a", False), "a2q4": ("1_2_10", False),
    "a3q1": ("1_3_10_1", True), "a3q4": ("1_3_7", False), "a3q8": ("1_3_13", False),
    "a3q12": ("1_3_17_2", False),
    "a4q1": ("1_4_6_3", False), "a4q2": ("1_4_3_1", True),
    "a5q1b": ("1_5_3", True), "a5q2": ("1_5_4", True), "a5q4": ("1_5_4b", True), "a5q5": ("1_5_3b", False),
    "a6q2": ("2_6_8", True), "a7q2": ("2_7_5_map", False), "a8q1": ("2_8_8", True), "a8q5": ("2_8_5", False),
    "a9q3": ("2_9_3", False), "a10q1": ("2_10_2", True), "a10q2": ("2_10_4", False),
}


def load(lang):
    d = os.path.join(raw, "data", lang)
    return {t: Dat(os.path.join(d, t + ".datc64"), S[t]) for t in TABLES}


KO, EN = load("korean"), load("")

# 클래스 순서는 캐릭터 생성 화면과 같은 Characters 테이블 순서를 쓴다.
ch = KO["characters"]
classes = [{
    "index": i,
    "id": ch.get(i, "Id").rsplit("/", 1)[-1],
    "ko": ch.get(i, "Name"),
    "en": EN["characters"].get(i, "Name"),
} for i in range(ch.n)]

# 처음 시작할 때 받는 젬 (스킬 젬 1 + 보조 젬 1, 예: 머라우더 묵직한 타격 + 무자비 보조).
#   Characters.StartSkillGem / StartSupportGem → SkillGems.BaseItemTypesKey → 젬 ID. 구매 목록에서 뺀다.
def start_gem(row):
    return KO["baseitemtypes"].get(KO["skillgems"].get(row, "BaseItemTypesKey"), "Id") if row is not None else None

for c in classes:
    c["start"] = [g for g in (start_gem(ch.get(c["index"], "StartSkillGem")), start_gem(ch.get(c["index"], "StartSupportGem"))) if g]

# 젬 색상: SkillGems.BaseItemTypesKey → GemColour (1=빨강 2=초록 3=파랑 4=흰색)
gem_info = {}
sg = KO["skillgems"]
for i in range(sg.n):
    gem_info[sg.get(i, "BaseItemTypesKey")] = {
        "color": {1: "r", 2: "g", 3: "b"}.get(sg.get(i, "GemColour"), "w"),
        "support": sg.get(i, "IsSupport"),
    }

bi_ko, bi_en, ic = KO["baseitemtypes"], EN["baseitemtypes"], KO["itemclasses"]


def item(k):
    d = {
        "ko": bi_ko.get(k, "Name"),
        "en": bi_en.get(k, "Name"),
        "class": ic.get(bi_ko.get(k, "ItemClassesKey"), "Name"),
        "w": bi_ko.get(k, "Width"),
        "h": bi_ko.get(k, "Height"),
    }
    if k not in gem_info:
        d["base"] = bi_ko.get(k, "Id")   # 장비 베이스 ID (이름이 같은 베이스 구분·아이콘용, 예: Metadata/Items/Rings/Ring12)
    if k in gem_info:
        d["id"] = bi_ko.get(k, "Id")   # 젬 내부 ID (PoB gemId 와 같음, 예: Metadata/Items/Gems/SkillGemSplitArrow)
        d["gem"] = gem_info[k]["color"]
        d["support"] = gem_info[k]["support"]
    return d


# 보상 행 그룹핑. 컬럼 의미(스키마 미기재, 실측):
#   _1  = 보상 창 표시 순서
#   _16 = 0 공통 / 1 일반 모드 / 2 무자비 모드,  _11 = 무자비 전용
#   Characters 빈 배열 = 모든 클래스
qr = KO["questrewards"]
by_offer = {}
for r in range(qr.n):
    if qr.get(r, "_16") == 2 or qr.get(r, "_11"):
        continue
    by_offer.setdefault(qr.get(r, "RewardOffer"), []).append(r)

qo, q, q_en = KO["questrewardoffers"], KO["quest"], EN["quest"]

# 보상 NPC 와 대화 버튼 이름: NPCTalk.QuestRewardOffersKey 로 역참조.
# 같은 보상이 여러 NPC 변형에 걸려 있으면 해당 액트 폴더(Metadata/NPC/ActN/)를 우선한다.
nt, npcs = KO["npctalk"], KO["npcs"]
talk = {}
for i in range(nt.n):
    o = nt.get(i, "QuestRewardOffersKey")
    if o is None:
        continue
    n = nt.get(i, "NPCKey")
    talk.setdefault(o, []).append((npcs.get(n, "Metadata"), npcs.get(n, "Name"), nt.get(i, "DialogueOption")))
offers = []
for o in range(qo.n):
    oid = qo.get(o, "Id")
    qk = qo.get(o, "QuestKey")
    # 액트 퀘스트(a1q1, a1q5b …)만. 리그 튜토리얼·중국판(_tencent) 제외.
    if qk is None or not re.fullmatch(r"a\d+q\d+b?", oid) or o not in by_offer:
        continue
    act = q.get(qk, "Act")
    if not 1 <= act <= 10:
        continue
    rows = sorted(by_offer[o], key=lambda r: qr.get(r, "_1"))
    per_class = {c["id"]: [] for c in classes}
    for r in rows:
        chars = qr.get(r, "Characters") or [c["index"] for c in classes]
        entry = item(qr.get(r, "Reward"))
        entry["order"] = qr.get(r, "_1")
        entry["level"] = qr.get(r, "RewardLevel")
        if qr.get(r, "RewardStack") > 1:
            entry["stack"] = qr.get(r, "RewardStack")
        for ci in chars:
            per_class[classes[ci]["id"]].append(entry)
    # 한 클래스라도 고를 게 2개 이상일 때만 "선택" 보상이다.
    if max(len(v) for v in per_class.values()) < 2:
        continue
    cands = talk.get(o, [])
    pref = [c for c in cands if f"/Act{act}/" in c[0]] or cands
    offers.append({
        "id": oid,
        "npc": pref[0][1] if pref else None,
        "option": pref[0][2] if pref else None,
        "complete": COMPLETE[oid][0] if oid in COMPLETE else None,
        "sure": COMPLETE[oid][1] if oid in COMPLETE else False,
        "quest": q.get(qk, "Id"),
        "act": act,
        "ko": q.get(qk, "Name"),
        "en": q_en.get(qk, "Name"),
        "rewards": per_class,
    })

offers.sort(key=lambda x: x["act"])  # 안정 정렬: 같은 액트 안에서는 테이블 순서 유지

# 이름이 같은데 베이스가 다른 장비 보상 (쌍보석 반지 3종: 5장 오리아스로 귀환, 8장 도이드리 보상)
#   → 기본 옵션(Implicit) 으로 이름 뒤에 구분을 붙인다. 프리셋·아이콘이 이름으로 보상을 찾기 때문.
#   예) Two-Stone Ring (Fire/Lightning) / 쌍보석 반지 (화염·번개)
ELEMENTS = [("Fire", "화염"), ("Cold", "냉기"), ("Lightning", "번개"), ("Chaos", "카오스")]
mods_path = os.path.join(raw, "data", "mods.datc64")
bases = {}
for o in offers:
    for lst in o["rewards"].values():
        for r in lst:
            if "base" in r:
                bases.setdefault(r["en"], {}).setdefault(r["base"], []).append(r)
dups = {n: v for n, v in bases.items() if len(v) > 1}
if dups:
    mods = Dat(mods_path, S["mods"])
    base_row = {bi_ko.get(k, "Id"): k for k in range(bi_ko.n)}
    for name, per_base in dups.items():
        for base, entries in per_base.items():
            mod_ids = [mods.get(m, "Id") for m in (bi_ko.get(base_row[base], "Implicit_ModsKeys") or [])]
            found = [(e, k) for e, k in ELEMENTS if any(e in m for m in mod_ids)]
            if not found:
                raise SystemExit(f"{name} {base}: 기본 옵션으로 구분할 수 없음 {mod_ids}")
            en_tag, ko_tag = "/".join(e for e, _ in found), "·".join(k for _, k in found)
            for r in {id(x): x for x in entries}.values():
                if "(" not in r["en"]:
                    r["en"] = f'{r["en"]} ({en_tag})'
                    r["ko"] = f'{r["ko"]} ({ko_tag})'

# 액트 지역
wa, wa_en = KO["worldareas"], EN["worldareas"]
areas = {}
for i in range(wa.n):
    aid = wa.get(i, "Id")
    act = wa.get(i, "Act")
    # 액트 지역 ID 는 "1_1_town", "2_6_14" 처럼 숫자_ 로 시작한다.
    if not re.match(r"\d+_", aid) or not 1 <= act <= 10 or wa.get(i, "IsMapArea"):
        continue
    if wa.get(i, "IsLabyrinthArea") or wa.get(i, "IsLabyrinthAirlock"):
        continue
    areas[aid] = {
        "ko": wa.get(i, "Name"),
        "en": wa_en.get(i, "Name"),
        "act": act,
        "level": wa.get(i, "AreaLevel"),
        "town": wa.get(i, "IsTown"),
        "waypoint": wa.get(i, "HasWaypoint"),
        "connections": [wa.get(c, "Id") for c in wa.get(i, "Connections_WorldAreasKeys")],
    }

# 퀘스트로 받는 패시브 스킬 포인트·재설계 포인트. 컬럼 의미(스키마 미기재, 실측):
#   QuestStaticRewards._1 = 패시브 스킬 포인트, _7 = 재설계 포인트 (합계 24 / 20)
qsr = KO["queststaticrewards"]
# 퀘스트가 속한 액트와 실제로 포인트를 받을 수 있게 되는 액트가 다른 퀘스트 → 받을 수 있는 액트로 센다.
#   나아가는 길(1장 퀘스트): 2장에서 길을 연 뒤 1장 마을 베스텔에게 받는다 → 1장 목표에 넣으면 1장 내내 하나가 비어 보인다
POINT_ACT = {"a1q9": 2}
points = []
for i in range(qsr.n):
    qk = qsr.get(i, "QuestKey")
    if qk is None:
        continue
    pas, ref = qsr.get(i, "_1"), qsr.get(i, "_7")
    act = q.get(qk, "Act")
    if not (pas or ref) or not 1 <= act <= 10:
        continue
    act = POINT_ACT.get(q.get(qk, "Id"), act)
    points.append({"id": q.get(qk, "Id"), "act": act, "ko": q.get(qk, "Name"), "en": q_en.get(qk, "Name"),
                   "passive": pas, "refund": ref})
points.sort(key=lambda x: x["act"])

# 모든 젬의 한국어 이름 (PoB 젬 ID → 이름). PoB 불러오기에서 보상으로 못 받는 젬을 한국어로 보여 줄 때 쓴다.
gems = {}
for k, info in gem_info.items():
    gid, name = bi_ko.get(k, "Id"), bi_ko.get(k, "Name")
    if gid and name:
        gems[gid] = {"ko": name, "en": bi_en.get(k, "Name"), "color": info["color"], "support": info["support"]}

# 퀘스트 상점 젬 판매 목록 (위키 vendor_rewards, fetch_vendor.py 로 받은 파일). 없으면 건너뜀.
#   [{gem: 젬 ID, act, npc: 한국어, quest: 퀘스트 ID, questKo, classes: [클래스 ID] 또는 None(모든 클래스)}]
vendor = []
wiki_path = os.path.join(raw, "vendor_wiki.json")
if os.path.exists(wiki_path):
    import html
    gem_by_en = {}
    for gid, g in gems.items():
        if "Royale" not in gid:          # 배틀로얄 모드용 중복 젬 제외
            gem_by_en.setdefault(g["en"], gid)
    npc_ko = {}
    npc_en = EN["npcs"]
    for i in range(npc_en.n):
        npc_ko.setdefault(npc_en.get(i, "Name"), KO["npcs"].get(i, "Name"))
    quest_ko = {q.get(i, "Id"): q.get(i, "Name") for i in range(q.n)}
    missing = set()
    for r in json.load(open(wiki_path, encoding="utf8")):
        name = html.unescape(r["page"])
        gid = gem_by_en.get(name)
        if gid is None:
            missing.add(name)
            continue
        cls = r.get("class ids")
        vendor.append({
            "gem": gid, "act": int(r["act"]),
            "npc": npc_ko.get(r["npc"], r["npc"]), "quest": r["quest id"],
            "questKo": quest_ko.get(r["quest id"], html.unescape(r["quest"] or "")),
            "classes": cls.split(",") if cls else None,
        })
    vendor.sort(key=lambda v: (v["act"], v["gem"]))
    if missing:
        print("vendor: 젬 목록에 없는 이름", sorted(missing))

os.makedirs(out, exist_ok=True)


def dump(name, obj):
    with open(os.path.join(out, name), "w", encoding="utf8") as f:
        json.dump(obj, f, ensure_ascii=False, separators=(",", ":"))
    print(f"{name}: {os.path.getsize(os.path.join(out, name))} bytes")


dump("classes.json", classes)
dump("rewards.json", offers)
dump("areas.json", areas)
dump("points.json", points)
dump("gems.json", gems)
dump("vendor.json", vendor)
