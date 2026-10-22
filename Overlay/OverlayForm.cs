using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Siosa
{
    // 게임 위에 떠 있는 안내창. 평소에는 마우스 클릭이 게임으로 그대로 통과한다.
    public class OverlayForm : Form
    {
        // 단축키는 Hotkeys.All (설정에서 바꿀 수 있음, 기본 Ctrl+Shift+ W/E/R/C/L/J/K/D)

        // 링크 표시 (링크 단축키로 켜기/끄기, 다음·이전 단계)
        bool showLinks;
        public bool ShowLinks { get => showLinks; set { showLinks = value; Redraw(); } }
        static readonly Font SocketFont = new Font("Consolas", 9f);

        readonly GameData data;
        readonly Settings settings;
        Tracker tracker;
        readonly HighlightForm highlight = new HighlightForm();
        Profile profile;
        bool editMode;
        string status;   // 한 줄 알림 (로그 못 찾음 등)


        // 보상 창 자동 감지
        readonly RewardDetector detector;
        readonly Timer detectTimer = new Timer { Interval = 200 };
        readonly Action onTrackerChanged;
        RewardDetector.Candidate autoShown;   // 지금 자동 강조 중인 보상 창
        RewardDetector.Result autoResult;     // 그 판별 결과 (칸 위치/배치)
        bool windowOpen;      // 지난 확인 때 보상 창이 열려 있었는지
        double openShift;     // 열린 창의 높이(위로 올라간 양) — 판별 후보를 좁힐 때 쓴다
        int identifyTries;    // 열린 창을 아직 판별 못 했으면 몇 번 더 시도할지 (아이콘이 늦게 그려지는 경우)
        readonly string debugDir;

        // 보상을 받았는지: 보상 창이 닫힌 뒤 3초 안에 화면 아래 가운데 "퀘스트 완료" 띠가 보이면 받은 것.
        // (게임이 마우스를 raw input 으로 받아서 클릭은 읽을 수 없었다)
        RewardDetector.Candidate pendingClaim;
        // 보상이 두 개인 퀘스트는 첫 보상을 받으면 게임이 곧바로(0.2초쯤) 다음 보상 창을 띄운다.
        // 닫힌 직후 같은 퀘스트의 다른 보상 창이 열리면 → 앞의 보상을 받은 것. (X/Esc 로 닫으면 다음 창이 안 뜬다)
        RewardDetector.Candidate lastClosed;
        RewardDetector.Result lastClosedResult;   // 마지막으로 닫힌 창의 판별 결과 (툴팁에 가려 다시 판별 못 할 때 재사용)
        DateTime lastClosedAt;
        bool lastClosedInPlace;   // 닫힘 없이 같은 자리에서 내용만 바뀌었는지 (자동 전환은 이 경우뿐)
        DateTime pendingUntil;

        public Action SettingsChanged;
        public Action StageChanged;   // 링크·트리 단계를 단축키로 바꿈 → 앱이 프리셋 파일에 저장

        public OverlayForm(GameData data, Settings settings, Profile profile, Tracker tracker, string debugDir)
        {
            this.debugDir = debugDir;
            detector = new RewardDetector(settings) { Icons = data.Icons };
            detectTimer.Tick += (s, e) => DetectTick();
            this.data = data;
            this.settings = settings;
            this.profile = profile;
            this.tracker = tracker;
            onTrackerChanged = () => { status = null; HideHighlightIfAreaChanged(); Redraw(); };
            tracker.Changed += onTrackerChanged;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Theme.Back;
            Opacity = 0.88;
            DoubleBuffered = true;
            StartPosition = FormStartPosition.Manual;
            var wa = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(320, 40);
            var home = new Point(wa.Left + 12, wa.Top + wa.Height / 3);
            Location = settings.overlayX >= 0 ? Ui.OnScreen(new Point(settings.overlayX, settings.overlayY), home) : home;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            treeWin = new TreeWindow(settings) { SettingsChanged = () => SettingsChanged?.Invoke() };
            VisibleChanged += (s, ev) => UpdateTreeWindow();   // 설정창을 열 때 오버레이를 숨기면 트리 창도
            SetHotkeys(true);
            Native.ExcludeFromCapture(Handle);   // 보상 칸을 캡처할 때 Siosa 창이 섞이지 않도록
            Redraw();
            detectTimer.Start();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            SetHotkeys(false);
            base.OnHandleDestroyed(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            tracker.Changed -= onTrackerChanged;   // 중지 후 다시 시작하면 새 오버레이가 구독한다
            detectTimer.Dispose();
            highlight.Close();
            treeWin?.Close();
            treeBmp?.Dispose();
            base.OnFormClosed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY)
            {
                int hk = m.WParam.ToInt32();
                switch (Hotkeys.All.FirstOrDefault(a => a.HotkeyId == hk)?.Id)   // 개발 모드가 아니면 D 는 목록에 없어 무시
                {
                    case Hotkeys.Move: ToggleEditMode(); break;
                    case Hotkeys.Hide:
                        Opacity = Opacity > 0 ? 0 : 0.88;
                        if (Opacity == 0) { HideHighlight(); windowOpen = false; }   // 다시 보이면 열린 창을 새로 판별
                        UpdateTreeWindow();
                        break;
                    case Hotkeys.Debug: DumpDebug(); break;
                    case Hotkeys.Regex: CopyRegex(); break;
                    case Hotkeys.BuyRegex: CopyBuyRegex(); break;
                    case Hotkeys.Links: ShowLinks = !ShowLinks; break;
                    case Hotkeys.NextStage: NextStage(1); StageChanged?.Invoke(); break;
                    case Hotkeys.PrevStage: NextStage(-1); StageChanged?.Invoke(); break;
                    case Hotkeys.Tree:
                        settings.showTree = !settings.showTree;
                        SettingsChanged?.Invoke();
                        UpdateTreeWindow();
                        break;
                    case Hotkeys.TreeBigger: ResizeTree(1); break;
                    case Hotkeys.TreeSmaller: ResizeTree(-1); break;
                    case Hotkeys.PrevTree: NextTree(-1); StageChanged?.Invoke(); break;
                    case Hotkeys.NextTree: NextTree(1); StageChanged?.Invoke(); break;
                    case Hotkeys.Tips:
                        // 상태 줄은 계속 남으므로 쓰지 않는다. 팁 줄이 나타나고 사라지는 것으로 충분
                        settings.showTips = !settings.showTips;
                        SettingsChanged?.Invoke();
                        Redraw();
                        break;
                }
                return;
            }
            base.WndProc(ref m);
        }

        // 다음(+1)·이전(-1) 트리 단계 (끝에서 처음으로 돈다). 미니맵이 꺼져 있으면 켠다
        void NextTree(int step)
        {
            int n = profile.trees?.Count ?? 0;
            if (n == 0) return;
            profile.treeStage = ((profile.treeStage + step) % n + n) % n;
            if (!settings.showTree) { settings.showTree = true; SettingsChanged?.Invoke(); }
            UpdateTreeWindow();
        }

        // ── 패시브 트리 창 (오버레이와 따로 뜨는 창) ──
        TreeWindow treeWin;
        Bitmap treeBmp;
        string treeKey;
        const int TreeMin = 160, TreeMax = 720, TreeStep = 40;

        // 트리 창을 보여 줄지·내용을 맞춘다 (오버레이가 숨겨져 있거나 설정창을 연 동안은 같이 숨김)
        void UpdateTreeWindow()
        {
            if (treeWin == null || treeWin.IsDisposed) return;
            bool show = settings.showTree && Visible && Opacity > 0 && data.Tree != null && profile.trees != null && profile.trees.Count > 0;
            if (!show) { if (treeWin.Visible) treeWin.Hide(); return; }
            int si = Math.Max(0, Math.Min(profile.treeStage, profile.trees.Count - 1));
            var cur = profile.trees[si];
            var prev = si > 0 ? profile.trees[si - 1] : null;
            int size = Math.Max(TreeMin, Math.Min(TreeMax, settings.treeSize));
            string key = $"{cur.GetHashCode()}|{si}|{cur.nodes.Count}|{prev?.nodes.Count}|{cur.masteries?.Count}|{size}";
            if (treeBmp == null || treeKey != key)
            {
                var old = treeBmp;
                treeBmp = TreeRender.Render(data.Tree, cur, prev, size, Theme.Back);
                treeKey = key;
                treeWin.SetContent($"트리 · {cur.name}",
                    $"   ({si + 1}/{profile.trees.Count} · {Hotkeys.Short(settings, Hotkeys.PrevTree)} 이전 · {Hotkeys.Short(settings, Hotkeys.NextTree)} 다음 · {Hotkeys.Short(settings, Hotkeys.TreeSmaller)}/{Hotkeys.Short(settings, Hotkeys.TreeBigger)} 크기)",
                    treeBmp);
                old?.Dispose();
            }
            if (!treeWin.Visible) treeWin.Show();
        }

        // 개발용(--snapshot): 트리 창 내용 맞추기·창 꺼내기
        public void DevUpdateTree() => UpdateTreeWindow();
        public TreeWindow DevTreeWindow => treeWin;

        void ResizeTree(int step)
        {
            settings.treeSize = Math.Max(TreeMin, Math.Min(TreeMax, settings.treeSize + step * TreeStep));
            if (!settings.showTree) settings.showTree = true;
            SettingsChanged?.Invoke();
            UpdateTreeWindow();
        }

        // 전역 단축키 등록/해제. 설정창을 여는 동안은 풀어 둔다 (단축키 변경 창이 키를 받을 수 있도록, 바뀐 키를 다시 등록하도록)
        public void SetHotkeys(bool on)
        {
            if (!IsHandleCreated) return;
            foreach (var a in Hotkeys.Every) Native.UnregisterHotKey(Handle, a.HotkeyId);
            if (!on) return;
            var failed = new List<string>();
            foreach (var a in Hotkeys.All)
            {
                var k = Hotkeys.Get(settings, a.Id);
                if (!Native.RegisterHotKey(Handle, a.HotkeyId, Hotkeys.Modifiers(k) | Native.MOD_NOREPEAT, (uint)(k & Keys.KeyCode)))
                    failed.Add(Hotkeys.Text(k));
            }
            if (failed.Count > 0) SetStatus("단축키 등록 실패 (다른 프로그램이 사용 중): " + string.Join(", ", failed));
            else Redraw();
        }

        // 다음(+1)·이전(-1) 링크 단계 (마지막 다음은 처음, 처음 이전은 마지막). 링크가 꺼져 있으면 켠다
        void NextStage(int step)
        {
            int n = profile.links?.Count ?? 0;
            if (n == 0) return;
            profile.linkStage = ((profile.linkStage + step) % n + n) % n;
            ShowLinks = true;
        }

        // 프리셋을 바꾸면 그 프리셋의 진행 기록(트래커)으로 바꾼다
        public void SetTracker(Tracker t)
        {
            if (t == tracker) return;
            tracker.Changed -= onTrackerChanged;
            tracker = t;
            tracker.Changed += onTrackerChanged;
            HideHighlight();
            Redraw();
        }

        public void SetProfile(Profile p)
        {
            profile = p;
            HideHighlight();
            Redraw();
        }

        public void SetStatus(string text)
        {
            status = text;
            Redraw();
        }

        // ── 보상 강조 ────────────────────────────────────────────

        void HideHighlight()
        {
            autoShown = null;
            autoResult = null;
            highlight.Hide();
        }

        // ── 보상 창 자동 감지 ────────────────────────────────────

        // 감지 후보: 지금 액트까지의 모든 보상 (이미 받음 처리한 것도 포함 → 다시 열리면 '안 받음'으로)
        //   퀘스트 완료 판정(로그의 지역 이동)은 넣지 않는다: 보상은 그보다 먼저 받을 수 있다
        //   (예: 사이렌의 마침곡은 머베일을 잡고 마을로 오면 액트 2 진입 전에 받는다). 어느 창인지는 아이콘 비교가 정한다.
        List<RewardDetector.Candidate> DetectCandidates()
        {
            int act = tracker.CurrentAct, i = 0;
            var picked = tracker.Picks(profile).ToDictionary(p => p.offer.id, p => p.reward);
            var list = new List<RewardDetector.Candidate>();
            foreach (var o in data.Offers)
            {
                i++;
                // 보상 NPC는 자기 액트 마을(또는 그 액트의 지역)에 있으므로 지금 있는 액트의 보상만 후보.
                // (예전: 이전 액트 보상까지 후보라, 고르지 않은 9장 태풍의 칼 창에 5장 우툴라 보상이 강조됐다)
                if (o.act != act || !o.rewards.TryGetValue(profile.classId, out var items) || items.Count == 0) continue;
                // 고르지 않은 퀘스트도 후보에 넣는다 (Pick = null → 판별만 하고 강조 안 함).
                // (예전: 고른 퀘스트만 후보라, 고르지 않은 힐록 보상 창을 칸 수가 같은 약품 상자 보상 2로 잘못 판별했다)
                list.Add(new RewardDetector.Candidate
                {
                    Offer = o, Pick = picked.TryGetValue(o.id, out var r) ? r : null, Items = items,
                    Claimed = tracker.IsClaimed(o), Priority = i,
                });
            }
            return list;
        }

        // 보상 NPC가 있는 곳: 마을 + 마을 밖 보상 NPC 지역
        static readonly HashSet<string> RewardPlaces = new HashSet<string>
        {
            "1_3_17_1",   // 도서관 - 시오사
            "1_3_9",      // 항구 - 페어그레이브즈 선장 (희망 한 모금)
            "1_3_8_2",    // 솔라리스 사원 2층 - 디알라 부인 (끈 감개)
        };
        bool AtRewardPlace => tracker.CurrentAreaInfo?.town == true || RewardPlaces.Contains(tracker.CurrentArea ?? "");

        // 0.2초마다: (마을에서만) 보상 창 띠 4곳만 보고 열림/닫힘을 판단한다.
        //   열린 순간 → 어느 퀘스트의 창인지 한 번 판별해서 강조
        //   닫힌 뒤 3초 → "퀘스트 완료" 띠가 뜨면 "받음" (같은 퀘스트의 두 번째 보상 창은 닫히는 순간 "받음")
        DateTime lastHoldLog;

        void DetectTick()
        {
            if (Opacity == 0) return;                                  // 숨김

            if (pendingClaim != null) CheckQuestComplete();
            if (!AtRewardPlace && !windowOpen) return;                 // 사냥 중에는 아무것도 안 한다

            double? shift;
            try { shift = detector.WindowOpen(detector.Shifts(DetectCandidates())); } catch { return; }   // 캡처 실패
            bool open = shift != null;

            if (open)
            {
                // 열린 창이 같은 자리에서 다른 창으로 바뀌는 경우는 "보상이 두 개인 퀘스트의 다음 보상 창"뿐이다
                // (첫 보상을 받으면 0.2초 만에 같은 자리에 뜬다). 판별해 둔 칸이 안 보이면 같은 퀘스트의 다른 보상 창인지만 확인하고,
                // 아니면 툴팁 등에 가려진 것으로 보고 강조를 유지한다.
                // (예전에는 띠 위치가 달라 보이기만 해도 다른 창으로 봐서, 툴팁 때문에 엉뚱한 보상이 번갈아 떴다)
                if (windowOpen && autoResult != null)
                {
                    bool still = true;
                    try { still = detector.StillThere(autoResult); } catch { }
                    if (!still && SiblingVisible(shift.Value))
                    {
                        DebugLog.Write("보상 창 내용이 바뀜 (같은 퀘스트의 다음 보상 창)");
                        WindowClosed(inPlace: true);
                    }
                }
                if (!windowOpen)
                {
                    openShift = shift.Value;
                    DebugLog.Write($"보상 창 열림 (지역 {tracker.CurrentArea}, 높이 이동 {openShift}, 게임 화면 {RewardGeometry.GameClient()})");
                    windowOpen = true;
                    identifyTries = 5;
                }
                if (autoShown == null && identifyTries > 0)
                {
                    identifyTries--;
                    Identify();
                }
                return;
            }

            if (!windowOpen) return;
            // 띠가 안 보여도, 아이템에 마우스를 올려 툴팁이 띠를 가린 것일 수 있다.
            // 판별해 둔 보상 칸 줄이 그대로 있으면 아직 열린 것으로 본다.
            bool stillThere = false;
            try { stillThere = autoResult != null && detector.FramesThere(autoResult); } catch { }
            if (stillThere)
            {
                if ((DateTime.Now - lastHoldLog).TotalSeconds >= 2)
                {
                    lastHoldLog = DateTime.Now;
                    DebugLog.Write("  띠 안 보임, 칸이 남아 있어 유지: " + detector.LastStillInfo);
                }
                return;
            }

            WindowClosed();
        }

        // 보상 창이 닫힘(또는 다른 보상 창으로 바뀜) → 받았는지 판단한다.
        // (마우스 클릭은 읽을 수 없었다: 게임 실행 중에는 입력 감시가 막혀 있다)
        // inPlace: 창이 닫히지 않고 같은 자리에서 다른 보상 창으로 바뀜
        void WindowClosed(bool inPlace = false)
        {
            DebugLog.Write($"보상 창 닫힘: 판별={autoShown?.Offer.id ?? "없음"}");
            windowOpen = false;
            var shown = autoShown;
            var closedResult = autoResult;
            HideHighlight();
            // 판별 못 한 창(닫히는 중의 잔상 등)이 닫힘 → 자동 전환이 아니다
            if (shown == null) { lastClosedInPlace = false; return; }
            lastClosedResult = closedResult;
            lastClosed = shown;
            lastClosedAt = DateTime.Now;
            lastClosedInPlace = inPlace;

            // 보상 창이 두 개인 퀘스트: 다른 쪽 보상이 이미 "퀘스트 완료" 배너로 확인됐으면 퀘스트가 끝난 상태라
            // 이 창에서는 배너가 다시 뜨지 않는다 → 열렸다 닫힌 것으로 받음 처리.
            // (안 받고 닫았다가 다시 열면 Identify 에서 다시 '안 받음'으로 되돌린다)
            if (tracker.SiblingClaimed(shown.Offer))
            {
                DebugLog.Write($"  → 같은 퀘스트의 다른 보상을 이미 받음: {shown.Offer.id} 받음 처리");
                tracker.SetClaimed(shown.Offer, true);
                return;
            }
            pendingClaim = shown;
            pendingUntil = DateTime.Now.AddSeconds(3);
            CheckQuestComplete();
        }

        void CheckQuestComplete()
        {
            bool seen = false;
            try { seen = detector.QuestCompleteVisible(); } catch { }
            if (seen)
            {
                DebugLog.Write($"  → '퀘스트 완료' 확인: {pendingClaim.Offer.id} 받음 처리");
                tracker.SetClaimed(pendingClaim.Offer, true);
                pendingClaim = null;
            }
            else if (DateTime.Now > pendingUntil)
            {
                DebugLog.Write("  → 3초 동안 '퀘스트 완료' 없음: 안 받음");
                pendingClaim = null;
            }
        }

        // 지금 화면이 열려 있는 창과 같은 퀘스트의 다른 보상 창인지
        bool SiblingVisible(double shift)
        {
            var cur = autoShown?.Offer;
            if (cur == null) return false;
            var sibs = DetectCandidates().Where(c => c.Offer.quest == cur.quest && c.Offer.id != cur.id).ToList();
            if (sibs.Count == 0) return false;
            try { return detector.Detect(sibs, shift)?.Match != null; } catch { return false; }
        }

        void Identify()
        {
            var cands = DetectCandidates();
            if (cands.Count == 0) return;
            RewardDetector.Result res;
            try { res = detector.Detect(cands, openShift); } catch { return; }
            var m = res?.Match;
            DebugLog.Write(m == null ? $"  판별 실패 (최고 점수 {res?.Score:0.00}, 후보 {cands.Count}개)"
                                     : $"  판별: {m.Offer.id} [{m.Offer.option}] 점수 {res.Score:0.00} ({res.Layout})");
            // 판별 실패: 툴팁이 칸을 가린 채로 띠만 잠깐 보인 경우(툴팁에서 툴팁으로 마우스를 옮길 때 등).
            // 방금(5초 안) 닫힌 창이 같은 자리에 그대로 보이면 그 창이 다시 열린 것으로 본다.
            bool reused = false;
            if (m == null)
            {
                bool same = false;
                if (lastClosedResult != null && (DateTime.Now - lastClosedAt).TotalSeconds <= 5)
                    try { same = detector.FramesThere(lastClosedResult); } catch { }
                if (!same) return;
                res = lastClosedResult;
                m = res.Match;
                reused = true;
                DebugLog.Write($"  → 방금 닫힌 {m.Offer.id} 창이 그대로 보임 ({detector.LastStillInfo}): 다시 강조");
            }

            // 색 배열이 같은 후보가 있으면 젬 아이콘(스킬마다 다른 금색 문양)으로 가른다
            if (!reused && res.Ties.Count > 0)
            {
                RewardDetector.Result byIcon = null;
                string why = "";
                try { byIcon = detector.ResolveByIcons(res, out why); } catch (Exception ex) { why = ex.Message; }
                DebugLog.Write($"  아이콘 비교: {why} → " + (byIcon != null ? $"{byIcon.Match.Offer.id} 확정" : "확정 못 함"));
                if (byIcon != null) { res = byIcon; m = byIcon.Match; }
            }
            autoShown = m;
            autoResult = res;
            if (!reused) try { detector.SetBaseline(res); DebugLog.Write("  칸별 아이콘 거리: " + string.Join(" ", res.BaseDist?.Select(d => d.ToString("0.0")) ?? new string[0])); } catch { }
            if (m.Claimed) tracker.SetClaimed(m.Offer, false);   // 다시 열렸으니 아직 안 받은 것

            var prev = lastClosed;
            // 자동 전환은 창이 닫히지 않고 0.2초 만에 내용만 바뀐다. 실제로 닫혔다가 직접 다음 버튼을 누른 경우는 해당 없음.
            if (prev != null && lastClosedInPlace && (DateTime.Now - lastClosedAt).TotalSeconds <= 0.6 &&
                prev.Offer.quest == m.Offer.quest && prev.Offer.id != m.Offer.id)
            {
                DebugLog.Write($"  → 같은 퀘스트의 다음 보상 창이 바로 열림: {prev.Offer.id} 받음 처리");
                tracker.SetClaimed(prev.Offer, true);
                if (pendingClaim?.Offer.id == prev.Offer.id) pendingClaim = null;
            }
            lastClosed = null;
            // 색 배열이 같은 다른 퀘스트와 구분할 수 없으면, 후보마다 고른 칸을 모두 강조하고 설명을 나눠 적는다
            var all = new[] { res }.Concat(res.Ties).GroupBy(r => r.Match.Offer.id).Select(g => g.First()).ToList();
            if (all.Count > 1) DebugLog.Write("  구분 불가 후보: " + string.Join(", ", all.Skip(1).Select(t => t.Match.Offer.id)));
            // 고르지 않은 퀘스트의 창이면 강조하지 않는다 (구분할 수 없는 후보 중 고른 것만 강조)
            all = all.Where(r => r.Match.Pick != null).ToList();
            if (all.Count == 0)
            {
                DebugLog.Write($"  → 고르지 않은 보상 창 ({m.Offer.id}): 안내 문구만");
                highlight.ShowNote(res.Rects, $"고르지 않은 보상 · [{m.Offer.option}]");
                return;
            }
            if (m.Pick == null) m = all[0].Match;
            var marked = all.Select(r => r.Rects[r.Match.Items.IndexOf(r.Match.Pick)]).Distinct().ToList();
            bool samePick = all.All(r => r.Match.Pick.en == m.Pick.en) && marked.Count == 1;
            string label = all.Count == 1
                ? $"{m.Offer.npc} [{m.Offer.option}] → {m.Pick.ko}"
                : samePick   // 보상 구성이 같은 퀘스트 (예: 정화를 위한 죽음 / 바스티리의 날개)
                    ? string.Join(" / ", all.Select(r => $"[{r.Match.Offer.option}]")) + $" → {m.Pick.ko}"
                    : string.Join("\n", all.Select(r => $"[{r.Match.Offer.option}] 이면 → {r.Match.Pick.ko}"));
            highlight.ShowMarked(marked, label);
        }

        // 상점 검색칸용 정규식을 클립보드에 (게임에서 검색칸을 누르고 Ctrl+V). 키 입력은 보내지 않는다
        void CopyRegex()
        {
            var text = RegexSearch.Build(data, profile);
            if (text.Length == 0) { SetStatus("복사할 정규식이 없습니다 (설정창 '정규식' 탭)"); return; }
            if (SetClipboard(text)) SetStatus($"정규식 복사됨 ({text.Length}자" + LengthNote(text) + ") · 검색칸에 Ctrl+V");
        }

        // 지금 액트(은신처면 마지막 액트) 상점에서 살 젬 이름만
        void CopyBuyRegex()
        {
            int act = tracker.CurrentAct >= 1 ? tracker.CurrentAct : tracker.P.lastAct;
            var text = RegexSearch.BuildBuy(data, profile, act, out int n);
            if (text.Length == 0) { SetStatus(act >= 1 ? $"{act}장에서 살 젬이 없습니다 ('젬 구매' 탭)" : "액트 안에서 눌러 주세요"); return; }
            if (SetClipboard(text)) SetStatus($"{act}장 구매 젬 {n}개 복사됨 ({text.Length}자" + LengthNote(text) + ") · 검색칸에 Ctrl+V");
        }

        static string LengthNote(string text) =>
            text.Length > RegexSearch.MaxLength ? $", {RegexSearch.MaxLength}자 넘음: 게임에서 잘릴 수 있음" : "";

        bool SetClipboard(string text)
        {
            for (int i = 0; ; i++)
            {
                try { Clipboard.SetText(text); return true; }
                catch (System.Runtime.InteropServices.ExternalException) when (i < 3) { System.Threading.Thread.Sleep(50); }   // 다른 프로그램이 클립보드를 잡고 있을 때
                catch (Exception ex) { SetStatus("클립보드 복사 실패: " + ex.Message); return false; }
            }
        }

        void DumpDebug()
        {
            try { SetStatus("디버그 저장: " + detector.DumpDebug(DetectCandidates(), debugDir)); }
            catch (Exception ex) { SetStatus("디버그 저장 실패: " + ex.Message); }
        }

        string highlightArea;
        void HideHighlightIfAreaChanged()
        {
            if (highlightArea != tracker.CurrentArea) HideHighlight();
            highlightArea = tracker.CurrentArea;
        }

        // ── 위치 이동 모드 ───────────────────────────────────────

        void ToggleEditMode()
        {
            editMode = !editMode;
            if (treeWin != null) treeWin.EditMode = editMode;   // 트리 창도 같이 옮길 수 있게
            int ex = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE);
            ex = editMode ? ex & ~Native.WS_EX_TRANSPARENT : ex | Native.WS_EX_TRANSPARENT;
            Native.SetWindowLong(Handle, Native.GWL_EXSTYLE, ex);
            if (!editMode)
            {
                settings.overlayX = Left;
                settings.overlayY = Top;
                SettingsChanged?.Invoke();
            }
            Redraw();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!editMode || e.Button != MouseButtons.Left) return;
            Native.ReleaseCapture();
            Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, IntPtr.Zero);
        }

        // ── 그리기 ───────────────────────────────────────────────

        class Line
        {
            public string Text, Text2;
            public Color Color, Color2;
            public Font Font = Theme.Body, Font2 = Theme.Bold;
            public int Indent;
            public List<(string text, Color color)> Segs;   // Text 뒤에 이어 그리는 여러 색 조각 (링크 젬)

            // 줄 높이: 글자 크기가 아니라 글꼴 높이로 (Text 가 빈 이어지는 줄도 한 줄 높이를 차지해야 한다)
            public int Height(int measured) => Math.Max(measured, Math.Max(Font.Height, Math.Max(Text2 != null ? Font2.Height : 0, Segs != null ? Theme.Body.Height : 0)));
        }

        readonly List<Line> lines = new List<Line>();
        const int Pad = 8;

        // 경험치 배율 (PoE1 위키 공식, 캠페인 범위: 몬스터 레벨 70 이하·캐릭터 95 미만 보정은 생략)
        //   안전 범위 = 3 + 캐릭터 레벨/16 (내림)
        //   초과 차이 d = max(|캐릭터 - 몬스터| - 안전 범위, 0)
        //   배율 = ((캐릭터+5) / (캐릭터+5 + d^2.5))^1.5
        // 이 지역 레벨에서 패널티가 없는 캐릭터 레벨 범위 (안전 범위가 캐릭터 레벨에 따라 달라서 직접 찾는다)
        //   예) 지역 37 → 32~42 (Lv 32: 안전 5, 차이 5 / Lv 42: 안전 5, 차이 5)
        public static (int lo, int hi) NoPenaltyLevels(int area)
        {
            var ok = Enumerable.Range(1, 100).Where(p => Math.Abs(p - area) <= 3 + p / 16).ToList();
            return (ok.Min(), ok.Max());
        }

        // 팁 줄 나누기: 대략 n 글자마다 공백에서 자른다 (공백이 없으면 그 자리에서)
        static IEnumerable<string> WrapTip(string text, int n)
        {
            while (text.Length > n)
            {
                int cut = text.LastIndexOf(' ', n);
                if (cut < n / 2) cut = n;
                yield return text.Substring(0, cut).TrimEnd();
                text = text.Substring(cut).TrimStart();
            }
            if (text.Length > 0) yield return text;
        }

        public static double ExpMultiplier(int player, int monster)
        {
            int safe = 3 + player / 16;
            double d = Math.Max(Math.Abs(player - monster) - safe, 0);
            return Math.Max(0.01, Math.Pow((player + 5) / (player + 5 + Math.Pow(d, 2.5)), 1.5));
        }

        void Redraw()
        {
            if (!IsHandleCreated) return;
            lines.Clear();

            var area = tracker.CurrentAreaInfo;
            int curAct = tracker.CurrentAct;
            if (area != null)
                lines.Add(new Line { Text = $"{area.ko}  ·  액트 {area.act}" + (tracker.CharLevel > 0 ? $"  ·  Lv {tracker.CharLevel}" : ""), Color = Theme.Text, Font = Theme.Bold });
            else if (tracker.InHideout)
                lines.Add(new Line { Text = "은신처" + (tracker.CharLevel > 0 ? $"  ·  Lv {tracker.CharLevel}" : ""), Color = Theme.Text, Font = Theme.Bold });
            else
                lines.Add(new Line { Text = tracker.CurrentArea == null ? "Siosa — 지역에 들어가면 표시됩니다" : "Siosa  ·  액트 밖 지역", Color = Theme.Dim, Font = Theme.Bold });

            // 이 프리셋 캐릭터가 아닌 캐릭터로 보이면 안내 (기록은 멈춰 있음)
            if (tracker.Foreign)
                lines.Add(new Line
                {
                    Text = $"다른 캐릭터로 보여 기록을 멈춤 · '{tracker.CharName}' 레벨업하면 다시 기록",
                    Color = Color.FromArgb(230, 180, 90), Indent = 4,
                });

            // 경험치 패널티 (마을에는 몬스터가 없으므로 표시 안 함)
            if (area != null && !area.town && tracker.CharLevel > 0 && tracker.CurrentLevel > 0)
            {
                double mult = ExpMultiplier(tracker.CharLevel, tracker.CurrentLevel);
                if (mult < 0.995)
                {
                    var (lo, hi) = NoPenaltyLevels(tracker.CurrentLevel);
                    lines.Add(new Line
                    {
                        Text = $"경험치 -{(1 - mult) * 100:0}%  ·  지역 Lv {tracker.CurrentLevel} (캐릭터 Lv {lo}~{hi}면 패널티 없음)",
                        Color = mult >= 0.9 ? Color.FromArgb(230, 200, 100) : Color.FromArgb(235, 110, 90),
                        Indent = 4,
                    });
                }
            }

            // 지역 팁 (경험치 패널티 바로 아래). 길면 두 줄로 나눈다
            var tip = settings.showTips ? data.TipFor(settings, tracker.CurrentArea) : null;
            if (!string.IsNullOrWhiteSpace(tip))
            {
                // 이어지는 줄은 첫 줄의 팁 내용 시작 위치에 맞춘다 ("팁  " 폭만큼 들여쓰기)
                int labelW = TextRenderer.MeasureText("팁  ", Theme.Body, Size.Empty, TextFormatFlags.NoPadding).Width;
                bool first = true;
                foreach (var part in WrapTip(tip.Trim(), 34))
                {
                    lines.Add(new Line { Text = first ? "팁  " : "", Color = Color.FromArgb(150, 210, 140), Indent = first ? 4 : 4 + labelW, Text2 = part, Color2 = Color.FromArgb(200, 220, 190), Font2 = Theme.Body });
                    first = false;
                }
            }

            // 퀘스트 보상 포인트: 지금 액트까지 받을 수 있는 수 중 받은 수 (누적).
            //   로그는 포인트를 실제로 얻은 순간(책 사용)에 찍히고 퀘스트 이름이 없어서, 8장 책을 9장에서 쓰면
            //   액트별로는 나눌 수 없다. 누적으로 보면 언제 썼든 정확하다.
            if (curAct >= 1 && curAct <= 10 && data.Points.Count > 0)
            {
                int needP = 0, needR = 0;
                for (int a = 1; a <= curAct; a++) { needP += tracker.Need(a, false); needR += tracker.Need(a, true); }
                // 도적을 도우면 2장 '도적 처리하기' 패시브(모두 처치 보상)를 받지 않는다
                if (curAct >= 2 && !Profile.KillsAllBandits(profile))
                    needP -= data.Points.Where(q => q.en == "Deal with the Bandits").Sum(q => q.passive);
                int gotP = tracker.PassiveTotal, gotR = tracker.RefundTotal;
                bool done = gotP >= needP && gotR >= needR;
                lines.Add(new Line
                {
                    Text = $"패시브 {gotP}/{needP}  ·  재설계 {gotR}/{needR}  ({curAct}장까지)",
                    Color = done ? Color.FromArgb(90, 92, 98) : Color.FromArgb(150, 200, 255), Indent = 4,
                });
            }

            // 이 액트 상점에서 살 젬 (마을·보상 NPC 지역에서만). 샀는지는 알 수 없으므로 그 액트에 있는 동안 계속 보인다.
            if (curAct >= 1 && AtRewardPlace && profile.buy != null && profile.buy.Count > 0)
            {
                // 보상·시작 젬으로 받는 것과 체크를 끈 것은 빼고, 이 액트에서 처음 살 수 있는 젬 (정규식 복사와 같은 기준)
                var here = RegexSearch.BuyGems(data, profile, curAct).Select(x => (x.id, v: x.where));
                foreach (var g in here.GroupBy(x => x.v.npc))
                {
                    var names = g.Select(x => data.GemName(x.id)).ToList();
                    for (int i = 0; i < names.Count; i += 4)   // 한 줄에 4개씩
                        lines.Add(new Line
                        {
                            Text = i == 0 ? $"구매 · {g.Key}: " : "",
                            Text2 = string.Join(", ", names.Skip(i).Take(4)),
                            Color = Color.FromArgb(150, 200, 255), Color2 = Theme.Text, Font2 = Theme.Body,
                            Indent = i == 0 ? 4 : 22,
                        });
                }
            }

            // 지금 액트의 보상: ☑ 받음 / ✔ 확실히 완료 / ◐ 아마 완료 / · 아직
            //   보상을 받을 수 있는 곳(마을·마을 밖 보상 NPC 지역)에서만. 사냥 중에는 '구매' 줄처럼 숨긴다
            foreach (var (o, r) in AtRewardPlace ? tracker.Picks(profile) : Enumerable.Empty<(Offer, Reward)>())
            {
                if (o.act != curAct) continue;
                var st = tracker.State(o);
                bool claimed = tracker.IsClaimed(o);
                string mark; Color c;
                if (claimed) { mark = "☑"; c = Color.FromArgb(90, 92, 98); }
                else if (st == QuestState.Done) { mark = "✔"; c = Color.FromArgb(120, 220, 120); }
                else if (st == QuestState.Probably) { mark = "◐"; c = Color.FromArgb(230, 200, 100); }
                else { mark = "·"; c = Theme.Dim; }

                lines.Add(new Line { Text = $"{mark} {o.ko}  → ", Color = c, Indent = 4, Text2 = r.ko, Color2 = claimed ? c : Theme.GemText(r.gem) });
                if (!claimed && st != QuestState.NotYet)
                    lines.Add(new Line { Text = $"{o.npc} [{o.option}]", Color = Theme.Dim, Indent = 22 });
            }

            // 링크: 고른 단계 하나 (Ctrl+Shift+L 켜기/끄기, K 다음 단계, J 이전 단계)
            if (ShowLinks && profile.links != null && profile.links.Count > 0)
            {
                int si = Math.Max(0, Math.Min(profile.linkStage, profile.links.Count - 1));
                var st = profile.links[si];
                lines.Add(new Line { Text = $"링크 · {st.name}", Color = Theme.Accent, Font = Theme.Bold, Text2 = $"   ({si + 1}/{profile.links.Count} · {Hotkeys.Short(settings, Hotkeys.PrevStage)} 이전 · {Hotkeys.Short(settings, Hotkeys.NextStage)} 다음)", Color2 = Theme.Dim, Font2 = Theme.Body });
                int sockW = st.groups.Count == 0 ? 0 : st.groups.Max(x => x.gems.Count * 2 - 1) + 2;   // 소켓 칸 폭 (B-G-B → 5글자 + 여백)
                foreach (var grp in st.groups.Where(x => x.gems.Count > 0))
                {
                    var segs = new List<(string, Color)>();
                    for (int k = 0; k < grp.gems.Count; k++)
                    {
                        if (k > 0) segs.Add((" ─ ", Theme.Dim));
                        var gi = data.Gems.TryGetValue(grp.gems[k], out var info) ? info : null;
                        segs.Add((gi?.ko ?? grp.gems[k], Theme.GemText(gi?.color)));
                    }
                    // 필요한 소켓 색 (예: B-G-B). 고정폭 글꼴로 줄을 맞춘다
                    string sockets = string.Join("-", grp.gems.Select(id => data.Gems.TryGetValue(id, out var gi2) ? (gi2.color ?? "w").ToUpperInvariant() : "?"));
                    lines.Add(new Line { Text = sockets.PadRight(sockW), Color = Theme.Dim, Font = SocketFont, Indent = 4, Segs = segs });
                }
            }

            UpdateTreeWindow();   // 패시브 트리는 따로 뜨는 창

            if (status != null) lines.Add(new Line { Text = status, Color = Theme.Accent });
            if (editMode) lines.Add(new Line { Text = $"이동 모드: 드래그 후 {Hotkeys.Text(settings, Hotkeys.Move)}", Color = Theme.Accent });
            else lines.Add(new Line
            {
                Text = Hotkeys.Summary(settings, new[] { (Hotkeys.Move, "이동"), (Hotkeys.Hide, "숨기기"), (Hotkeys.Links, "링크"), (Hotkeys.Tree, "트리"), (Hotkeys.Tips, "팁"), (Hotkeys.BuyRegex, "구매 젬"), (Hotkeys.Regex, "정규식") }),
                Color = Theme.Dim,
            });

            int w = 0, h = Pad;
            foreach (var l in lines)
            {
                var s1 = TextRenderer.MeasureText(l.Text, l.Font, Size.Empty, TextFormatFlags.NoPadding);
                int lw = l.Indent + s1.Width + (l.Text2 != null ? TextRenderer.MeasureText(l.Text2, l.Font2, Size.Empty, TextFormatFlags.NoPadding).Width : 0);
                if (l.Segs != null) foreach (var sg in l.Segs) lw += TextRenderer.MeasureText(sg.text, Theme.Body, Size.Empty, TextFormatFlags.NoPadding).Width;
                w = Math.Max(w, lw);
                h += l.Height(s1.Height) + 3;
            }
            Size = new Size(w + Pad * 2 + 4, h + Pad);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var pen = new Pen(editMode ? Theme.Accent : Color.FromArgb(70, 72, 78)))
                g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            int y = Pad;
            const TextFormatFlags F = TextFormatFlags.NoPadding;
            foreach (var l in lines)
            {
                var s1 = TextRenderer.MeasureText(l.Text, l.Font, Size.Empty, F);
                TextRenderer.DrawText(g, l.Text, l.Font, new Point(Pad + l.Indent, y), l.Color, F);
                if (l.Text2 != null)
                    TextRenderer.DrawText(g, l.Text2, l.Font2, new Point(Pad + l.Indent + s1.Width, y), l.Color2, F);
                if (l.Segs != null)
                {
                    int x = Pad + l.Indent + s1.Width;
                    foreach (var sg in l.Segs)
                    {
                        TextRenderer.DrawText(g, sg.text, Theme.Body, new Point(x, y), sg.color, F);
                        x += TextRenderer.MeasureText(sg.text, Theme.Body, Size.Empty, F).Width;
                    }
                }
                y += l.Height(s1.Height) + 3;
            }
        }
    }
}
