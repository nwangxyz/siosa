using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Siosa
{
    // 클라이언트 로그를 꼬리 읽기한다. 파일 전체를 읽지 않고, 늘어난 부분만 300ms 마다 읽는다.
    public class LogWatcher : IDisposable
    {
        static readonly Regex AreaRe = new Regex(@"^(\S+ \S+) .*Generating level (\d+) area ""([^""]+)""", RegexOptions.Compiled);

        // 레벨업: "이름(클래스) 님이 N레벨이 되었습니다." / 영문 "이름 (클래스) is now level N"
        static readonly Regex LevelRe = new Regex(@"\] : (.+?) ?\(([^()]+)\) (?:님이 (\d+)레벨이 되었습니다|is now level (\d+))", RegexOptions.Compiled);

        // (로그 시각, 지역 레벨, 지역 ID, 로그인 직후인지)
        public event Action<string, int, string, bool> AreaEntered;
        bool sawLogin;   // 로그인 서버에 접속한 뒤 아직 지역에 들어가지 않음 (캐릭터 선택 화면에서 들어온 것)
        // (캐릭터 이름, 클래스, 레벨)
        public event Action<string, string, int> LevelUp;

        // 사망: "] : 이름 님이 사망했습니다." (파티원도 찍힘. 이 프리셋 캐릭터의 이름인지만 본다)
        static readonly Regex DeathRe = new Regex(@"\] : (.+?) 님이 사망했습니다", RegexOptions.Compiled);
        public event Action<string> Died;

        // 퀘스트 보상 포인트. 시스템 메시지는 "] : 내용" 형식 (채팅은 "] #이름: …" 처럼 이름이 붙는다)
        //   ] : 패시브 스킬 포인트를 획득했습니다.
        //   ] : 패시브 스킬 포인트 2포인트를 획득했습니다.        (10장 마지막 보상처럼 한 번에 여러 개)
        //   ] : 패시브 스킬 재설계 포인트 2포인트를 획득했습니다.
        static readonly Regex PassiveRe = new Regex(@"\] : 패시브 스킬 포인트(?: (\d+)포인트)?를 획득했습니다", RegexOptions.Compiled);
        static readonly Regex RefundRe = new Regex(@"\] : 패시브 스킬 재설계 포인트 (\d+)포인트를 획득했습니다", RegexOptions.Compiled);
        public event Action<bool, int> PointsGained;   // (재설계 포인트인지, 개수)

        readonly string path;
        readonly Timer timer = new Timer { Interval = 300 };
        long pos;
        // 아직 줄바꿈이 오지 않은 마지막 줄 (바이트 그대로: 4MB 경계에서 한글 글자가 잘려도 줄이 끝날 때 한 번에 읽는다)
        readonly List<byte> partial = new List<byte>();

        public LogWatcher(string path)
        {
            this.path = path;
            timer.Tick += (s, e) => Poll();
        }

        // 다음에 읽을 위치 = 마지막으로 다 읽은 줄의 끝. 저장해 두면 다음 실행 때 이어서 읽을 수 있다.
        //   (예전: 읽은 바이트 끝이라, 게임이 줄을 쓰는 도중에 저장하면 그 줄 앞부분을 건너뛰어 한 줄을 놓쳤다)
        public long Position => pos - partial.Count;

        // resumeAt: 읽기 시작할 위치 (지난번에 읽은 위치, 또는 "이전 진행 불러오기"로 고른 캐릭터의 시작).
        //   음수거나 파일보다 크면 지금 끝부터 (새 프리셋: 예전 로그로 캐릭터를 추측하지 않는다.
        //   최근 로그의 첫/마지막 레벨업 이름으로 정하면 파티원·몇 달 전 캐릭터가 잡히는 경우가 많았다)
        public void Start(long resumeAt)
        {
            using (var fs = Open())
            {
                pos = resumeAt >= 0 && resumeAt <= fs.Length ? resumeAt : fs.Length;
                partial.Clear();
                ReadFrom(fs);
            }
            timer.Start();
        }

        // 로그에 나온 캐릭터 목록 (설정창 "이전 진행 불러오기"). 최근에 레벨업한 순서.
        //   파티원 레벨업도 섞여 있으므로 사용자가 고른다. 새로 만든 기록(로그인 → 레벨 1 황혼의 해안 → 2레벨)이
        //   있으면 그 로그인 줄부터, 없으면 처음 보인 접속부터 다시 읽는다.
        public class LogChar
        {
            public string Name, Class, Time;
            public int Level;
            public long Start = -1;   // 다시 읽을 위치
            public bool Created;      // 새로 만든 기록이 로그에 있음
        }

        public static List<LogChar> ScanCharacters(string path, int max = 12)
        {
            var chars = new Dictionary<string, LogChar>();
            long login = -1, created = -1, lastLogin = 0;   // lastLogin: 마지막 접속 줄 (만든 기록 없는 캐릭터는 처음 보인 접속부터)
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var buf = new byte[4 << 20];
                var carry = new List<byte>();
                long chunkPos = 0, carryPos = 0;   // buf[0] 의 파일 위치, carry 첫 바이트의 파일 위치
                int r;
                while ((r = fs.Read(buf, 0, buf.Length)) > 0)
                {
                    int lineStart = 0;
                    for (int i = 0; i < r; i++)
                    {
                        if (buf[i] != '\n') continue;
                        string line;
                        long at;
                        if (carry.Count > 0)
                        {
                            carry.AddRange(new ArraySegment<byte>(buf, 0, i).ToArray());
                            line = Encoding.UTF8.GetString(carry.ToArray());
                            at = carryPos;
                            carry.Clear();
                        }
                        else
                        {
                            line = Encoding.UTF8.GetString(buf, lineStart, i - lineStart);
                            at = chunkPos + lineStart;
                        }
                        lineStart = i + 1;
                        if (line.IndexOf("Async connecting to", StringComparison.Ordinal) >= 0 && line.IndexOf("login", StringComparison.OrdinalIgnoreCase) >= 0)
                            login = lastLogin = at;
                        else if (line.IndexOf("Generating level", StringComparison.Ordinal) >= 0)
                        {
                            var m = AreaRe.Match(line);
                            if (m.Success)
                            {
                                if (login >= 0 && m.Groups[3].Value == "1_1_1" && m.Groups[2].Value == "1") created = login;
                                login = -1;
                            }
                        }
                        else if (line.IndexOf("레벨이 되었습니다", StringComparison.Ordinal) >= 0 || line.IndexOf("is now level", StringComparison.Ordinal) >= 0)
                        {
                            var lm = LevelRe.Match(line);
                            if (!lm.Success) continue;
                            string name = lm.Groups[1].Value;
                            int lv = int.Parse(lm.Groups[3].Success ? lm.Groups[3].Value : lm.Groups[4].Value);
                            if (!chars.TryGetValue(name, out var c)) chars[name] = c = new LogChar { Name = name, Start = lastLogin };
                            c.Class = lm.Groups[2].Value;
                            c.Level = lv;
                            c.Time = line.Length >= 19 ? line.Substring(0, 19) : "";
                            if (lv == 2 && created >= 0) { c.Start = created; c.Created = true; created = -1; }
                        }
                    }
                    // 줄이 버퍼 끝에서 잘렸으면 다음 읽기로 넘긴다
                    if (lineStart < r)
                    {
                        if (carry.Count == 0) carryPos = chunkPos + lineStart;
                        carry.AddRange(new ArraySegment<byte>(buf, lineStart, r - lineStart).ToArray());
                    }
                    chunkPos += r;
                }
            }
            return chars.Values.OrderByDescending(c => c.Time, StringComparer.Ordinal).Take(max).ToList();
        }

        FileStream Open() => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        void Poll()
        {
            try
            {
                using (var fs = Open())
                {
                    if (fs.Length < pos) { pos = 0; partial.Clear(); }   // 로그가 지워지고 새로 생긴 경우
                    if (fs.Length == pos) return;
                    ReadFrom(fs);
                }
            }
            catch (IOException) { }
        }

        // 4MB 씩 끝까지 읽는다. (예전: 남은 만큼을 배열 하나로 한 번에 읽어서, 오래 꺼 둔 뒤나 오래전 캐릭터를 불러올 때
        // 수백 MB~GB 를 한 번에 잡아 멈추거나 메모리 부족으로 꺼질 수 있었다)
        readonly byte[] chunk = new byte[4 << 20];
        void ReadFrom(FileStream fs)
        {
            fs.Seek(pos, SeekOrigin.Begin);
            int n;
            while ((n = fs.Read(chunk, 0, chunk.Length)) > 0)
            {
                pos += n;
                int lineStart = 0;
                for (int i = 0; i < n; i++)
                {
                    if (chunk[i] != '\n') continue;
                    string line;
                    if (partial.Count > 0)
                    {
                        for (int k = lineStart; k < i; k++) partial.Add(chunk[k]);
                        line = Encoding.UTF8.GetString(partial.ToArray());
                        partial.Clear();
                    }
                    else line = Encoding.UTF8.GetString(chunk, lineStart, i - lineStart);
                    HandleLine(line.TrimEnd('\r'));
                    lineStart = i + 1;
                }
                for (int k = lineStart; k < n; k++) partial.Add(chunk[k]);
            }
        }

        void HandleLine(string line)
        {
            if (line.IndexOf("포인트를 획득했습니다", StringComparison.Ordinal) >= 0)
            {
                var pm = PassiveRe.Match(line);
                if (pm.Success) PointsGained?.Invoke(false, pm.Groups[1].Success ? int.Parse(pm.Groups[1].Value) : 1);
                else
                {
                    var rm = RefundRe.Match(line);
                    if (rm.Success) PointsGained?.Invoke(true, int.Parse(rm.Groups[1].Value));
                }
                return;
            }
            if (line.IndexOf("레벨이 되었습니다", StringComparison.Ordinal) >= 0 || line.IndexOf("is now level", StringComparison.Ordinal) >= 0)
            {
                var lm = LevelRe.Match(line);
                if (lm.Success)
                    LevelUp?.Invoke(lm.Groups[1].Value, lm.Groups[2].Value,
                        int.Parse(lm.Groups[3].Success ? lm.Groups[3].Value : lm.Groups[4].Value));
                return;
            }
            if (line.IndexOf("님이 사망했습니다", StringComparison.Ordinal) >= 0)
            {
                var dm = DeathRe.Match(line);
                if (dm.Success) Died?.Invoke(dm.Groups[1].Value.Trim());
                return;
            }
            if (line.IndexOf("Async connecting to", StringComparison.Ordinal) >= 0 && line.IndexOf("login", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                sawLogin = true;
                return;
            }
            if (line.IndexOf("Generating level", StringComparison.Ordinal) < 0) return;
            var m = AreaRe.Match(line);
            if (!m.Success) return;
            bool fromLogin = sawLogin;
            sawLogin = false;
            AreaEntered?.Invoke(m.Groups[1].Value, int.Parse(m.Groups[2].Value), m.Groups[3].Value, fromLogin);
        }

        public void Dispose() => timer.Dispose();
    }
}
