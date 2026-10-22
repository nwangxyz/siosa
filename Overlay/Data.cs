using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace Siosa
{
    // data/*.json 구조 (Extractor/build_data.py 가 생성). 속성 이름은 JSON 키와 같아야 한다.
    public class ClassInfo
    {
        public int index { get; set; }
        public string id { get; set; }
        public string ko { get; set; }
        public string en { get; set; }
        public List<string> start { get; set; } = new List<string>();   // 처음 시작할 때 받는 젬 ID (스킬 1 + 보조 1)
        public override string ToString() => ko;
    }

    public class Reward
    {
        public string ko { get; set; }
        public string en { get; set; }
        public string @class { get; set; }
        public int w { get; set; }
        public int h { get; set; }
        public string id { get; set; }       // 젬 내부 ID (PoB gemId 와 같음). 젬만
        public string gem { get; set; }      // r/g/b/w, 젬이 아니면 null
        public bool support { get; set; }
        public int order { get; set; }       // 보상 창 표시 순서
        public int level { get; set; }
        public int stack { get; set; }
    }

    public class Offer
    {
        public string id { get; set; }
        public string npc { get; set; }
        public string option { get; set; }   // NPC 대화 버튼 이름
        public string complete { get; set; } // 이 지역에 들어가면 퀘스트 완료로 본다 (null = 자동 판정 없음)
        public bool sure { get; set; }       // true: 확실한 완료 신호 / false: 목표 지역 방문(추정)
        public string quest { get; set; }
        public int act { get; set; }
        public string ko { get; set; }
        public string en { get; set; }
        public Dictionary<string, List<Reward>> rewards { get; set; }
    }

    public class Area
    {
        public string ko { get; set; }
        public string en { get; set; }
        public int act { get; set; }
        public int level { get; set; }
        public bool town { get; set; }
        public bool waypoint { get; set; }
        public List<string> connections { get; set; }
    }

    // 패시브 스킬 포인트·재설계 포인트를 주는 퀘스트 (points.json)
    public class PointQuest
    {
        public string id { get; set; }
        public int act { get; set; }
        public string ko { get; set; }
        public string en { get; set; }
        public int passive { get; set; }
        public int refund { get; set; }
    }

    // 모든 젬 (gems.json, 키 = 젬 내부 ID)
    public class GemInfo
    {
        public string ko { get; set; }
        public string en { get; set; }
        public string color { get; set; }
        public bool support { get; set; }
    }

    // 퀘스트를 깨면 상점에서 파는 젬 (vendor.json, PoE 위키 vendor_rewards)
    public class VendorEntry
    {
        public string gem { get; set; }          // 젬 ID
        public int act { get; set; }
        public string npc { get; set; }          // 한국어 NPC 이름
        public string quest { get; set; }        // 판매를 여는 퀘스트 ID
        public string questKo { get; set; }
        public List<string> classes { get; set; }   // null = 모든 클래스 (시오사, 릴리 로스)
    }

    public class GameData
    {
        public List<ClassInfo> Classes;
        public List<Offer> Offers;
        public Dictionary<string, Area> Areas;
        public RewardIcons Icons;
        public List<PointQuest> Points;
        public Dictionary<string, string> Tips;
        // 정규식 탭의 '자주 쓰는 정규식' (data/regex.json, 직접 고쳐 쓰는 파일). 없거나 읽지 못하면 빈 목록
        public List<RegexSearch.Preset> RegexPresets = new List<RegexSearch.Preset>();
        public TreeData Tree;                     // 패시브 트리 좌표 (data/tree.json, 없으면 null)   // 지역 ID → 기본 한 줄 팁 (data/tips.json, 직접 쓴 내용)
        public Dictionary<string, GemInfo> Gems;
        public List<VendorEntry> Vendor;

        // 이 클래스가 이 젬을 가장 먼저 살 수 있는 곳 (없으면 null = 상점에서 판매하지 않음)
        public VendorEntry WhereToBuy(string gemId, string classId)
        {
            // 젬별로 묶어 두고 찾는다 (구매 탭·검색에서 젬마다 부르므로 1300줄을 매번 훑지 않게)
            if (vendorByGem == null)
                vendorByGem = Vendor.GroupBy(v => v.gem).ToDictionary(g => g.Key, g => g.OrderBy(v => v.act).ToList());
            return vendorByGem.TryGetValue(gemId, out var list) ? list.FirstOrDefault(v => v.classes == null || v.classes.Contains(classId)) : null;
        }
        Dictionary<string, List<VendorEntry>> vendorByGem;

        public string GemName(string gemId) => Gems.TryGetValue(gemId, out var g) ? g.ko : gemId;

        // 사람이 직접 고치는 데이터(tips.json)는 "// 지역 이름" 주석 줄을 둔다. JSON 에는 주석이 없으니 읽기 전에 그 줄만 뺀다
        static string StripLineComments(string text) =>
            string.Join("\n", text.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        // 사용자가 메모장으로 고치는 파일이라, 잘못 써도 프로그램이 켜지도록 읽기 실패는 빈 목록으로 (debug\siosa.log 에 남김).
        //   id 가 없으면 이름을 id 로, 이름·식이 빈 항목·같은 id 는 뺀다. "//" 로 시작하는 줄은 주석
        //   options(묶음)가 있으면 pattern 에 {} 가 있어야 한다
        static List<RegexSearch.Preset> LoadRegex(JavaScriptSerializer js, string dataDir)
        {
            if (!DataExists(dataDir, "regex.json")) return new List<RegexSearch.Preset>();
            try
            {
                var list = js.Deserialize<List<RegexSearch.Preset>>(StripLineComments(ReadData(dataDir, "regex.json"))) ?? new List<RegexSearch.Preset>();
                var seen = new HashSet<string>();
                var ok = new List<RegexSearch.Preset>();
                foreach (var x in list)
                {
                    if (x == null || string.IsNullOrWhiteSpace(x.name) || string.IsNullOrWhiteSpace(x.pattern)) continue;
                    if (string.IsNullOrWhiteSpace(x.id)) x.id = x.name;
                    if (x.IsGroup)
                    {
                        x.options = x.options.Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o.Trim()).Distinct().ToList();
                        if (!x.pattern.Contains("{}")) { DebugLog.Write($"regex.json: '{x.name}' 은 options 가 있는데 pattern 에 {{}} 가 없어 건너뜀"); continue; }
                    }
                    if (seen.Add(x.id)) ok.Add(x);
                }
                return ok;
            }
            catch (Exception ex)
            {
                DebugLog.Write("regex.json 읽기 실패: " + ex.Message);
                return new List<RegexSearch.Preset>();
            }
        }

        // 데이터 파일: dataDir 에 있으면 그것, 없으면 exe 안에 넣어 둔 것 (배포는 exe 하나)
        public static bool DataExists(string dataDir, string name) =>
            (dataDir != null && File.Exists(Path.Combine(dataDir, name))) || typeof(GameData).Assembly.GetManifestResourceInfo("Siosa.data." + name) != null;

        public static Stream OpenData(string dataDir, string name)
        {
            if (dataDir != null && File.Exists(Path.Combine(dataDir, name))) return File.OpenRead(Path.Combine(dataDir, name));
            return typeof(GameData).Assembly.GetManifestResourceStream("Siosa.data." + name) ?? throw new FileNotFoundException("데이터 파일이 없습니다: " + name);
        }

        public static string ReadData(string dataDir, string name)
        {
            using (var s = OpenData(dataDir, name))
            using (var r = new StreamReader(s, System.Text.Encoding.UTF8, true))
                return r.ReadToEnd();
        }

        public static GameData Load(string dataDir)
        {
            var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            T Read<T>(string name) => js.Deserialize<T>(ReadData(dataDir, name));
            var offers = Read<List<Offer>>("rewards.json");
            // 보상 창은 테이블 순서(_1)를 그대로 쓰지 않고 키 큰 것 → 넓은 것 순으로 다시 늘어놓는다 (같으면 테이블 순서).
            // 실측: 9장 태풍의 칼 (활 2x4, 펜싱 검 1x4, 화살통 2x3, 글라디우스 2x3 / 단검 1x3, 버클러 2x2).
            // 젬·플라스크처럼 크기가 같은 보상은 순서가 그대로다.
            foreach (var o in offers)
                foreach (var cls in o.rewards.Keys.ToList())
                    o.rewards[cls] = o.rewards[cls].Select((r, i) => (r, i)).OrderByDescending(t => t.r.h).ThenByDescending(t => t.r.w).ThenBy(t => t.i).Select(t => t.r).ToList();
            return new GameData
            {
                Classes = Read<List<ClassInfo>>("classes.json"),
                Offers = offers,
                Areas = Read<Dictionary<string, Area>>("areas.json"),
                Icons = RewardIcons.Load(dataDir),
                Vendor = DataExists(dataDir, "vendor.json") ? Read<List<VendorEntry>>("vendor.json") : new List<VendorEntry>(),
                Gems = DataExists(dataDir, "gems.json") ? Read<Dictionary<string, GemInfo>>("gems.json") : new Dictionary<string, GemInfo>(),
                Points = DataExists(dataDir, "points.json") ? Read<List<PointQuest>>("points.json") : new List<PointQuest>(),
                Tips = DataExists(dataDir, "tips.json")
                    ? js.Deserialize<Dictionary<string, string>>(StripLineComments(ReadData(dataDir, "tips.json")))
                    : new Dictionary<string, string>(),
                Tree = DataExists(dataDir, "tree.json") ? TreeData.From(Read<TreeFile>("tree.json")) : null,
                RegexPresets = LoadRegex(js, dataDir),
            };
        }

        public Offer Offer(string id) => Offers.FirstOrDefault(o => o.id == id);

        // 지역 팁: 사용자가 고친 것(settings.tips, 빈 문자열 = 숨김)이 있으면 그것, 없으면 기본 팁
        public string TipFor(Settings s, string areaId)
        {
            if (areaId == null) return null;
            if (s?.tips != null && s.tips.TryGetValue(areaId, out var mine)) return mine;
            return Tips != null && Tips.TryGetValue(areaId, out var tip) ? tip : null;
        }

        // 프리셋에 저장된 영문 이름으로 보상 찾기.
        //   예전 프리셋의 "Two-Stone Ring" 은 이름을 나누기 전 값이라, 그때 강조되던 첫 번째(화염·번개)로 본다.
        public static Reward FindReward(IList<Reward> list, string en) =>
            list.FirstOrDefault(x => x.en == en) ?? list.FirstOrDefault(x => x.en.StartsWith(en + " ("));
    }

    // data/tree.json (Extractor/build_tree.py): 본 트리 노드 [번호, x, y, 종류] 와 연결 [i, j]
    public class TreeFile
    {
        public string version { get; set; }
        public List<List<int>> nodes { get; set; }
        public List<List<int>> edges { get; set; }
        public Dictionary<string, TreeFile> asc { get; set; }   // 전직 이름 → 그 전직 트리
        public Dictionary<string, MasteryInfo> mastery { get; set; }   // 마스터리 노드 번호 → 이름·효과 문구
    }

    public class MasteryInfo
    {
        public string name { get; set; }
        public Dictionary<string, string> effects { get; set; }   // 효과 번호 → 문구
    }

    public class TreeData
    {
        public string Version;
        public int[] Id, X, Y, Kind;              // 종류: 0 일반 · 1 주요 · 2 키스톤 · 3 주얼 · 4 클래스/전직 시작 · 5 마스터리
        public Dictionary<string, TreeData> Asc = new Dictionary<string, TreeData>();   // 전직 트리 (본 트리와 따로 그린다)
        public Dictionary<string, MasteryInfo> Mastery = new Dictionary<string, MasteryInfo>();

        // 찍은 마스터리: 이름("화염 숙련")과 효과 줄들 (여러 효과면 줄마다 하나). 모르면 null
        public (string name, string[] effects)? MasteryParts(int node, int effect)
        {
            if (!Mastery.TryGetValue(node.ToString(), out var m) || m.effects == null) return null;
            if (!m.effects.TryGetValue(effect.ToString(), out var e)) return null;
            return (m.name, e.Split(new[] { " / " }, StringSplitOptions.RemoveEmptyEntries));
        }
        public (int a, int b)[] Edges;            // 노드 배열 번호
        public Dictionary<int, int> Index = new Dictionary<int, int>();
        public int MinX, MinY, MaxX, MaxY;

        public static TreeData From(TreeFile f)
        {
            int n = f.nodes.Count;
            var t = new TreeData { Version = f.version, Id = new int[n], X = new int[n], Y = new int[n], Kind = new int[n] };
            for (int i = 0; i < n; i++)
            {
                var v = f.nodes[i];
                t.Id[i] = v[0]; t.X[i] = v[1]; t.Y[i] = v[2]; t.Kind[i] = v[3];
                t.Index[v[0]] = i;
            }
            t.Edges = f.edges.Select(e => (e[0], e[1])).ToArray();
            t.MinX = t.X.Min(); t.MaxX = t.X.Max(); t.MinY = t.Y.Min(); t.MaxY = t.Y.Max();
            if (f.asc != null)
                foreach (var kv in f.asc) if (kv.Value?.nodes?.Count > 0) t.Asc[kv.Key] = From(kv.Value);
            if (f.mastery != null) t.Mastery = f.mastery;
            return t;
        }
    }
}
