using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace Siosa
{
    // 전역 단축키 목록과 설정값. 키 값은 Keys(수정키 포함, 예: Ctrl+Shift+R)를 정수로 settings.hotkeys 에 저장한다.
    // 게임 채팅 입력과 겹치지 않도록 Ctrl 또는 Alt 가 들어간 조합만 쓴다.
    public static class Hotkeys
    {
        // R(수동 강조)·C(보상 칸 위치 확인)는 없앴다: 자동 강조와 같은 위치 계산이라 자동이 못 잡는 해상도에서는 같이 어긋나고,
        // C 는 위치를 옮기기만 해서 비율이 다른 경우를 못 맞추며 게임이 앞에 있어 방향키도 받지 못했다
        public const string Move = "move", Hide = "hide",
            Links = "links", NextStage = "nextStage", PrevStage = "prevStage", Debug = "debug", Tips = "tips",
            Tree = "tree", PrevTree = "prevTree", NextTree = "nextTree", TreeBigger = "treeBigger", TreeSmaller = "treeSmaller",
            Regex = "regex", BuyRegex = "buyRegex";

        public class Action
        {
            public string Id, Name;
            public Keys Default;
            public int HotkeyId;   // RegisterHotKey 번호
        }

        const Keys CS = Keys.Control | Keys.Shift;

        // 개발용 기능(D 화면 저장)을 켤지. Siosa.exe --dev 로 실행했을 때만 (일반 사용자에게는 키·안내·변경 창 모두 없음)
        public static bool DevMode;

        // 지금 쓰는 단축키 (등록·설정창·안내 문구). 개발 모드가 아니면 D 를 뺀다
        public static Action[] All => DevMode ? Every : Every.Where(a => a.Id != Debug).ToArray();

        // 설정창·안내 문구에 나오는 순서 (개발용 포함 전체)
        public static readonly Action[] Every =
        {
            new Action { Id = Move,      Name = "오버레이 이동 (드래그)",   Default = CS | Keys.W, HotkeyId = 5 },
            new Action { Id = Hide,      Name = "오버레이 숨기기/보이기",   Default = CS | Keys.E, HotkeyId = 6 },
            new Action { Id = Links,     Name = "링크 표시 켜기/끄기",      Default = CS | Keys.L, HotkeyId = 8 },
            new Action { Id = PrevStage, Name = "이전 링크 단계",           Default = CS | Keys.J, HotkeyId = 10 },
            new Action { Id = NextStage, Name = "다음 링크 단계",           Default = CS | Keys.K, HotkeyId = 9 },
            new Action { Id = Tips,      Name = "지역 팁 표시 켜기/끄기",   Default = CS | Keys.T, HotkeyId = 11 },
            new Action { Id = Tree,      Name = "패시브 트리 켜기/끄기",    Default = CS | Keys.P, HotkeyId = 12 },
            new Action { Id = PrevTree,  Name = "이전 트리 단계",           Default = CS | Keys.U, HotkeyId = 13 },
            new Action { Id = NextTree,  Name = "다음 트리 단계",           Default = CS | Keys.I, HotkeyId = 14 },
            new Action { Id = TreeBigger,  Name = "트리 창 크게",           Default = CS | Keys.Oemplus, HotkeyId = 15 },
            new Action { Id = TreeSmaller, Name = "트리 창 작게",           Default = CS | Keys.OemMinus, HotkeyId = 16 },
            new Action { Id = BuyRegex,  Name = "구매 젬 정규식 복사",      Default = CS | Keys.G, HotkeyId = 18 },
            new Action { Id = Regex,     Name = "상점 검색 정규식 복사",    Default = CS | Keys.F, HotkeyId = 17 },
            new Action { Id = Debug,     Name = "화면 저장 (문제 신고용)",  Default = CS | Keys.D, HotkeyId = 7 },
        };

        public static Action ById(string id) => Every.First(a => a.Id == id);

        public static Keys Get(Settings s, string id)
        {
            if (s?.hotkeys != null && s.hotkeys.TryGetValue(id, out var v) && Valid((Keys)v)) return (Keys)v;
            return ById(id).Default;
        }

        // Ctrl 또는 Alt 가 있고, 수정키가 아닌 키가 하나 있어야 한다
        public static bool Valid(Keys k)
        {
            var key = k & Keys.KeyCode;
            bool mod = (k & (Keys.Control | Keys.Alt)) != 0;
            return mod && key != Keys.None && key != Keys.ControlKey && key != Keys.ShiftKey && key != Keys.Menu
                && key != Keys.LWin && key != Keys.RWin;
        }

        public static uint Modifiers(Keys k) =>
            ((k & Keys.Control) != 0 ? Native.MOD_CONTROL : 0) | ((k & Keys.Shift) != 0 ? Native.MOD_SHIFT : 0) |
            ((k & Keys.Alt) != 0 ? Native.MOD_ALT : 0);

        // 수정키 부분만 (예: "Ctrl+Shift+")
        static string ModText(Keys k) =>
            ((k & Keys.Control) != 0 ? "Ctrl+" : "") + ((k & Keys.Shift) != 0 ? "Shift+" : "") + ((k & Keys.Alt) != 0 ? "Alt+" : "");

        public static string KeyName(Keys k)
        {
            var key = k & Keys.KeyCode;
            if (key >= Keys.D0 && key <= Keys.D9) return ((char)('0' + (key - Keys.D0))).ToString();
            if (key >= Keys.NumPad0 && key <= Keys.NumPad9) return "Num" + (key - Keys.NumPad0);
            switch (key)
            {
                case Keys.Oemtilde: return "`";
                case Keys.OemMinus: return "-";
                case Keys.Oemplus: return "=";
                case Keys.OemOpenBrackets: return "[";
                case Keys.OemCloseBrackets: return "]";
                case Keys.OemPipe: return "\\";
                case Keys.OemSemicolon: return ";";
                case Keys.OemQuotes: return "'";
                case Keys.Oemcomma: return ",";
                case Keys.OemPeriod: return ".";
                case Keys.OemQuestion: return "/";
                case Keys.Prior: return "PageUp";
                case Keys.Next: return "PageDown";
                default: return key.ToString();
            }
        }

        public static string Text(Keys k) => ModText(k) + KeyName(k);
        public static string Text(Settings s, string id) => Text(Get(s, id));

        // 짧게: 기본 수정키(Ctrl+Shift)면 키 이름만, 아니면 전체 (오버레이 링크 줄처럼 자리가 좁은 곳)
        public static string Short(Settings s, string id)
        {
            var k = Get(s, id);
            return (k & Keys.Modifiers) == CS ? KeyName(k) : Text(k);
        }

        // 여러 기능을 한 줄로. 수정키가 모두 같으면 "Ctrl+Shift+  R 수동 강조 · C 위치 확인", 다르면 기능마다 전체
        public static string Summary(Settings s, IEnumerable<(string id, string label)> items)
        {
            var on = new HashSet<string>(All.Select(a => a.Id));   // 개발용 키는 개발 모드에서만 안내
            var list = items.Where(x => on.Contains(x.id)).Select(x => (k: Get(s, x.id), x.label)).ToList();
            if (list.Count == 0) return "";
            var mods = list.Select(x => x.k & Keys.Modifiers).Distinct().ToList();
            if (mods.Count == 1)
                return ModText(list[0].k) + "  " + string.Join(" · ", list.Select(x => $"{KeyName(x.k)} {x.label}"));
            return string.Join(" · ", list.Select(x => $"{x.label} {Text(x.k)}"));
        }
    }
}
