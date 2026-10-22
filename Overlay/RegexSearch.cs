using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Siosa
{
    // 상점 검색칸에 붙여 넣을 정규식 (설정창 '정규식' 탭, 게임 중 단축키 → 클립보드 → 게임에서 Ctrl+V).
    //   카테고리(직접 입력 / 이 액트에서 살 젬 / 자주 쓰는 정규식)를 | 로 이어 전체를 따옴표 하나로 묶는다:
    //   "직접 입력|젬1|젬2|자주 쓰는 것1|것2"
    //   게임 검색칸 길이 제한은 250자 (영문판 기준. 카카오 클라이언트도 같은지는 게임에서 확인)
    //   게임 검색은 공백을 AND 로 나눈다(따옴표 안에서도). 젬 이름·자주 쓰는 정규식의 공백은 . (아무 글자 하나)로 바꾼다
    //   (예: "겨울 보주" → 겨울.보주). 직접 입력한 식은 사용자가 쓴 그대로 둔다
    public static class RegexSearch
    {
        public const int MaxLength = 250;

        // 자주 쓰는 정규식 하나 (data/regex.json 의 항목)
        //   묶음: options 가 있으면 고른 것들을 | 로 이어 pattern 의 {} 자리에 넣는다
        //   예) pattern "^({}).*추가", options [물리, 화염, 냉기] → 물리·화염을 고르면 ^(물리|화염).*추가
        //   고른 상태는 프리셋에 "id:선택지" 로 저장 (묶음이 아니면 "id")
        public class Preset
        {
            public string id { get; set; }
            public string name { get; set; }
            public string pattern { get; set; }
            public string note { get; set; }
            public List<string> options { get; set; }

            public bool IsGroup => options != null && options.Count > 0;
            public string Key(string option) => id + ":" + option;
        }

        // 이 항목이 만드는 식 (고른 것이 없으면 null)
        public static string PatternFor(Preset x, ICollection<string> on)
        {
            if (!x.IsGroup) return on.Contains(x.id) ? x.pattern : null;
            var sel = x.options.Where(o => on.Contains(x.Key(o))).ToList();
            return sel.Count == 0 ? null : x.pattern.Replace("{}", string.Join("|", sel));
        }

        // 프리셋에 저장할 수 있는 키 (지금 regex.json 에 있는 항목·선택지만)
        public static IEnumerable<string> Keys(IEnumerable<Preset> list) =>
            list.SelectMany(x => x.IsGroup ? x.options.Select(x.Key) : new[] { x.id });

        // 이 액트 상점에서 살 젬 (오버레이의 '구매' 줄과 같은 기준):
        //   구매 목록 중 체크를 끄지 않았고, 퀘스트 보상·시작 젬으로 받지 않으며, 이 액트에서 처음 살 수 있는 젬
        public static List<(string id, VendorEntry where)> BuyGems(GameData data, Profile p, int act)
        {
            var result = new List<(string, VendorEntry)>();
            if (p?.buy == null || act < 1 || act > 10) return result;
            var fromReward = new HashSet<string>();
            foreach (var o in data.Offers)
            {
                if (p.picks == null || !p.picks.TryGetValue(o.id, out var en)) continue;
                if (!o.rewards.TryGetValue(p.classId ?? "", out var list)) continue;
                var r = GameData.FindReward(list, en);
                if (r?.id != null) fromReward.Add(r.id);
            }
            var cls = data.Classes.FirstOrDefault(c => c.id == p.classId);
            if (cls?.start != null) fromReward.UnionWith(cls.start);
            var off = new HashSet<string>(p.buyOff ?? new List<string>());
            foreach (var id in p.buy)
            {
                if (fromReward.Contains(id) || off.Contains(id)) continue;
                var v = data.WhereToBuy(id, p.classId);
                if (v != null && v.act == act) result.Add((id, v));
            }
            return result;
        }

        // 정규식 특수 문자를 글자 그대로 찾도록
        public static string Escape(string s)
        {
            var sb = new StringBuilder();
            foreach (var ch in s)
            {
                if ("\\.^$*+?()[]{}|\"".IndexOf(ch) >= 0) sb.Append('\\');
                sb.Append(ch);
            }
            return sb.ToString();
        }

        // 공백 → . (게임 검색에서 공백은 AND)
        public static string NoSpace(string s) => s.Replace(' ', '.');

        // 사용자가 직접 넣은 식: 양 끝 따옴표는 Siosa 가 붙이므로 떼어 낸다
        static string Unquote(string s)
        {
            s = (s ?? "").Trim();
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"') s = s.Substring(1, s.Length - 2).Trim();
            return s;
        }

        // 직접 입력 + 자주 쓰는 정규식 (Ctrl+Shift+F). 빈 카테고리는 빠진다.
        //   구매 젬은 섞지 않는다: 옵션 정규식(이동 속도 등)과 같이 넣으면 젬이 아닌 아이템까지 강조돼서 따로 복사한다 (BuildBuy)
        public static List<(string label, string part)> Parts(GameData data, Profile p)
        {
            var parts = new List<(string, string)>();
            var custom = Unquote(p?.regexCustom);
            if (custom.Length > 0) parts.Add(("직접 입력", custom));
            var on = new HashSet<string>(p?.regexPresets ?? new List<string>());
            var pre = data.RegexPresets.Select(x => PatternFor(x, on)).Where(x => x != null).Select(NoSpace).ToList();
            if (pre.Count > 0) parts.Add(("자주 쓰는 정규식", string.Join("|", pre)));
            return parts;
        }

        // 클립보드에 넣을 최종 문자열: 전체를 따옴표 하나로 묶고 카테고리는 | 로 잇는다 ("직접 입력|젬1|젬2|자주 쓰는 것")
        //   (카테고리마다 따옴표로 묶는 "a"|"b" 형식은 게임 검색에서 동작하지 않았다)
        public static string Build(GameData data, Profile p)
        {
            var parts = Parts(data, p);
            return parts.Count == 0 ? "" : "\"" + string.Join("|", parts.Select(x => x.part)) + "\"";
        }

        // 이 액트 상점에서 살 젬만 (Ctrl+Shift+G): "젬1|젬2" (젬 이름의 공백은 .)
        public static string BuildBuy(GameData data, Profile p, int act, out int count)
        {
            var names = BuyGems(data, p, act).Select(g => NoSpace(Escape(data.GemName(g.id)))).Distinct().ToList();
            count = names.Count;
            return names.Count == 0 ? "" : "\"" + string.Join("|", names) + "\"";
        }
    }
}
