using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace Siosa
{
    static class SafeFile
    {
        // path.tmp 에 다 쓴 뒤 원래 파일과 바꾼다. 쓰는 도중 PC 가 꺼지거나 프로그램이 죽어도 원래 파일은 온전하다.
        // (예전: 바로 덮어써서 중간에 끊기면 파일이 깨지고, 다음 실행 때 빈 기록으로 조용히 시작했다)
        public static void WriteAllText(string path, string text)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, text, Encoding.UTF8);
            if (File.Exists(path))
            {
                try { File.Replace(tmp, path, null); return; }
                catch (IOException) { }                     // 일부 드라이브(네트워크 등)는 Replace 를 못 한다
                catch (PlatformNotSupportedException) { }
                File.Copy(tmp, path, true);
                File.Delete(tmp);
            }
            else File.Move(tmp, path);
        }
    }

    // 사용자가 고른 보상 프리셋. picks 는 보상 ID → 보상 영문 이름.
    public class Profile
    {
        public int version { get; set; } = 1;
        public string id { get; set; } = "";       // 프리셋 ID: 진행 기록(progress\<id>.json)을 프리셋마다 따로 둔다
        public string name { get; set; } = "";
        public string classId { get; set; } = "";
        public Dictionary<string, string> picks { get; set; } = new Dictionary<string, string>();
        public List<string> buy { get; set; } = new List<string>();        // 구매할 젬 ID (PoB 또는 직접 추가, 순서 유지)
        public List<string> buyOff { get; set; } = new List<string>();     // 구매 목록에서 체크 해제한 젬
        public List<SkillGroup> groups { get; set; } = new List<SkillGroup>();   // (예전 형식) PoB 스킬 묶음. 열 때 links 로 옮긴다
        public List<LinkStage> links { get; set; } = new List<LinkStage>();     // 링크: 단계(Act 1, 8Lv …)별 묶음. 직접 수정 가능
        public int linkStage { get; set; }                                       // 오버레이에 보여 줄 단계
        public List<TreeStage> trees { get; set; } = new List<TreeStage>();    // 패시브 트리 단계 (PoB 의 트리 여러 개: 8Lv, Act 3 …)
        public int treeStage { get; set; }                                       // 오버레이에 보여 줄 트리 단계
        public string bandit { get; set; } = "None";                             // 2장 도적: None(모두 처치) / Alira / Kraityn / Oak (PoB 값)
        // 정규식 탭: 직접 넣은 식, 고른 자주 쓰는 정규식(data/regex.json 의 id). 구매 젬은 따로 복사(Ctrl+Shift+G)
        //   (예전 regexBuy: 구매 젬을 같이 넣을지. 따로 복사하면서 없앰 — 프리셋 파일에 남아 있어도 무시)
        public string regexCustom { get; set; } = "";
        public List<string> regexPresets { get; set; } = new List<string>();

        // 도적 선택지 (PoB 값 → 표시 이름). 모두 처치하면 2장 '도적 처리하기' 패시브 +1, 도우면 그 포인트가 없다
        public static readonly Dictionary<string, string> Bandits = new Dictionary<string, string>
        {
            ["None"] = "모두 처치", ["Alira"] = "알리라 돕기", ["Kraityn"] = "크레이튼 돕기", ["Oak"] = "오크 돕기",
        };
        public static bool KillsAllBandits(Profile p) => p == null || string.IsNullOrEmpty(p.bandit) || p.bandit == "None";

        static readonly JavaScriptSerializer Js = new JavaScriptSerializer();

        public string ToJson() => Js.Serialize(this);
        public static Profile FromJson(string json) => Js.Deserialize<Profile>(json);

        public void Save(string path) => SafeFile.WriteAllText(path, ToJson());
        public static Profile Load(string path) => FromJson(File.ReadAllText(path, Encoding.UTF8));

        // 진행 기록에 쓰는 ID. 예전 프리셋 파일에는 ID 가 없어서 파일 이름으로 정한다 (SetupForm.Open 과 같게)
        public static string IdOf(Profile p, string path) => string.IsNullOrEmpty(p.id) ? "file-" + Path.GetFileNameWithoutExtension(path) : p.id;
    }

    // PoB 스킬 묶음 하나 (같이 링크된 젬)
    public class SkillGroup
    {
        public string set { get; set; } = "";      // 단계 이름: PoB 스킬 세트(Act 1 …) 또는 한 세트 안의 구간(8Lv …)
        public string label { get; set; } = "";
        public List<string> gems { get; set; } = new List<string>();   // 젬 ID
    }

    // 패시브 트리 단계 하나 (PoB 트리 하나 = 찍은 노드 번호들)
    public class TreeStage
    {
        public string name { get; set; } = "";
        public List<int> nodes { get; set; } = new List<int>();
        public Dictionary<string, int> masteries { get; set; } = new Dictionary<string, int>();   // 마스터리 노드 번호 → 고른 효과 번호
    }

    // 링크 단계 하나 (Act 1, 8Lv …)
    public class LinkStage
    {
        public string name { get; set; } = "";
        public List<SkillGroup> groups { get; set; } = new List<SkillGroup>();

        // 묶음 목록(단계 이름이 붙은)을 단계별로 나눈다. 순서 유지
        public static List<LinkStage> FromGroups(IEnumerable<SkillGroup> groups)
        {
            var stages = new List<LinkStage>();
            foreach (var g in groups)
            {
                if (stages.Count == 0 || stages[stages.Count - 1].name != g.set)
                    stages.Add(new LinkStage { name = g.set });
                stages[stages.Count - 1].groups.Add(g);
            }
            return stages;
        }
    }

    // 프로그램 설정 (Siosa 폴더의 settings.json)
    public class Settings
    {
        public string logPath { get; set; } = "";
        public int overlayX { get; set; } = -1;
        public int overlayY { get; set; } = -1;
        public string lastProfile { get; set; } = "";
        public string lastSession { get; set; } = "";   // (사용 안 함) 예전 버전의 마지막 프리셋. 실행 시 비운다
        public bool showItems { get; set; }              // 설정창에 장비·플라스크 보상도 표시
        public bool showTips { get; set; } = true;       // 오버레이에 지역 팁 표시 (단축키로 켜고 끔)
        public bool showTree { get; set; }               // 패시브 트리 창 표시 (단축키로 켜고 끔)
        public int treeX { get; set; } = -1;             // 패시브 트리 창 위치 (-1 = 기본 자리)
        public int treeY { get; set; } = -1;
        public int treeSize { get; set; } = 240;         // 패시브 트리 그림 높이 (+/- 단축키로 조절)
        public Dictionary<string, int> hotkeys { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, string> tips { get; set; } = new Dictionary<string, string>();
        // (예전 regexOneQuote: 정규식을 따옴표 하나로 묶을지. 항상 하나로 묶으면서 없앰 — 설정 파일에 남아 있어도 무시)

        // (예전 rewardOffsetX/Y: Ctrl+Shift+C 로 저장하던 보상 칸 위치 조정값. 기능을 없애면서 읽지 않는다 — 설정 파일에 남아 있어도 무시)

        static readonly JavaScriptSerializer Js = new JavaScriptSerializer();

        public static Settings Load(string path)
        {
            try { return File.Exists(path) ? Js.Deserialize<Settings>(File.ReadAllText(path, Encoding.UTF8)) : new Settings(); }
            catch { return new Settings(); }
        }

        public void Save(string path) => SafeFile.WriteAllText(path, Js.Serialize(this));
    }

    // 현재 캐릭터 진행 상황. 프로그램을 껐다 켜도 로그의 이어 읽을 위치(offset)부터 다시 읽어 복원한다.
    public class Progress
    {
        public string logPath { get; set; } = "";
        public long offset { get; set; }                           // 로그에서 다음에 읽을 바이트 위치
        public string charStart { get; set; } = "";                // 새 캐릭터 시작 시각 (로그 기준)
        public List<string> visited { get; set; } = new List<string>();
        public List<string> claimed { get; set; } = new List<string>();   // 받았다고 표시한 보상 ID
        public string claimedFor { get; set; } = "";                       // claimed 가 어느 캐릭터(charStart) 것인지
        public string currentArea { get; set; }                    // 마지막으로 들어간 지역 (다시 켰을 때 바로 표시)
        public int currentLevel { get; set; }                      // 그 지역의 레벨
        public int lastAct { get; set; }                           // 마지막으로 있었던 액트 (은신처 등 액트 밖에서 쓰는 액트)
        public string charName { get; set; }                       // 이 프리셋의 캐릭터 (새 캐릭터 시작 후 처음 레벨업한 이름)
        public bool foreign { get; set; }                          // 다른 캐릭터가 플레이 중으로 보임 → 기록을 멈춤
        public int charLevel { get; set; }
        public int[] passive { get; set; } = new int[11];          // 액트별로 받은 패시브 스킬 포인트 (1~10)
        public int[] refund { get; set; } = new int[11];           // 액트별로 받은 재설계 포인트

        static readonly JavaScriptSerializer Js = new JavaScriptSerializer();

        public static Progress Load(string path)
        {
            try { return File.Exists(path) ? Js.Deserialize<Progress>(File.ReadAllText(path, Encoding.UTF8)) : new Progress(); }
            catch { return new Progress(); }
        }

        public void Save(string path) => SafeFile.WriteAllText(path, Js.Serialize(this));
    }
}
