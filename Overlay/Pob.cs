using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Siosa
{
    // PoB(Path of Building) 빌드에서 쓰는 젬
    public class PobGem
    {
        public string Id;          // 젬 내부 ID (Metadata/Items/Gems/…). 변형 젬도 원본 젬 ID
        public string Name;        // PoB 표시 이름 (영문)
        public bool Support;
        public int GroupSize;      // 같은 링크 묶음의 젬 수
        public bool MainGroup;     // 빌드의 메인 스킬 묶음
        public int Order;          // 빌드에 처음 나온 순서
        public int Set;            // 스킬 세트 번호 (PobBuild.Sets 의 인덱스)
        public int Stage;          // 레벨링 단계: 스킬 세트 순서(Act 1, Act 2 …), 한 세트면 젬 없는 이름 묶음("1Lv", "8Lv" …)으로 나눈 구간
    }

    public class PobSet
    {
        public string Title;
        public bool Leveling;      // 이름으로 본 레벨링 세트 추정 (불러오기 창의 기본 체크)
        public int GemCount;
    }

    public class PobBuild
    {
        public string ClassName;   // Marauder, Witch, …
        public string Ascendancy;
        public string Bandit = "None";   // None(모두 처치) / Alira / Kraityn / Oak
        public List<PobSet> Sets = new List<PobSet>();
        public List<PobGem> Gems = new List<PobGem>();   // 같은 젬이 여러 묶음에 있으면 여러 개
        public List<(int set, SkillGroup group)> Groups = new List<(int, SkillGroup)>();
        public List<TreeStage> Trees = new List<TreeStage>();   // PoB 트리(Spec)들, 순서대로
    }

    // PoB 코드 해석과 퀘스트 보상 자동 선택.
    //   코드 = URL 용 Base64( zlib( XML ) ).  링크(pobb.in / pastebin / poe.ninja)는 원본 코드 주소에서 받는다.
    public static class Pob
    {
        // ── 링크 ─────────────────────────────────────────────────

        static readonly Regex PobbIn = new Regex(@"pobb\.in/(?:u/[^/]+/)?([A-Za-z0-9_-]+)", RegexOptions.IgnoreCase);
        static readonly Regex Pastebin = new Regex(@"pastebin\.com/(?:raw/)?([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
        static readonly Regex Ninja = new Regex(@"poe\.ninja/(?:poe1/)?pob/(?:raw/)?([A-Za-z0-9]+)", RegexOptions.IgnoreCase);

        public static bool IsLink(string s) => Regex.IsMatch(s.Trim(), @"^(https?://|www\.|pobb\.in|pastebin\.com|poe\.ninja)", RegexOptions.IgnoreCase);

        // 붙여넣은 링크 → 원본 코드 주소. 모르는 사이트면 null
        public static string RawUrl(string link)
        {
            Match m;
            if ((m = PobbIn.Match(link)).Success) return $"https://pobb.in/{m.Groups[1].Value}/raw";
            if ((m = Pastebin.Match(link)).Success) return $"https://pastebin.com/raw/{m.Groups[1].Value}";
            if ((m = Ninja.Match(link)).Success) return $"https://poe.ninja/poe1/pob/raw/{m.Groups[1].Value}";
            return null;
        }

        // 링크면 받아오고, 코드면 그대로. 사용자가 불러오기를 누를 때만 호출된다.
        public static string Resolve(string input)
        {
            input = (input ?? "").Trim();
            if (!IsLink(input)) return input;
            var url = RawUrl(input) ?? throw new FormatException("지원하는 링크가 아닙니다 (pobb.in, pastebin, poe.ninja).");
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;   // .NET 4.8 은 환경에 따라 꺼져 있다
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "Siosa (PoE leveling overlay)";
            req.Timeout = req.ReadWriteTimeout = 10000;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd().Trim();
        }

        // ── 코드 해석 ────────────────────────────────────────────

        public static PobBuild Parse(string code)
        {
            code = Regex.Replace(code ?? "", @"\s", "").Replace('-', '+').Replace('_', '/');
            if (code.Length < 20) throw new FormatException("PoB 코드가 아닙니다.");
            code += new string('=', (4 - code.Length % 4) % 4);
            byte[] bytes;
            try { bytes = Convert.FromBase64String(code); }
            catch (FormatException) { throw new FormatException("PoB 코드가 아닙니다."); }

            string xml;
            using (var ms = new MemoryStream(bytes, 2, bytes.Length - 2))   // zlib 헤더 2바이트 건너뜀
            using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
            using (var sr = new StreamReader(ds, Encoding.UTF8))
                xml = sr.ReadToEnd();

            var root = XDocument.Parse(xml).Root;
            var buildEl = root.Element("Build") ?? throw new FormatException("PoB 코드에 빌드 정보가 없습니다.");
            var b = new PobBuild { ClassName = (string)buildEl.Attribute("className"), Ascendancy = (string)buildEl.Attribute("ascendClassName"), Bandit = (string)buildEl.Attribute("bandit") ?? "None" };

            // 패시브 트리: <Tree><Spec title="8Lv" nodes="1,2,3"/>… 순서대로 단계가 된다
            var specs = root.Element("Tree")?.Elements("Spec").ToList() ?? new List<XElement>();
            for (int i = 0; i < specs.Count; i++)
            {
                var ids = ((string)specs[i].Attribute("nodes") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => int.TryParse(s, out var v) ? v : -1).Where(v => v >= 0).ToList();
                if (ids.Count == 0) continue;
                string title = ((string)specs[i].Attribute("title") ?? "").Trim();
                var stage = new TreeStage { name = title.Length > 0 ? title : (specs.Count == 1 ? "트리" : $"트리 {i + 1}"), nodes = ids };
                foreach (Match m in Regex.Matches((string)specs[i].Attribute("masteryEffects") ?? "", @"\{(\d+),(\d+)\}"))
                    stage.masteries[m.Groups[1].Value] = int.Parse(m.Groups[2].Value);
                b.Trees.Add(stage);
            }

            var skills = root.Element("Skills");
            if (skills == null) return b;
            // 스킬 세트가 여러 개면(레벨링용 "Act 1", "Act 2" …) 모두 합친다. 옛 형식은 Skills 바로 아래에 Skill.
            var sets = skills.Elements("SkillSet").ToList();
            if (sets.Count == 0) sets.Add(skills);
            string activeSet = (string)skills.Attribute("activeSkillSet");
            int.TryParse((string)buildEl.Attribute("mainSocketGroup"), out int mainGroup);

            int order = 0;
            for (int si = 0; si < sets.Count; si++)
            {
                var set = sets[si];
                string title = (string)set.Attribute("title");
                if (string.IsNullOrWhiteSpace(title)) title = sets.Count == 1 ? "스킬" : $"세트 {si + 1}";
                b.Sets.Add(new PobSet { Title = title, Leveling = LevelingRe.IsMatch(title) && !EndgameRe.IsMatch(title) });
                bool isActive = sets.Count == 1 || (string)set.Attribute("id") == activeSet;
                int groupIndex = 0, section = 0;
                string stage = title;   // 링크 단계 이름: 세트 이름, 구간 구분선이 있으면 그 구간 이름
                foreach (var skill in set.Elements("Skill"))
                {
                    groupIndex++;
                    var gems = skill.Elements("Gem").Where(g => !string.IsNullOrEmpty((string)g.Attribute("gemId"))).ToList();
                    // 젬 없이 이름만 있는 묶음은 구간 구분선 ("8Lv", "31Lv 도서관" …)
                    string label = ((string)skill.Attribute("label") ?? "").Trim();
                    if (gems.Count == 0 && label.Length > 0 && skill.Elements("Gem").All(g => (string)g.Attribute("gemId") == null))
                    {
                        section++;
                        stage = sets.Count == 1 ? label : $"{title} · {label}";
                        continue;
                    }
                    if (gems.Count > 0)
                        b.Groups.Add((si, new SkillGroup
                        {
                            set = stage, label = label,
                            gems = gems.Select(g => (string)g.Attribute("gemId")).ToList(),
                        }));
                    b.Sets[si].GemCount += gems.Count;
                    foreach (var g in gems)
                    {
                        string id = (string)g.Attribute("gemId");
                        b.Gems.Add(new PobGem
                        {
                            Id = id,
                            Name = (string)g.Attribute("nameSpec"),
                            Support = id.Contains("/SupportGem") || ((string)g.Attribute("skillId") ?? "").StartsWith("Support"),
                            GroupSize = gems.Count,
                            MainGroup = isActive && groupIndex == mainGroup,
                            Order = order++,
                            Set = si,
                            Stage = si * 1000 + section,
                        });
                    }
                }
            }
            // 레벨링으로 보이는 세트가 하나도 없으면(이름 없는 한 세트 등) 전부 레벨링으로 본다
            if (!b.Sets.Any(x => x.Leveling)) foreach (var x in b.Sets) x.Leveling = true;
            return b;
        }

        static readonly Regex LevelingRe = new Regex(@"act|액트|\d+\s*장|lv|lvl|level|레벨|campaign|캠페인|leveling|\d+\s*[-~]", RegexOptions.IgnoreCase);
        static readonly Regex EndgameRe = new Regex(@"map|맵|endgame|엔드|boss|보스|uber|atlas|아틀라스|swap|스왑", RegexOptions.IgnoreCase);

        // ── 보상 자동 선택 ───────────────────────────────────────

        public class Selection
        {
            public ClassInfo Class;
            public string Bandit = "None";                                                  // 2장 도적 선택
            public Dictionary<string, string> Picks = new Dictionary<string, string>();   // 젬 보상 퀘스트만
            public int GemOffers;                                                          // 이 클래스의 젬 보상 퀘스트 수
            public List<string> NotFromRewards = new List<string>();                       // 보상으로 받지 않는 빌드 젬 (한국어, 없으면 영문)
            public List<string> Buy = new List<string>();                                  // 구매 목록 (보상으로 받지 않는 젬 ID, 빌드 순서)
            public List<LinkStage> Links = new List<LinkStage>();                          // 고른 세트의 스킬 묶음 (단계별)
            public List<TreeStage> Trees = new List<TreeStage>();                          // 패시브 트리 단계 (스킬 세트 선택과 상관없이 전부)
        }

        // 퀘스트마다 빌드에 있는 젬 중 하나를 고른다. 여러 개면
        //   빌드에서 더 일찍 쓰는 젬(레벨링 단계) > 메인 스킬 묶음 > 더 큰 링크 묶음 > 스킬 젬(보조 젬보다) > 보상 창 순서
        //   (단계를 먼저 보지 않으면 엔드게임 세트의 큰 링크 젬이 뽑힌다: 예) 1장 보상에 화염 벽 대신 맵용 서리 폭탄)
        // sets: 쓸 스킬 세트 번호 (null = 전부)
        public static Selection Select(GameData data, PobBuild build, ICollection<int> sets = null)
        {
            if (sets != null)
            {
                var keep = new HashSet<int>(sets);
                build = new PobBuild
                {
                    ClassName = build.ClassName, Ascendancy = build.Ascendancy, Bandit = build.Bandit, Sets = build.Sets, Trees = build.Trees,
                    Gems = build.Gems.Where(g => keep.Contains(g.Set)).ToList(),
                    Groups = build.Groups.Where(g => keep.Contains(g.set)).ToList(),
                };
            }
            var cls = data.Classes.FirstOrDefault(c => string.Equals(c.en, build.ClassName, StringComparison.OrdinalIgnoreCase))
                      ?? throw new FormatException($"알 수 없는 클래스: {build.ClassName}");
            var sel = new Selection { Class = cls, Bandit = Profile.Bandits.ContainsKey(build.Bandit ?? "") ? build.Bandit : "None" };

            // 젬마다 가장 좋은 점수 (같은 젬이 여러 묶음에 있으면 가장 중요한 쪽)
            var score = new Dictionary<string, int>();
            var stage = new Dictionary<string, int>();
            foreach (var g in build.Gems)
            {
                int s = (g.MainGroup ? 1000 : 0) + g.GroupSize * 10 + (g.Support ? 0 : 5);
                if (!score.TryGetValue(g.Id, out var old) || s > old) score[g.Id] = s;
                if (!stage.TryGetValue(g.Id, out var st) || g.Stage < st) stage[g.Id] = g.Stage;
            }

            foreach (var o in data.Offers)
            {
                if (!o.rewards.TryGetValue(cls.id, out var list) || list.Count == 0 || list.All(r => r.gem == null)) continue;
                sel.GemOffers++;
                Reward best = null; int bestStage = int.MaxValue, bestScore = -1;
                foreach (var r in list)
                {
                    if (r.id == null || !score.TryGetValue(r.id, out var s)) continue;
                    int st = stage[r.id];
                    if (st < bestStage || (st == bestStage && s > bestScore)) { best = r; bestStage = st; bestScore = s; }
                }
                if (best != null) sel.Picks[o.id] = best.en;
            }

            var picked = new HashSet<string>(sel.Picks.Select(p => data.Offer(p.Key).rewards[cls.id].First(r => r.en == p.Value).id));
            // 처음 시작할 때 받는 젬(예: 머라우더 묵직한 타격 + 무자비 보조)도 이미 있는 젬
            picked.UnionWith(cls.start ?? new List<string>());
            var notPicked = build.Gems.Where(g => !picked.Contains(g.Id)).OrderBy(g => g.Order).ToList();
            sel.NotFromRewards = notPicked.Select(g => data.Gems.TryGetValue(g.Id, out var gi) ? gi.ko : g.Name).Distinct().ToList();
            sel.Buy = notPicked.Select(g => g.Id).Distinct().ToList();
            sel.Links = LinkStage.FromGroups(build.Groups.Select(g => g.group));
            sel.Trees = build.Trees;
            return sel;
        }
    }
}
