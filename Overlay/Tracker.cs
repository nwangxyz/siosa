using System;
using System.Collections.Generic;
using System.Linq;

namespace Siosa
{
    public enum QuestState { NotYet, Probably, Done }

    // 로그에서 읽은 지역 이동으로 퀘스트 완료 여부를 판정하고, 프리셋마다 progress\<프리셋 ID>.json 에 저장한다.
    // 프리셋 = 캐릭터 하나: 이 프리셋의 캐릭터 이름(P.charName)을 기억하고, 다른 캐릭터로 보이는 동안은 기록하지 않는다.
    //   - 새 캐릭터 시작(레벨 1 황혼의 해안 접속)을 봤는데 이미 이름이 정해져 있으면 → 다른 캐릭터 (foreign)
    //   - 기억한 이름의 레벨업·사망 줄이 나오면 → 다시 이 프리셋 캐릭터
    //   - 다른 이름의 레벨업은 파티원일 수 있으므로 무시만 한다
    public class Tracker
    {
        readonly GameData data;
        readonly string path;
        public string Id { get; }   // 프리셋 ID
        HashSet<string> visited;

        public Progress P { get; private set; }
        public string CurrentArea { get; private set; }
        public int CurrentLevel { get; private set; }   // 지역 레벨
        public int CharLevel => P.charLevel;
        public event Action Changed;

        public Tracker(GameData data, string path, string id = null)
        {
            this.data = data;
            this.path = path;
            Id = id;
            P = Progress.Load(path);
            visited = new HashSet<string>(P.visited);
            CurrentArea = P.currentArea;
            CurrentLevel = P.currentLevel;
            // 예전 진행 파일에는 lastAct 가 없다 → 지금 지역의 액트로 채운다
            if (P.lastAct == 0 && CurrentArea != null && data.Areas.TryGetValue(CurrentArea, out var a)) P.lastAct = a.act;
        }

        // 지금 액트 (액트 지역이 아니면 0. 은신처도 0 → 보상·포인트 줄은 액트 안에서만 보인다)
        public int CurrentAct => CurrentArea != null && data.Areas.TryGetValue(CurrentArea, out var a) ? a.act : 0;
        public bool InHideout => CurrentArea != null && CurrentArea.StartsWith("Hideout", StringComparison.Ordinal);
        public Area CurrentAreaInfo => CurrentArea != null && data.Areas.TryGetValue(CurrentArea, out var a) ? a : null;

        public void OnAreaEntered(string time, int level, string areaId, bool fromLogin)
        {
            // 새 캐릭터 = 캐릭터 선택 화면(로그인)에서 곧바로 레벨 1 황혼의 해안에 들어옴.
            //   마을에서 해안으로 걸어 나가는 경우는 로그인을 거치지 않으므로 초기화하지 않는다.
            if (areaId == "1_1_1" && level == 1 && fromLogin)
            {
                if (P.charName != null)
                {
                    // 이 프리셋은 이미 캐릭터가 있다 → 다른 캐릭터를 새로 만든 것. 기록을 멈춘다
                    P.foreign = true;
                    CurrentArea = areaId;
                    CurrentLevel = level;
                    Changed?.Invoke();
                    return;
                }
                P.foreign = false;
                P.charStart = time;
                P.visited.Clear();
                visited.Clear();
                P.charName = null;
                P.charLevel = 1;
                ClearPoints();
                // 받음 표시는 claimedFor 로 캐릭터를 구분하므로 지우지 않는다.
                // (로그를 처음부터 다시 읽으면 예전 캐릭터의 시작 줄을 먼저 지나가기 때문)
            }
            if (P.foreign)
            {
                // 다른 캐릭터로 보이는 동안은 위치만 보여 주고 방문 기록·액트는 남기지 않는다
                CurrentArea = areaId;
                CurrentLevel = level;
                Changed?.Invoke();
                return;
            }
            if (data.Areas.ContainsKey(areaId) && visited.Add(areaId)) P.visited.Add(areaId);
            if (data.Areas.TryGetValue(areaId, out var info) && info.act >= 1 && info.act <= 10) P.lastAct = info.act;
            CurrentArea = P.currentArea = areaId;
            CurrentLevel = P.currentLevel = level;
            Changed?.Invoke();
        }

        // 파티원의 레벨업도 로그에 찍히므로, 새 캐릭터 시작 후 처음 레벨업한 이름을 내 캐릭터로 본다
        public void OnLevelUp(string name, string cls, int level)
        {
            if (P.charName == null)
            {
                if (expect != null && name != expect) return;
                P.charName = name;
                expect = null;
            }
            if (name != P.charName) return;
            P.foreign = false;   // 이 프리셋 캐릭터가 다시 플레이 중
            P.charLevel = level;
            Changed?.Invoke();
        }

        public void OnDied(string name)
        {
            if (P.charName == null || name != P.charName || !P.foreign) return;
            P.foreign = false;
            Changed?.Invoke();
        }

        // "이전 진행 불러오기": 기록을 비우고, 다시 읽는 동안 이 이름의 레벨업에만 묶는다 (그 전의 파티원 레벨업은 무시)
        string expect;
        public void Adopt(string name)
        {
            ResetProgress();
            ForgetVisits();
            P.claimedFor = "";
            expect = name;
        }

        public bool Foreign => P.foreign;
        public string CharName => P.charName;

        // 편집 화면 "진행 초기화": 이 프리셋을 새 캐릭터에 다시 쓸 때. 다음 레벨업한 캐릭터가 이 프리셋 캐릭터가 된다
        public void ResetProgress()
        {
            P.charName = null;
            P.charLevel = 0;
            P.charStart = "";
            P.foreign = false;
            P.visited.Clear();
            visited.Clear();
            P.claimed.Clear();
            P.lastAct = 0;
            ClearPoints();
            Changed?.Invoke();
        }

        // 퀘스트 보상 포인트 (로그에 퀘스트 이름이 없어 얻은 순간의 액트로 기록, 표시는 누적 합계)
        public void OnPointsGained(bool refund, int count)
        {
            // 패시브 책은 은신처·지도 등 액트 밖에서 쓸 수도 있다 → 마지막으로 있던 액트로 센다 (예전: 버려짐)
            if (P.foreign) return;   // 다른 캐릭터의 포인트
            int act = CurrentAct >= 1 ? CurrentAct : P.lastAct;
            if (act < 1 || act > 10) return;
            EnsurePoints();
            (refund ? P.refund : P.passive)[act] += count;   // 표시는 누적 합계만 쓴다 (액트별 값은 참고용)
            Changed?.Invoke();
        }

        void EnsurePoints()
        {
            if (P.passive == null || P.passive.Length < 11) P.passive = new int[11];
            if (P.refund == null || P.refund.Length < 11) P.refund = new int[11];
        }

        public int Need(int act, bool refund) => data.Points.Where(q => q.act == act).Sum(q => refund ? q.refund : q.passive);

        void ClearPoints() { P.passive = new int[11]; P.refund = new int[11]; }

        public int PassiveTotal { get { EnsurePoints(); return P.passive.Sum(); } }
        public int RefundTotal { get { EnsurePoints(); return P.refund.Sum(); } }

        public QuestState State(Offer o)
        {
            if (o.complete == null || !visited.Contains(o.complete)) return QuestState.NotYet;
            return o.sure ? QuestState.Done : QuestState.Probably;
        }

        bool ClaimsValid => P.claimedFor == P.charStart;

        public bool IsClaimed(Offer o) => ClaimsValid && P.claimed.Contains(o.id);

        // 같은 퀘스트(예: 자비로운 임무의 플라스크 보상과 보조 젬 보상)의 다른 보상 창이 이미 받음 처리됐는지
        public bool SiblingClaimed(Offer o) =>
            data.Offers.Any(x => x.quest == o.quest && x.id != o.id && IsClaimed(x));

        // "퀘스트 완료" 배너로 확인된 보상. 화면에는 표시하지 않고, 같은 색 배열 후보를 가를 때만 쓴다.
        public void SetClaimed(Offer o, bool on)
        {
            if (P.foreign) return;
            if (!ClaimsValid) { P.claimed.Clear(); P.claimedFor = P.charStart; }   // 다른 캐릭터의 기록이면 새로 시작
            P.claimed.Remove(o.id);
            if (on) P.claimed.Add(o.id);
            Changed?.Invoke();
        }

        // 로그를 다시 읽기 전에 방문 기록을 비운다. 받음 표시는 유지.
        public void ForgetVisits()
        {
            ClearPoints();   // 다시 읽으면서 다시 센다
            P.visited.Clear();
            P.offset = 0;
            visited.Clear();
        }

        public void Save(string logPath, long offset)
        {
            P.logPath = logPath;
            P.offset = offset;
            P.Save(path);
        }

        // 프로필에서 고른 보상 목록 (해당 클래스 기준)
        public IEnumerable<(Offer offer, Reward reward)> Picks(Profile profile)
        {
            foreach (var o in data.Offers)
            {
                if (!profile.picks.TryGetValue(o.id, out var en)) continue;
                if (!o.rewards.TryGetValue(profile.classId, out var list)) continue;
                var r = GameData.FindReward(list, en);
                if (r != null) yield return (o, r);
            }
        }
    }
}
