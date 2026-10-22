using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Siosa
{
    // 1) 클래스 선택 → 2) 퀘스트별 보상 선택 → 3) 저장 / 시작 / 초기화
    public class SetupForm : Form
    {
        readonly GameData data;
        readonly string profilesDir;
        readonly Settings settings;

        readonly FlatCombo cbClass = new FlatCombo(140) { Margin = new Padding(0) };
        readonly FieldBox nameBox = new FieldBox(220);
        readonly FieldBox logBox = new FieldBox(520);
        TextBox tbName => nameBox.Box;
        TextBox tbLog => logBox.Box;
        readonly Label lbLogHint = new Label { AutoSize = true, ForeColor = Theme.Dim, Margin = new Padding(8, 6, 0, 0) };
        readonly FlowLayoutPanel list = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            AutoScroll = true, BackColor = Theme.Back, Padding = new Padding(8),
        };
        readonly ToolTip tip = new ToolTip();

        // 보상 ID → 그 보상의 선택 버튼들
        readonly Dictionary<string, List<ChoiceChip>> buttons = new Dictionary<string, List<ChoiceChip>>();
        readonly Dictionary<string, string> picks = new Dictionary<string, string>();
        string bandit = "None";   // 2장 도적 선택 (Profile.Bandits 키)
        string presetId = NewId();   // 프리셋 ID (진행 기록을 프리셋마다 따로 둔다)
        string presetPath;           // 지금 편집 중인 프리셋의 파일 (열었거나 저장한 것. 새로 만든 뒤 저장 전이면 null)
        public string ResultPath { get; private set; }   // 시작한 프리셋의 파일 (단축키로 바꾼 링크·트리 단계를 여기에 저장)
        static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 12);

        // 프리셋 캐릭터 정보·진행 초기화 (오버레이 앱이 넘겨줌. 개발용 화면에서는 null)
        public Func<string, string> ProgressInfo;
        public Action<string> ResetProgress;
        public Func<List<LogWatcher.LogChar>> ScanChars;
        public Action<string, LogWatcher.LogChar> RecoverProgress;
        // 아래쪽 줄 "캐릭터: 이름 · Lv ▾" 버튼 하나 + 메뉴(이전 진행 불러오기 / 새 캐릭터로 시작 = 진행 초기화).
        //   예전: 글자와 버튼 두 개를 따로 놓아 아래쪽 줄이 넘쳐 캐릭터 글자가 잘리거나 사라졌다
        FlatButton btnChar;
        readonly ContextMenuStrip charMenu = new ContextMenuStrip();
        ToolStripMenuItem miReset;

        // 젬 구매: 구매할 젬 ID (PoB 또는 검색으로 추가), 체크 해제한 젬, PoB 스킬 묶음 (링크 표시용)
        readonly List<string> buy = new List<string>();
        readonly HashSet<string> buyOff = new HashSet<string>();
        List<LinkStage> links = new List<LinkStage>();   // 링크 탭 (단계 → 묶음 → 젬)
        int linkStage;                                    // 오버레이에 보여 줄 단계

        public Profile Result { get; private set; }
        public bool StartRequested { get; private set; }
        public bool RestartRequested { get; private set; }   // 업데이트를 마침 → 앱이 새 exe 로 다시 시작
        public string AppRoot;                                // Siosa 폴더 (업데이트할 곳). 개발용 화면에서는 null

        // 오버레이가 실행 중이면 시작 버튼이 "중지"가 된다. 중지하면 설정창은 그대로 두고 오버레이만 끈다.
        // (X 는 언제나 프로그램 종료)
        public Action Stop;
        FlatButton btnStart;
        // 기본은 젬 보상만. 체크하면 장비·플라스크 보상 퀘스트도 보여 주고 고를 수 있다.
        readonly FlatCheck cbItems = new FlatCheck("장비·플라스크 보상도 선택") { Margin = new Padding(0, 0, 16, 0) };
        bool suppressItems;
        bool running;
        public bool Running
        {
            get => running;
            set { running = value; if (btnStart != null) btnStart.Text = value ? "중지" : "시작"; }
        }

        public SetupForm(GameData data, string profilesDir, Settings settings, Profile current, string currentPath = null)
        {
            this.data = data;
            this.profilesDir = profilesDir;
            this.settings = settings;

            Text = "Siosa";
            Icon = Ui.AppIcon(32);
            Size = new Size(900, 760);
            MinimumSize = new Size(640, 480);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = Theme.Body;

            // 한 창 안에서 메인 화면 ↔ 편집 화면을 바꿔 보여 준다 (X 는 언제나 종료)
            editor.Controls.Add(list);
            editor.Controls.Add(buyPanel);
            editor.Controls.Add(linkPanel);
            editor.Controls.Add(tipPanel);
            editor.Controls.Add(treePanel);
            editor.Controls.Add(regexPanel);
            editor.Controls.Add(BuildBottom());
            editor.Controls.Add(BuildTabs());
            editor.Controls.Add(BuildTop());
            BuildBuyPanel();
            BuildLinkPanel();
            BuildTipPanel();
            BuildTreePanel();
            BuildRegexPanel();
            prebuild.Tick += (s, e) => Prebuild();
            FormClosed += (s, e) => prebuild.Dispose();
            Controls.Add(editor);
            Controls.Add(home);
            BuildHome();

            cbItems.Checked = settings.showItems;
            cbItems.CheckedChanged += (s, e) => OnItemsToggled();

            cbClass.Items.AddRange(data.Classes.ToArray());
            cbClass.SelectedIndexChanged += (s, e) =>
            {
                // 사용자가 클래스를 바꾸면 새 프리셋: 이전 클래스에서 고른 것은 남기지 않는다
                // (같은 보상이 있는 퀘스트만 선택이 남아 섞이던 문제). 프리셋을 불러올 때는 그 프리셋의 선택을 쓴다.
                if (!applying) { presetId = NewId(); picks.Clear(); bandit = "None"; tbName.Text = ""; buy.Clear(); buyOff.Clear(); links = new List<LinkStage>(); linkStage = 0; linkDirty = true; trees = new List<TreeStage>(); treeStage = 0; treeDirty = true; }
                RebuildList();
                MarkBuyDirty();
            };
            list.Resize += (s, e) => { if (!resizing) FitCards(list); };
            foreach (var sc in new ScrollableControl[] { list, buyList, linkList, tipList, home }) KeepTopVisible(sc);

            // 비워 두면 실행할 때마다 자동 감지한다. 직접 지정한 경로가 있으면 그것을 쓴다.
            tbLog.Text = settings.logPath;
            UpdateLogHint();
            tbLog.TextChanged += (s, e) => UpdateLogHint();

            // 처음 실행하면 메인 화면, 오버레이 실행 중에 다시 열면 지금 쓰는 프리셋의 편집 화면
            if (current != null) { Apply(current); presetPath = currentPath != null && File.Exists(currentPath) ? currentPath : null; ShowEditor(); }   // 실행 중에 다시 열면 그 프리셋의 파일 (저장 여부 판단용)
            else { cbClass.SelectedIndex = 0; ShowHome(); }
        }

        // ── 메인 화면 ─────────────────────────────────────────────

        readonly Panel editor = new Panel { Dock = DockStyle.Fill };
        readonly Panel home = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Back, AutoScroll = true, Padding = new Padding(40, 28, 40, 16) };
        readonly FlowLayoutPanel presetList = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
        readonly Label lbHomeLog = new Label { AutoSize = true, Margin = new Padding(0, 18, 0, 0) };
        // 단축키 안내 (단축키를 바꾸면 다시 쓴다)
        readonly Label lbHomeKeys = new Label { AutoSize = true, ForeColor = Theme.Text, BackColor = Ui.Card, Margin = new Padding(3, 0, 3, 3) };   // 위 1~3번 줄(기본 여백 3)과 왼쪽을 맞춤
        readonly Label lbLinkKeys = new Label { ForeColor = Theme.Dim, AutoSize = true, Margin = new Padding(14, 6, 0, 0) };
        readonly Label lbTipKeys = new Label { ForeColor = Theme.Dim, AutoSize = true, Margin = new Padding(14, 6, 0, 0) };

        readonly Label lbVersion = new Label { AutoSize = true, ForeColor = Theme.Dim, Margin = new Padding(0, 6, 10, 0) };
        readonly FlatButton btnUpdate = new FlatButton("업데이트 확인 중…", FlatButton.Kind.Normal) { Enabled = false, Margin = new Padding(0), Height = 28 };
        Updater.Info update;

        // 창이 뜬 뒤 GitHub 최신 릴리스를 확인 (기다리지 않는다. 실패하면 버전만 보여 준다)
        async void CheckUpdate()
        {
            if (AppRoot == null || !Updater.Supported)
            {
                btnUpdate.Visible = false;
                lbVersion.Text = $"v{AppInfo.Version} · 개발 실행 (업데이트 확인 안 함)";
                return;
            }
            try { update = await Updater.CheckAsync(); }
            catch (Exception ex)
            {
                DebugLog.Write("업데이트 확인 실패: " + ex.Message);
                btnUpdate.Text = "업데이트 확인 실패";
                tip.SetToolTip(btnUpdate, ex.Message);
                return;
            }
            if (IsDisposed) return;
            if (Updater.IsNewer(update))
            {
                btnUpdate.Text = $"v{update.Version} 으로 업데이트";
                btnUpdate.Style = FlatButton.Kind.Primary;
                btnUpdate.Enabled = true;
                lbVersion.Text = $"v{AppInfo.Version} · 새 버전이 있습니다";
                lbVersion.ForeColor = Theme.Accent;
            }
            else btnUpdate.Text = "최신 버전";
        }

        async void RunUpdate()
        {
            if (!Updater.IsNewer(update)) return;
            // 릴리스 본문의 맨 위 '변경 사항'만 (--- 아래는 설치 안내), 마크다운 기호는 떼고
            var notes = (update.Notes ?? "").Replace("\r", "");
            int cut = notes.IndexOf("\n---", StringComparison.Ordinal);
            if (cut >= 0) notes = notes.Substring(0, cut);
            notes = System.Text.RegularExpressions.Regex.Replace(notes, @"^#+\s*", "", System.Text.RegularExpressions.RegexOptions.Multiline)
                         .Replace("**", "").Replace("`", "").Trim();
            if (notes.Length > 600) notes = notes.Substring(0, 600) + "…";
            if (!Confirm($"v{AppInfo.Version} → v{update.Version} 으로 업데이트할까요?\n받은 뒤 Siosa 가 다시 시작됩니다. 설정·프리셋·진행 기록은 그대로 남습니다." +
                         (notes.Length > 0 ? "\n\n" + notes : ""))) return;
            btnUpdate.Enabled = false;
            var prog = new UpdateForm(update.Version.ToString());
            Enabled = false;   // 진행 창이 떠 있는 동안 설정창은 누를 수 없게
            prog.Show(this);
            try
            {
                await System.Threading.Tasks.Task.Run(() => Updater.Apply(update,
                    (msg, pct) => BeginInvoke((Action)(() => prog.SetProgress(msg, pct)))));
                await System.Threading.Tasks.Task.Delay(400);   // '완료'를 잠깐 보여 준다
            }
            catch (Exception ex)
            {
                prog.Close();
                Enabled = true;
                btnUpdate.Text = $"v{update.Version} 으로 업데이트";
                btnUpdate.Enabled = true;
                DebugLog.Write("업데이트 실패: " + ex);
                MessageBox.Show(this, "업데이트하지 못했습니다. 지금 버전은 그대로입니다.\n\n" + ex.Message +
                                      $"\n\n직접 받기: {update.PageUrl}", "Siosa");
                return;
            }
            // 끝: 설정창을 닫으면 앱이 종료하고 새 버전을 띄운다 (따로 묻지 않음)
            prog.Close();
            Enabled = true;
            RestartRequested = true;
            Close();
        }

        void BuildHome()
        {
            const int W = 760;   // 메인 화면 카드 폭
            var col = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Theme.Back };
            // 로고: 아이콘 + 금색 Siosa
            var brand = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Back, Margin = new Padding(0) };
            using (var ico = Ui.AppIcon(48))
                brand.Controls.Add(new PictureBox { Image = ico.ToBitmap(), Size = new Size(48, 48), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 0, 10, 0) });
            brand.Controls.Add(new Label { Text = "Siosa", Font = new Font(Theme.Header.FontFamily, 24f, FontStyle.Bold), ForeColor = Theme.Accent, AutoSize = true, Margin = new Padding(0, 2, 0, 0) });
            col.Controls.Add(brand);
            col.Controls.Add(new Label { Text = "액트 퀘스트 보상 강조 오버레이", ForeColor = Theme.Dim, AutoSize = true, Margin = new Padding(4, 0, 0, 6) });
            // 버전 · 업데이트 (새 버전이 있을 때만 버튼이 켜진다)
            var verRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Back, Margin = new Padding(2, 0, 0, 14) };
            lbVersion.Text = $"v{AppInfo.Version}";
            verRow.Controls.Add(lbVersion);
            btnUpdate.Click += (s, e) => RunUpdate();
            verRow.Controls.Add(btnUpdate);
            col.Controls.Add(verRow);

            var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 16), BackColor = Theme.Back };
            var create = new FlatButton("새로 만들기", FlatButton.Kind.Primary, 130) { Height = 36, Margin = new Padding(0) };
            create.Click += (s, e) => NewPreset();
            var pob = new FlatButton("PoB 불러오기", FlatButton.Kind.Normal, 130) { Height = 36, Margin = new Padding(8, 0, 0, 0) };
            pob.Click += (s, e) => { if (ImportPob(fresh: true)) ShowEditor(); };
            var fromFile = new FlatButton("파일에서 불러오기", FlatButton.Kind.Normal, 150) { Height = 36, Margin = new Padding(8, 0, 0, 0) };
            fromFile.Click += (s, e) => { if (LoadProfile()) ShowEditor(); };
            actions.Controls.Add(create);
            actions.Controls.Add(pob);
            actions.Controls.Add(fromFile);
            col.Controls.Add(actions);

            // 저장한 프리셋 카드
            var presets = new Card();
            var head = CardRow();
            head.WrapContents = false;
            head.Controls.Add(new Label { Text = "저장한 프리셋", Font = Theme.Header, ForeColor = Theme.Accent, AutoSize = true, Margin = new Padding(0, 0, 0, 6) });
            presets.Body.Controls.Add(head);
            presetList.BackColor = Ui.Card;
            presets.Body.Controls.Add(presetList);
            col.Controls.Add(presets);
            presets.FitWidth(W);

            // 사용법 카드
            var usage = new Card { Margin = new Padding(0, 0, 0, 10) };
            // 제목과 버튼을 세로 가운데로 맞춘다 (제목 글자 높이 기준으로 버튼 높이를 정함)
            var usageHead = CardRow();
            usageHead.WrapContents = false;
            usageHead.Margin = new Padding(0, 0, 0, 6);
            var usageTitle = new Label { Text = "사용법", Font = Theme.Header, ForeColor = Theme.Accent, AutoSize = true, Margin = new Padding(0) };
            usageHead.Controls.Add(usageTitle);
            var keysBtn = new FlatButton("단축키 변경", FlatButton.Kind.Normal) { Margin = new Padding(14, 0, 0, 0) };
            keysBtn.Height = usageTitle.PreferredHeight + 2;
            keysBtn.Click += (s, e) => EditHotkeys();
            usageHead.Controls.Add(keysBtn);
            usage.Body.Controls.Add(usageHead);
            usage.Body.Controls.Add(new Label
            {
                AutoSize = true, ForeColor = Theme.Text, BackColor = Ui.Card,
                Text = "1. 프리셋을 새로 만들거나(PoB 불러오기 가능) 저장한 프리셋을 골라 시작합니다.\n" +
                       "2. 게임에서 퀘스트 보상 창을 열면 고른 보상이 강조됩니다.\n" +
                       "3. 오버레이에 지역·레벨·경험치 패널티·패시브 포인트가 표시됩니다. 설정은 트레이 아이콘으로 다시 엽니다.",
            });
            lbHomeKeys.MaximumSize = new Size(W - 40, 0);   // 바꾼 키가 길어지면 줄바꿈
            usage.Body.Controls.Add(lbHomeKeys);
            UpdateHotkeyTexts();
            col.Controls.Add(usage);
            usage.FitWidth(W);
            var report = BuildReportCard();
            col.Controls.Add(report);
            report.FitWidth(W);

            lbHomeLog.Margin = new Padding(4, 4, 0, 0);
            col.Controls.Add(lbHomeLog);
            home.Controls.Add(col);
        }

        // ── 버그 제보 · 건의하기 (카카오톡 오픈채팅) ──
        const string ReportUrl = "https://open.kakao.com/o/svXdSBTi";

        Card BuildReportCard()
        {
            var card = new Card { Margin = new Padding(0, 0, 0, 10) };
            var row = CardRow();
            row.WrapContents = false;

            // QR: 흰 바탕 정사각형. 작게 줄일 때 흐려지지 않도록 가장 가까운 픽셀로 (휴대폰 카메라로 찍기 쉽게)
            const int qr = 116;
            var pic = new PictureBox { Size = new Size(qr, qr), Margin = new Padding(0, 2, 18, 0), BackColor = Color.White, Cursor = Cursors.Hand };
            using (var s = typeof(SetupForm).Assembly.GetManifestResourceStream("Siosa.kakao_qr.png"))
                if (s != null)
                    using (var src = new Bitmap(s))
                    {
                        var bmp = new Bitmap(qr, qr);
                        using (var g = Graphics.FromImage(bmp))
                        {
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                            g.DrawImage(src, new Rectangle(0, 0, qr, qr));
                        }
                        pic.Image = bmp;
                    }
            pic.Click += (s, e) => OpenReport();
            tip.SetToolTip(pic, "휴대폰 카메라로 찍으면 오픈채팅이 열립니다");
            row.Controls.Add(pic);

            var text = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Ui.Card, Margin = new Padding(0) };
            text.Controls.Add(new Label { Text = "버그 제보 · 건의하기", Font = Theme.Header, ForeColor = Theme.Accent, AutoSize = true, Margin = new Padding(0, 0, 0, 6) });
            text.Controls.Add(new Label
            {
                AutoSize = true, ForeColor = Theme.Text, BackColor = Ui.Card, Margin = new Padding(0, 0, 0, 10),
                MaximumSize = new Size(590, 0),   // 카드 폭(760) - QR - 여백: 넘치면 줄바꿈
                Text = "카카오톡 오픈채팅으로 받습니다. 휴대폰으로 QR 을 찍거나 아래 버튼을 누르세요.\n" +
                       "버그는 액트·지역·해상도를 같이 알려 주세요.",
            });
            var open = new FlatButton("오픈채팅 열기", FlatButton.Kind.Primary, 130) { Height = 32, Margin = new Padding(0) };
            open.Click += (s, e) => OpenReport();
            text.Controls.Add(open);
            text.Controls.Add(new Label { Text = ReportUrl, ForeColor = Theme.Dim, AutoSize = true, Margin = new Padding(0, 6, 0, 0) });
            row.Controls.Add(text);
            card.Body.Controls.Add(row);
            return card;
        }

        // 기본 브라우저로 오픈채팅 주소를 연다 (사용자가 누를 때만)
        void OpenReport()
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ReportUrl) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, "브라우저를 열지 못했습니다.\n" + ReportUrl + "\n\n" + ex.Message, "Siosa"); }
        }

        // 메인 화면 사용법·링크 탭의 단축키 안내
        void UpdateHotkeyTexts()
        {
            lbHomeKeys.Text = "4. 단축키 " + Hotkeys.Summary(settings, new[]
            {
                (Hotkeys.Move, "오버레이 이동"), (Hotkeys.Hide, "숨기기"), (Hotkeys.Links, "링크"),
                (Hotkeys.PrevStage, "이전 단계"), (Hotkeys.NextStage, "다음 단계"), (Hotkeys.Tips, "팁"), (Hotkeys.Tree, "트리"), (Hotkeys.PrevTree, "이전 트리"), (Hotkeys.NextTree, "다음 트리"), (Hotkeys.TreeSmaller, "트리 작게"), (Hotkeys.TreeBigger, "트리 크게"), (Hotkeys.BuyRegex, "구매 젬 정규식"), (Hotkeys.Regex, "정규식"), (Hotkeys.Debug, "화면 저장"),
            });
            lbRegexKeys.Text = $"게임 중 {Hotkeys.Text(settings, Hotkeys.Regex)} → 상점 검색칸을 누르고 Ctrl+V (직접 입력 + 자주 쓰는 정규식)";
            lbBuyKeys.Text = $"게임 중 {Hotkeys.Text(settings, Hotkeys.BuyRegex)} → 상점 검색칸을 누르고 Ctrl+V";
            lbTipKeys.Text = $"오버레이의 경험치 패널티 아래에 표시 · 게임 중 {Hotkeys.Text(settings, Hotkeys.Tips)} 켜기/끄기 · 모든 프리셋 공통 · 비우면 숨김";
            lbLinkKeys.Text = "게임 중 " + Hotkeys.Summary(settings, new[]
            {
                (Hotkeys.Links, "링크 표시"), (Hotkeys.PrevStage, "이전 단계"), (Hotkeys.NextStage, "다음 단계"),
            });
        }

        void EditHotkeys()
        {
            using (var f = new HotkeyForm(settings))
                if (f.ShowDialog(this) == DialogResult.OK)
                {
                    UpdateHotkeyTexts();
                    linkDirty = true;           // 링크 탭 툴팁도 새 키로
                    HotkeysChanged?.Invoke();   // 설정 파일 바로 저장
                }
        }

        // 단축키를 바꿔 저장했을 때 (설정 파일 저장용)
        public Action HotkeysChanged;

        void ShowHome()
        {
            RefreshPresets();
            var found = !string.IsNullOrEmpty(settings.logPath) ? (File.Exists(settings.logPath) ? settings.logPath : null) : LogDetect.Find();
            lbHomeLog.Text = found != null ? "게임 로그: 연결됨 ✔   " + found : "게임 로그를 찾지 못했습니다. 게임을 켜면 자동으로 찾습니다. (직접 지정은 편집 화면 아래)";
            lbHomeLog.ForeColor = found != null ? Color.FromArgb(120, 200, 120) : Color.FromArgb(230, 180, 90);
            editor.Visible = false;
            home.Visible = true;
        }

        // 아래쪽 줄의 "캐릭터: 이름 · Lv" (프리셋마다 진행 기록이 따로라 어느 캐릭터인지 보여 준다)
        void UpdateCharInfo()
        {
            bool on = ProgressInfo != null;
            btnChar.Visible = on;
            if (!on) return;
            var info = ProgressInfo(presetId);
            btnChar.Text = (info != null ? "캐릭터: " + info : "캐릭터: 아직 없음") + "  ▾";
            tip.SetToolTip(btnChar, (info != null ? "이 프리셋의 진행 기록이 묶인 캐릭터."
                : "시작한 뒤 처음 레벨업하는 캐릭터가 이 프리셋 캐릭터가 됩니다.") + "\n눌러서: 이전 진행 불러오기(이미 키우던 캐릭터) · 새 캐릭터로 시작(진행 초기화)");
            miReset.Enabled = info != null;
        }

        // ── 창 크기 바꾸기 ──
        // 탭 화면 다섯 개가 겹쳐 있어서(전환을 빠르게 하려고 앞뒤만 바꿈) 크기가 바뀔 때마다 보이지 않는 탭까지
        // 전부 다시 배치했다 (20px 마다 150~560ms → 끊김). 가장자리를 끄는 동안은 배치를 멈추고 놓을 때 한 번만 한다.
        //   놓을 때도 지금 보이는 탭만 배치하고, 나머지 탭은 그 탭으로 바꿀 때 배치한다 (놓을 때 140~820ms → 그 탭 몫만).
        bool resizing;
        readonly HashSet<Control> layoutPending = new HashSet<Control>();
        Control[] TabPanels => new Control[] { list, buyPanel, linkPanel, tipPanel, treePanel, regexPanel };
        Control ActivePanel => buyShown ? buyPanel : linksShown ? (Control)linkPanel : treesShown ? treePanel : tipsShownNow ? tipPanel : regexShown ? regexPanel : list;
        bool tipsShownNow;

        protected override void OnResizeBegin(EventArgs e)
        {
            base.OnResizeBegin(e);
            BeginDeferred();
        }

        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            EndDeferred();
        }

        // 최대화·복원은 끌기 시작/끝 알림이 없다 → 크기가 한 번에 바뀌는 그때만 같은 방식으로
        FormWindowState lastState = FormWindowState.Normal;
        public static bool DevNoMinFix;   // 개발용: 최소화 수정 끄고 비교
        bool minimized;   // 최소화 동안은 배치를 멈춘다 (예전: 최소화 크기로 탭 전부 배치 → 복원할 때 또 전부 → 버벅임)
        protected override void OnResize(EventArgs e)
        {
            if (IsHandleCreated && WindowState == FormWindowState.Minimized && !DevNoMinFix)
            {
                if (!minimized && !resizing) { minimized = true; BeginDeferred(); }
                base.OnResize(e);
                return;
            }
            if (minimized)
            {
                minimized = false;
                base.OnResize(e);
                EndDeferred();
                lastState = WindowState;
                return;
            }
            bool jump = !resizing && IsHandleCreated && WindowState != lastState && WindowState != FormWindowState.Minimized;
            if (jump) BeginDeferred();
            base.OnResize(e);
            if (jump) EndDeferred();
            if (WindowState != FormWindowState.Minimized) lastState = WindowState;
        }

        // 끄는 동안만 창 전체 합성 그리기: 크기가 바뀔 때마다 배경을 지우고 카드·버튼을 하나씩 다시 그리는 사이가
        // 깜빡임으로 보였다. 평소에는 끈다 (입력칸 커서 등에 영향이 없도록)
        void SetComposited(bool on)
        {
            if (!IsHandleCreated) return;
            int ex = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE);
            int want = on ? ex | Native.WS_EX_COMPOSITED : ex & ~Native.WS_EX_COMPOSITED;
            if (want != ex) Native.SetWindowLong(Handle, Native.GWL_EXSTYLE, want);
        }

        void BeginDeferred()
        {
            if (resizing) return;
            resizing = true;
            SetComposited(true);
            home.SuspendLayout();
            editor.SuspendLayout();
            foreach (var c in TabPanels) if (layoutPending.Add(c)) c.SuspendLayout();
        }

        void EndDeferred()
        {
            if (!resizing) return;
            resizing = false;
            home.ResumeLayout(true);
            editor.ResumeLayout(true);
            LayoutPanel(ActivePanel);
            SetComposited(false);
            foreach (var sc in new ScrollableControl[] { list, buyList, linkList, tipList, regexList, home }) FixScrollTop(sc);
            Invalidate(true);
        }

        // 크기를 바꾼 뒤 아직 배치하지 않은 탭 화면을 배치하고 카드 폭을 맞춘다
        void LayoutPanel(Control panel)
        {
            if (!layoutPending.Remove(panel)) return;
            panel.ResumeLayout(true);
            if (panel == list) FitCards(list);
            else if (panel == buyPanel) FitCards(buyList);
            else if (panel == linkPanel) FitCards();
            else if (panel == tipPanel && tipsBuilt) FitTips();
            else if (panel == regexPanel) FitRegex();
        }

        // ── 스크롤 위치 바로잡기 ──
        // 스크롤을 내린 채 창을 키워 세로 스크롤바가 사라지면, 내린 만큼 내용이 위로 밀린 채 남아
        // 맨 위가 잘리고 스크롤바도 없어 되돌릴 수 없었다 (WinForms 자동 스크롤). 배치가 끝날 때마다 확인해 맨 위로 돌린다.
        void KeepTopVisible(ScrollableControl sc)
        {
            void Later()
            {
                if (sc.IsHandleCreated) sc.BeginInvoke((Action)(() => FixScrollTop(sc)));   // 배치가 끝난 뒤에 (배치 중에 위치를 바꾸면 다시 배치됨)
            }
            sc.Layout += (s, e) => Later();
            sc.ClientSizeChanged += (s, e) => Later();   // 스크롤바가 사라지면 안쪽 크기가 바뀐다
            sc.VisibleChanged += (s, e) => Later();
        }

        public static int DevScrollFixes;   // 개발용: 바로잡기가 실제로 위치를 고친 횟수
        public static bool DevNoScrollFix;  // 개발용: 바로잡기를 끄고 원래 현상 재현

        static void FixScrollTop(ScrollableControl sc)
        {
            if (DevNoScrollFix) return;
            if (sc.IsDisposed || sc.VerticalScroll.Visible || sc.Controls.Count == 0) return;
            int top = sc.Controls.Cast<Control>().Min(c => c.Top - c.Margin.Top);
            if (top >= 0) return;
            DevScrollFixes++;
            sc.AutoScrollPosition = Point.Empty;
            top = sc.Controls.Cast<Control>().Min(c => c.Top - c.Margin.Top);
            if (top < 0) { sc.AutoScroll = false; sc.AutoScroll = true; }   // 스크롤바가 없으면 위치 지정이 안 먹을 때
        }

        // 실행 중에 설정창을 다시 열면 생성자에서 바로 편집 화면을 여는데, 그때는 ProgressInfo 를 아직 못 받았다
        // (만든 뒤에 넘겨줌) → 캐릭터 버튼이 숨겨진 채 남았다. 창이 뜰 때 한 번 더 채운다
        protected override void OnShown(EventArgs e)
        {
            CheckUpdate();   // Shown 처리기(개발용 화면 캡처 등)보다 먼저 시작
            base.OnShown(e);
            UpdateCharInfo();
        }

        void ShowEditor()
        {
            UpdateCharInfo();
            home.Visible = false;
            editor.Visible = true;
            if (buyDirty) { prebuild.Stop(); prebuild.Start(); }   // 구매 탭을 미리 만들어 둔다
        }

        // 저장한 프리셋: 최근 수정 순. 열기 → 편집 화면 (확인 후 편집 화면에서 시작)
        void RefreshPresets()
        {
            presetList.SuspendLayout();
            foreach (Control c in presetList.Controls) c.Dispose();
            presetList.Controls.Clear();
            var files = Directory.Exists(profilesDir)
                ? new DirectoryInfo(profilesDir).GetFiles("*.json").OrderByDescending(f => f.LastWriteTime).ToList()
                : new List<FileInfo>();
            foreach (var f in files)
            {
                Profile p;
                try { p = Profile.Load(f.FullName); } catch { continue; }
                var cls = data.Classes.FirstOrDefault(c => c.id == p.classId);
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 0), BackColor = Ui.Card };
                row.Controls.Add(new Label
                {
                    Text = p.name, Font = Theme.Bold, AutoSize = false, Size = new Size(300, 30),
                    TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Margin = new Padding(0),
                });
                row.Controls.Add(new Label
                {
                    Text = $"{cls?.ko ?? "?"}  ·  보상 {p.picks.Count}개  ·  {f.LastWriteTime.Month}월 {f.LastWriteTime.Day}일",
                    ForeColor = Theme.Dim, AutoSize = false, Size = new Size(236, 30), TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(10, 0, 4, 0),
                });
                var open = new FlatButton("열기", FlatButton.Kind.Primary, 70);
                open.Click += (s, e) => { if (Open(f.FullName)) ShowEditor(); };
                row.Controls.Add(open);
                var del = new FlatButton("삭제", FlatButton.Kind.Danger, 70) { Margin = new Padding(6, 0, 0, 0) };
                string name = p.name;
                del.Click += (s, e) => DeletePreset(f.FullName, name);
                row.Controls.Add(del);
                presetList.Controls.Add(row);
            }
            if (files.Count == 0)
                presetList.Controls.Add(new Label { Text = "저장한 프리셋이 없습니다. '새로 만들기'로 시작하세요.", ForeColor = Theme.Dim, AutoSize = true, Margin = new Padding(0, 6, 0, 2) });
            presetList.ResumeLayout();
        }

        // 프리셋 파일을 휴지통으로 (실수로 지워도 휴지통에서 되살릴 수 있게)
        void DeletePreset(string path, string name)
        {
            if (!Confirm($"'{name}' 프리셋을 삭제할까요?\n(파일은 휴지통으로 이동합니다)")) return;
            if (!Recycle(path)) return;
            if (string.Equals(settings.lastProfile, path, StringComparison.OrdinalIgnoreCase)) settings.lastProfile = "";
            if (SamePath(presetPath, path)) presetPath = null;
            RefreshPresets();
        }

        // 프리셋 파일을 휴지통으로. 실패하면 알리고 false
        bool Recycle(string path)
        {
            try
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "파일을 휴지통으로 옮기지 못했습니다.\n" + ex.Message, "Siosa");
                return false;
            }
        }

        static bool SamePath(string a, string b) =>
            a != null && b != null && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

        void NewPreset()
        {
            ClearEditor();
            ShowEditor();
        }

        // 빈 프리셋 상태로 (클래스는 첫 번째, 장비·플라스크 숨김)
        void ClearEditor()
        {
            presetId = NewId();
            presetPath = null;
            regexCustom = "";
            regexOn = new HashSet<string>();
            SyncRegexUi();
            picks.Clear();
            bandit = "None";
            buy.Clear();
            buyOff.Clear();
            links = new List<LinkStage>();
            linkStage = 0;
            linkDirty = true;
            trees = new List<TreeStage>();
            treeStage = 0;
            treeDirty = true;
            buyDirty = true;
            tbSearch.Text = "";
            ShowTab(TabRewards);
            tbName.Text = "";
            suppressItems = true; cbItems.Checked = false; suppressItems = false;
            settings.showItems = false;
            applying = true;
            try
            {
                if (cbClass.SelectedIndex == 0) RebuildList();
                else cbClass.SelectedIndex = 0;
            }
            finally { applying = false; }
            MarkSaved();
        }

        static string FileId(string path) => "file-" + Path.GetFileNameWithoutExtension(path);

        bool Open(string path)
        {
            try
            {
                ClearEditor();
                var loaded = Profile.Load(path);
                // 예전 프리셋은 ID 가 없다 → 파일 이름으로 정해 둔다 (다시 열어도 같은 진행 기록을 쓰도록)
                if (string.IsNullOrEmpty(loaded.id)) loaded.id = FileId(path);
                Apply(loaded);
                presetPath = path;
                settings.lastProfile = path;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "불러오지 못했습니다: " + ex.Message, "Siosa");
                return false;
            }
        }

        ClassInfo SelectedClass => cbClass.SelectedItem as ClassInfo;

        Control BuildTop()
        {
            var p = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 50, Padding = new Padding(14, 11, 14, 0), BackColor = Theme.Panel, WrapContents = false };
            var back = MakeButton("← 처음으로", BackToHome);
            back.Margin = new Padding(0);
            p.Controls.Add(back);
            p.Controls.Add(new Panel { Size = new Size(1, 28), BackColor = Ui.CardBorder, Margin = new Padding(14, 0, 14, 0) });
            p.Controls.Add(new Label { Text = "클래스", ForeColor = Theme.Dim, AutoSize = true, Margin = new Padding(0, 6, 6, 0) });
            p.Controls.Add(cbClass);
            p.Controls.Add(new Label { Text = "프리셋 이름", ForeColor = Theme.Dim, AutoSize = true, Margin = new Padding(18, 6, 6, 0) });
            nameBox.Margin = new Padding(0);
            p.Controls.Add(nameBox);
            var load = MakeButton("불러오기", () => LoadProfile());
            load.Margin = new Padding(12, 0, 0, 0);
            p.Controls.Add(load);
            var pob = MakeButton("PoB 불러오기", () => ImportPob(fresh: false));
            pob.Margin = new Padding(6, 0, 0, 0);
            p.Controls.Add(pob);
            return p;
        }

        Control BuildBottom()
        {
            var p = new Panel { Dock = DockStyle.Bottom, Height = 92, BackColor = Theme.Panel, Padding = new Padding(14, 10, 14, 10) };

            var logRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32, WrapContents = false, BackColor = Theme.Panel };
            logRow.Controls.Add(new Label { Text = "게임 로그", ForeColor = Theme.Dim, AutoSize = true, Margin = new Padding(0, 6, 8, 0) });
            logBox.Margin = new Padding(0);
            logRow.Controls.Add(logBox);
            var browse = MakeButton("찾기", BrowseLog);
            browse.Margin = new Padding(8, 0, 0, 0);
            logRow.Controls.Add(browse);
            logRow.Controls.Add(MakeButton("자동", () => tbLog.Text = "", kind: FlatButton.Kind.Ghost));
            lbLogHint.Margin = new Padding(10, 6, 0, 0);
            logRow.Controls.Add(lbLogHint);

            var btnRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = Theme.Panel };   // 넘쳐도 다음 줄(안 보임)로 밀지 않게
            btnRow.Controls.Add(MakeButton("초기화", ResetPicks));
            btnStart = new FlatButton("시작", FlatButton.Kind.Primary, 96);
            btnStart.Click += (s, e) => { if (Running) { Stop?.Invoke(); Running = false; } else StartOverlay(); };
            btnStart.Margin = new Padding(8, 0, 0, 0);
            btnRow.Controls.Add(btnStart);
            btnRow.Controls.Add(MakeButton("저장", () => SaveProfile()));
            btnRow.Controls.Add(cbItems);
            charMenu.Items.Add("이전 진행 불러오기 (이미 키우던 캐릭터)…", null, (s2, e2) => PickCharacter());
            // 초기화 뒤 처음 레벨업한 캐릭터로 묶이므로 기존 캐릭터에도 쓸 수는 있지만, 누르기 전 진행은 빠진다
            //   → 새 캐릭터용으로 안내하고, 키우던 캐릭터는 '이전 진행 불러오기'로
            miReset = new ToolStripMenuItem("새 캐릭터로 시작 (진행 초기화)", null, (s2, e2) =>
            {
                if (!Confirm("이 프리셋의 진행 기록(캐릭터·레벨·포인트·퀘스트 진행)을 지우고 새 캐릭터로 시작할까요?\n"
                    + "이제 만드는 캐릭터가 2레벨이 되면 이 프리셋 캐릭터가 됩니다.\n\n"
                    + "이미 키우던 캐릭터라면 '이전 진행 불러오기'를 쓰세요 (지금까지의 진행도 채워집니다).")) return;
                ResetProgress?.Invoke(presetId);
                UpdateCharInfo();
            });
            charMenu.Items.Add(miReset);
            btnChar = new FlatButton("캐릭터", FlatButton.Kind.Ghost) { Margin = new Padding(0, 0, 10, 0) };
            btnChar.Click += (s2, e2) => charMenu.Show(btnChar, new Point(0, -charMenu.GetPreferredSize(Size.Empty).Height));
            btnRow.Controls.Add(btnChar);

            p.Controls.Add(btnRow);
            p.Controls.Add(logRow);
            return p;
        }

        // 삭제처럼 되돌릴 수 없는 동작은 한 번 더 묻는다
        // 로그에서 찾은 캐릭터 중 하나를 골라 이 프리셋의 진행으로 불러온다
        void PickCharacter()
        {
            if (ScanChars == null || RecoverProgress == null) return;
            List<LogWatcher.LogChar> chars;
            Cursor = Cursors.WaitCursor;
            try { chars = ScanChars(); }
            finally { Cursor = Cursors.Default; }
            if (chars == null) { MessageBox.Show(this, "게임 로그를 찾지 못했습니다.", "Siosa"); return; }
            if (chars.Count == 0) { MessageBox.Show(this, "로그에 레벨업 기록이 있는 캐릭터가 없습니다.", "Siosa"); return; }
            using (var f = new CharPickForm(chars))
            {
                if (f.ShowDialog(this) != DialogResult.OK || f.Picked == null) return;
                if (ProgressInfo(presetId) != null && !Confirm($"이 프리셋의 진행 기록을 지우고 {f.Picked.Name} 의 진행으로 바꿀까요?")) return;
                Cursor = Cursors.WaitCursor;
                try { RecoverProgress(presetId, f.Picked); }
                finally { Cursor = Cursors.Default; }
                UpdateCharInfo();
            }
        }

        bool Confirm(string message) =>
            Ask(message, MessageBoxButtons.YesNo, MessageBoxDefaultButton.Button2) == DialogResult.Yes;

        // 개발용(--savetest): 답을 미리 넣어 두면 창을 띄우지 않고 차례로 쓴다. 물은 내용은 DevAsked 에
        public static readonly Queue<DialogResult> DevAnswers = new Queue<DialogResult>();
        public static readonly List<string> DevAsked = new List<string>();
        public static bool DevQuiet;   // "저장했습니다" 알림도 띄우지 않음

        DialogResult Ask(string message, MessageBoxButtons buttons, MessageBoxDefaultButton def = MessageBoxDefaultButton.Button1)
        {
            if (DevAnswers.Count > 0) { DevAsked.Add(message.Split('\n')[0]); return DevAnswers.Dequeue(); }
            return MessageBox.Show(this, message, "Siosa", buttons, MessageBoxIcon.Question, def);
        }

        static FlatButton MakeButton(string text, Action onClick, bool primary = false, FlatButton.Kind? kind = null)
        {
            var b = new FlatButton(text, kind ?? (primary ? FlatButton.Kind.Primary : FlatButton.Kind.Normal));
            b.Click += (s, e) => onClick();
            return b;
        }

        // ── 탭: 퀘스트 보상 / 젬 구매 ──────────────────────────────
        // WinForms 기본 탭은 다크 테마가 안 먹으므로 버튼으로 화면을 바꾼다.

        FlatButton tabRewards, tabBuy, tabLinks, tabTree, tabTips, tabRegex;
        const int TabRewards = 0, TabBuy = 1, TabLinks = 2, TabTips = 3, TabTree = 4, TabRegex = 5;

        Control BuildTabs()
        {
            var p = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(14, 8, 14, 0), BackColor = Theme.Back };
            tabRewards = MakeButton("퀘스트 보상", () => ShowTab(TabRewards), kind: FlatButton.Kind.Tab);
            tabBuy = MakeButton("젬 구매", () => ShowTab(TabBuy), kind: FlatButton.Kind.Tab);
            tabLinks = MakeButton("링크", () => ShowTab(TabLinks), kind: FlatButton.Kind.Tab);
            tabTree = MakeButton("패시브 트리", () => ShowTab(TabTree), kind: FlatButton.Kind.Tab);
            tabTips = MakeButton("지역 팁", () => ShowTab(TabTips), kind: FlatButton.Kind.Tab);
            tabRegex = MakeButton("정규식", () => ShowTab(TabRegex), kind: FlatButton.Kind.Tab);
            foreach (var t in new[] { tabRewards, tabBuy, tabLinks, tabTree, tabTips, tabRegex }) { t.Height = 34; t.Margin = new Padding(0, 0, 2, 0); }
            p.Controls.Add(tabRewards);
            p.Controls.Add(tabBuy);
            p.Controls.Add(tabLinks);
            p.Controls.Add(tabTree);
            p.Controls.Add(tabTips);
            p.Controls.Add(tabRegex);
            ShowTab(TabRewards);
            return p;
        }

        // 개발용(--tabtime): 탭 전환 시간 (화면까지 다시 그린 시간)
        // 개발용(--flickertest): 폭을 1px 씩 바꾸며, 크기를 바꾼 뒤에도 탭 화면이 계속 다시 배치되는(스크롤바가
        // 붙었다 떨어지는) 폭을 찾는다. 바꾼 뒤 0.15초 동안 목록의 ClientSize 변화·배치 횟수를 센다.
        public string DevFlickerTest()
        {
            var sb = new System.Text.StringBuilder();
            void Pump(int ms) { var w = System.Diagnostics.Stopwatch.StartNew(); while (w.ElapsedMilliseconds < ms) { Application.DoEvents(); System.Threading.Thread.Sleep(5); } }
            ShowEditor();
            Pump(600);
            foreach (var (tab, name, target) in new[] { (TabRewards, "퀘스트 보상", (Control)list), (TabBuy, "젬 구매", buyList), (TabLinks, "링크", linkList), (TabTree, "패시브 트리", treeBox), (TabTips, "지역 팁", tipList) })
            {
                ShowTab(tab);
                Pump(200);
                int changes = 0, layouts = 0;
                EventHandler onClient = (s, e) => changes++;
                LayoutEventHandler onLayout = (s, e) => layouts++;
                target.ClientSizeChanged += onClient;
                target.Layout += onLayout;
                var bad = new List<string>();
                for (int w = 640; w <= 1300; w += 3)
                {
                    BeginDeferred(); Width = w; EndDeferred();   // 끌었다 놓은 것과 같게
                    changes = layouts = 0;
                    Pump(90);
                    if (changes > 1 || layouts > 2) bad.Add($"{w}(크기변화 {changes}, 배치 {layouts})");
                }
                target.ClientSizeChanged -= onClient;
                target.Layout -= onLayout;
                sb.AppendLine($"[{name}] 놓은 뒤에도 계속 바뀌는 폭 {bad.Count}개" + (bad.Count > 0 ? ": " + string.Join(", ", bad.Take(12)) : ""));
                Width = 900;
            }
            return sb.ToString();
        }

        // 개발용(--scrolltest): 창을 작게 → 맨 아래로 스크롤 → 창을 크게 (스크롤바가 사라질 만큼) 했을 때
        // 내용 맨 위가 화면 맨 위에 있는지 (DisplayRectangle.Y 가 0 이어야 함)
        public string DevScrollTest()
        {
            var sb = new System.Text.StringBuilder();
            void Pump(int ms) { var w = System.Diagnostics.Stopwatch.StartNew(); while (w.ElapsedMilliseconds < ms) { Application.DoEvents(); System.Threading.Thread.Sleep(5); } }
            ShowEditor();
            Pump(600);
            foreach (var (tab, name, sc) in new[] { (TabRewards, "퀘스트 보상", (ScrollableControl)list), (TabBuy, "젬 구매", buyList), (TabLinks, "링크", linkList), (TabTips, "지역 팁", tipList) })
            {
                ShowTab(tab);
                Size = new Size(900, 480);
                Pump(200);
                int contentH = sc.DisplayRectangle.Height;
                sc.AutoScrollPosition = new Point(0, contentH);   // 맨 아래로
                Pump(100);
                int scrolled = -sc.DisplayRectangle.Y;
                // 내용이 다 들어갈 만큼 키운다 (화면 높이까지). 실제 끌기처럼 20px 씩, 끌기 알림 있이/없이 번갈아
                int target = Math.Min(Screen.FromControl(this).WorkingArea.Height, Height + contentH);
                bool drag = tab == TabBuy || tab == TabLinks;
                if (drag) BeginDeferred();
                while (Height < target) { Height = Math.Min(target, Height + 20); Application.DoEvents(); }
                if (drag) EndDeferred();
                Pump(200);
                sb.Append(drag ? "(끌기) " : "(끌기 알림 없음) ");
                // 폭: 좁게(640) → 맨 아래 → 넓게. 카드가 덜 접혀 내용이 짧아지면서 스크롤바가 사라지는 경우
                Size = new Size(640, Math.Max(480, Math.Min(target, contentH / 2 + 300)));
                Pump(200);
                sc.AutoScrollPosition = new Point(0, sc.DisplayRectangle.Height);
                Pump(100);
                int scrolledW = -sc.DisplayRectangle.Y;
                if (drag) BeginDeferred();
                while (Width < 1500) { Width += 20; Application.DoEvents(); }
                if (drag) EndDeferred();
                Pump(200);
                int ft = sc.Controls.Count > 0 ? sc.Controls.Cast<Control>().Min(c => c.Top) : 0;
                sb.AppendLine($"  폭 넓힘: 내린 만큼 {scrolledW} → 세로 스크롤 {(sc.VerticalScroll.Visible ? "있음" : "없음")}, 첫 카드 위치 {ft} " + (!sc.VerticalScroll.Visible && ft < 0 ? "← 위가 잘림" : "OK"));
                int firstTop = sc.Controls.Count > 0 ? sc.Controls.Cast<Control>().Min(c => c.Top) : 0;
                sb.AppendLine($"[{name}] 내용 높이 {contentH}, 내린 만큼 {scrolled} → 키운 뒤 세로 스크롤 {(sc.VerticalScroll.Visible ? "있음" : "없음")}, 첫 카드 위치 {firstTop} " +
                              (!sc.VerticalScroll.Visible && firstTop < 0 ? "← 위가 잘림" : "OK"));
            }
            // 사용자 재현: 처음 실행 → 스크롤 안 함(맨 위) → 가로로 살짝 늘림. 단계마다 첫 카드 위치·높이·스크롤 상태
            foreach (var (tab, name, sc) in new[] { (TabBuy, "젬 구매", (ScrollableControl)buyList), (TabRewards, "퀘스트 보상", list), (TabLinks, "링크", linkList) })
            {
                DevNoScrollFix = true;   // 바로잡기 없이 원래 현상
                ShowTab(tab);
                Size = new Size(905, 750);
                Pump(300);
                sc.AutoScrollPosition = Point.Empty;
                Pump(100);
                var first = sc.Controls.Cast<Control>().Where(c => c.Visible).OrderBy(c => c.Top).First();
                int h0 = first.Height;
                var bad = new List<string>();
                for (int w = 905; w <= 1000; w += 3)
                {
                    BeginDeferred();
                    Width = w; Application.DoEvents();
                    EndDeferred();
                    Pump(60);
                    var f = sc.Controls.Cast<Control>().Where(c => c.Visible).OrderBy(c => c.Top).First();
                    int top = f.Top - f.Margin.Top;
                    if (top != sc.Padding.Top || f.Height < h0 / 2)
                        bad.Add($"{w}: 첫 카드 위치 {top}, 높이 {f.Height}/{h0}, 스크롤 {(sc.VerticalScroll.Visible ? "있음" : "없음")}, 스크롤값 {sc.VerticalScroll.Value}");
                }
                sb.AppendLine($"[가로 늘림·{name}] 이상한 폭 {bad.Count}개" + (bad.Count > 0 ? ": " + string.Join(" | ", bad.Take(4)) : ""));
                DevNoScrollFix = false;
            }

            // 실제와 같게: 마우스 휠로 내린 뒤 창을 끌어 키움. 바로잡기 끄고 → 켜고
            foreach (bool fixOn in new[] { false, true })
            {
                DevNoScrollFix = !fixOn;
                DevScrollFixes = 0;
                ShowTab(TabBuy);
                Size = new Size(900, 600);
                Pump(300);
                var pt = buyList.PointToScreen(new Point(buyList.Width / 2, buyList.Height / 2));
                for (int k = 0; k < 15; k++)
                {
                    Native.SendMessage(buyList.Handle, 0x020A, (IntPtr)unchecked((int)(-120 << 16)), (IntPtr)((pt.Y << 16) | (pt.X & 0xFFFF)));
                    Application.DoEvents();
                }
                Pump(200);
                int wheelScrolled = -buyList.DisplayRectangle.Y;
                BeginDeferred();
                while (Height < 900 || Width < 1240) { Height = Math.Min(900, Height + 20); Width = Math.Min(1240, Width + 30); Application.DoEvents(); }
                EndDeferred();
                Pump(300);
                int ft = buyList.Controls.Cast<Control>().Where(c => c.Visible).Min(c => c.Top - c.Margin.Top);
                sb.AppendLine($"[휠로 내림·바로잡기 {(fixOn ? "켬" : "끔")}] 내린 만큼 {wheelScrolled} → 세로 스크롤 {(buyList.VerticalScroll.Visible ? "있음" : "없음")}, 첫 카드 위치 {ft} " +
                              (!buyList.VerticalScroll.Visible && ft < 0 ? "← 위가 잘림" : "OK") + $" (바로잡은 횟수 {DevScrollFixes})");
            }
            DevNoScrollFix = false;

            // 밀린 상태 만들기: 젬 구매에서 스크롤을 내린 뒤 카드 대부분을 숨겨 내용을 짧게 → 스크롤바가 사라짐
            ShowTab(TabBuy);
            Size = new Size(900, 480);
            Pump(200);
            buyList.AutoScrollPosition = new Point(0, buyList.DisplayRectangle.Height);
            Pump(100);
            var hidden = buyList.Controls.Cast<Control>().Skip(1).ToList();
            foreach (var c in hidden) c.Visible = false;
            Pump(300);
            int ftop = buyList.Controls.Cast<Control>().Where(c => c.Visible).Min(c => c.Top - c.Margin.Top);
            sb.AppendLine($"[밀린 상태 만들기] 세로 스크롤 {(buyList.VerticalScroll.Visible ? "있음" : "없음")}, 첫 카드 위치 {ftop} " + (ftop < 0 ? "← 위가 잘림" : "OK (맨 위로 돌아옴)"));
            foreach (var c in hidden) c.Visible = true;
            Size = new Size(900, 760);
            return sb.ToString();
        }

        public string DevTimeTabs()
        {
            var sb = new System.Text.StringBuilder();
            var sw = new System.Diagnostics.Stopwatch();
            ShowEditor();
            Refresh();
            // 실제처럼: 편집 화면이 열리고 잠시 뒤 (미리 만들기가 끝난 뒤) 탭을 누른다
            var wait = System.Diagnostics.Stopwatch.StartNew();
            while (wait.ElapsedMilliseconds < 700) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
            for (int i = 1; i <= 3; i++)
            {
                sw.Restart(); ShowTab(TabBuy); long a1 = sw.ElapsedMilliseconds; Refresh(); Application.DoEvents(); long a = sw.ElapsedMilliseconds;
                sw.Restart(); ShowTab(TabRewards); long b1 = sw.ElapsedMilliseconds; Refresh(); Application.DoEvents(); long b = sw.ElapsedMilliseconds;
                sb.AppendLine($"{i}회: 보상→구매 {a}ms (전환 {a1}ms + 그리기 {a - a1}ms), 구매→보상 {b}ms (전환 {b1}ms)");
            }
            // 창 크기 바꾸기: 탭마다 폭을 20px 씩 10번 늘였다 줄이며 한 번에 걸리는 시간 (레이아웃 + 그리기)
            foreach (var (tab, name) in new[] { (TabRewards, "퀘스트 보상"), (TabBuy, "젬 구매"), (TabLinks, "링크"), (TabTree, "패시브 트리"), (TabTips, "지역 팁") })
            {
                ShowTab(tab);
                Refresh(); Application.DoEvents();
                var times = new List<long>();
                OnResizeBegin(EventArgs.Empty);   // 가장자리를 끄는 것과 같게
                for (int k = 0; k < 20; k++)
                {
                    sw.Restart();
                    Width += k < 10 ? 20 : k < 15 ? -20 : -10;   // 놓을 때 처음보다 50px 넓음
                    Refresh(); Application.DoEvents();
                    times.Add(sw.ElapsedMilliseconds);
                }
                sw.Restart();
                OnResizeEnd(EventArgs.Empty);
                Refresh(); Application.DoEvents();
                sb.AppendLine($"크기 바꾸기 [{name}] 끄는 동안 평균 {times.Average():0}ms, 최대 {times.Max()}ms · 놓을 때 {sw.ElapsedMilliseconds}ms");
                sw.Restart();
                WindowState = FormWindowState.Maximized; Refresh(); Application.DoEvents();
                long mx = sw.ElapsedMilliseconds;
                sw.Restart();
                WindowState = FormWindowState.Normal; Refresh(); Application.DoEvents();
                sb.AppendLine($"    최대화 {mx}ms · 복원 {sw.ElapsedMilliseconds}ms");
                sw.Restart();
                foreach (bool off in new[] { true, false })
                {
                    DevNoMinFix = off;
                    sw.Restart();
                    WindowState = FormWindowState.Minimized; Application.DoEvents();
                    long mn = sw.ElapsedMilliseconds;
                    sw.Restart();
                    WindowState = FormWindowState.Normal; Refresh(); Application.DoEvents();
                    sb.AppendLine($"    {(off ? "예전" : "수정")} 최소화 {mn}ms · 다시 열기 {sw.ElapsedMilliseconds}ms");
                }
                DevNoMinFix = false;
            }
            return sb.ToString();
        }

        // 개발용(--savetest): 새로 저장 → 이름 바꾸기 → 복사본 → 같은 이름 덮어쓰기(아니요/예) → 저장 안 하고 시작
        public string DevSaveTest()
        {
            var sb = new System.Text.StringBuilder();
            DevQuiet = true;
            string Files() => string.Join(", ", Directory.GetFiles(profilesDir, "*.json").Select(f => $"{Path.GetFileNameWithoutExtension(f)}={Profile.Load(f).id}").OrderBy(x => x));
            void Step(string what, params DialogResult[] answers)
            {
                DevAsked.Clear();
                foreach (var a in answers) DevAnswers.Enqueue(a);
                sb.AppendLine($"[{what}] 물음: {(DevAsked.Count == 0 ? "-" : "")}");
            }
            void After(string what)
            {
                sb.AppendLine($"    물음 {DevAsked.Count}개: {string.Join(" / ", DevAsked)}");
                sb.AppendLine($"    파일: {Files()}   편집 중 ID {presetId}");
                DevAnswers.Clear();
            }
            ClearEditor();
            tbName.Text = "가"; Step("새로 저장 '가'"); SaveProfile(); After("");
            string idA = presetId;
            tbName.Text = "나"; Step("'가'→'나' 이름 바꾸기 (예)", DialogResult.Yes); SaveProfile(); After("");
            sb.AppendLine($"    ID 그대로: {presetId == idA}");
            tbName.Text = "다"; Step("'나'→'다' 복사본 (아니요)", DialogResult.No); SaveProfile(); After("");
            sb.AppendLine($"    새 ID: {presetId != idA}");
            ClearEditor();
            tbName.Text = "나"; Step("새 프리셋을 '나'로 저장, 덮어쓰기 아니요", DialogResult.No); bool saved = SaveProfile(); After("");
            sb.AppendLine($"    저장됨: {saved}");
            Step("다시 저장, 덮어쓰기 예", DialogResult.Yes); saved = SaveProfile(); After("");
            sb.AppendLine($"    저장됨: {saved}");
            Step("변경 없이 다시 저장"); SaveProfile(); After("");
            DevQuiet = false;
            return sb.ToString();
        }

        // 개발용(--tabshot): 실제 화면 캡처로 두 탭을 저장 (DrawToBitmap 은 겹친 컨트롤 순서를 거꾸로 그린다)
        // 개발용(--combotest): 클래스 드롭다운을 열고 바깥을 실제로 클릭해 닫기를 반복, 그다음 항목 고르기
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern void mouse_event(int flags, int dx, int dy, int data, IntPtr extra);
        public string DevComboTest()
        {
            var log = new System.Text.StringBuilder();
            void Pump(int ms) { var w = System.Diagnostics.Stopwatch.StartNew(); while (w.ElapsedMilliseconds < ms) { Application.DoEvents(); System.Threading.Thread.Sleep(10); } }
            ShowEditor();
            Pump(300);
            // 저장 안 한 변경 감지: 연 직후·탭을 다 만든 뒤에는 변경 없음, 보상 하나 바꾸면 변경 있음
            log.AppendLine($"unsaved after open: {Unsaved}");
            ShowTab(TabBuy); Pump(200); ShowTab(TabLinks); Pump(200); ShowTab(TabRewards); Pump(100);
            log.AppendLine($"unsaved after tabs: {Unsaved}");
            var chip = buttons.Values.First().First(c => !c.Selected);
            OnPick(buttons.Keys.First(), chip);
            log.AppendLine($"unsaved after pick: {Unsaved}");
            // 지역 팁: 고치면 settings.tips 에 들어가고, 기본값으로 되돌리면 빠진다. 프리셋 변경으로는 치지 않는다
            ShowTab(TabTips); Pump(200);
            var tb = tipBoxes.First(b => b.Box.Text.Length > 0);   // 기본 팁이 있는 첫 지역 (마을은 기본 팁이 없음)
            string tipBefore = tb.Box.Text;
            tb.Box.Text = "테스트 팁";
            log.AppendLine($"tip edited: count={settings.tips.Count} value={settings.tips.Values.FirstOrDefault()} overlay={data.TipFor(settings, settings.tips.Keys.FirstOrDefault())}");
            tb.Box.Text = "";
            log.AppendLine($"tip cleared: count={settings.tips.Count} hidden={(data.TipFor(settings, settings.tips.Keys.FirstOrDefault()) == "" ? "yes" : "no")}");
            tb.Box.Text = tipBefore;
            log.AppendLine($"tip restored: count={settings.tips.Count}");
            ShowTab(TabRewards); Pump(100);
            OnPick(buttons.Keys.First(), chip);
            var saved = Cursor.Position;
            try
            {
                for (int i = 1; i <= 5; i++)
                {
                    cbClass.DevOpen();
                    Pump(200);
                    // 메뉴 바깥(카드 영역)을 실제로 클릭 → Windows 가 메뉴를 닫는다 (예전에 꺼지던 경로)
                    Cursor.Position = PointToScreen(new Point(Width / 2, Height - 200));
                    mouse_event(0x02, 0, 0, 0, IntPtr.Zero);
                    mouse_event(0x04, 0, 0, 0, IntPtr.Zero);
                    Pump(250);
                    log.AppendLine($"{i}회 열고 바깥 클릭으로 닫기: OK");
                }
                string before = SelectedClass?.ko;
                cbClass.DevOpen();
                Pump(200);
                cbClass.DevPick(3);
                Pump(400);
                log.AppendLine($"항목 고르기: {before} → {SelectedClass?.ko}");
                cbClass.DevOpen();
                Pump(200);
                cbClass.DevPick(0);
                Pump(400);
                log.AppendLine($"다시 고르기: → {SelectedClass?.ko}");

                // 젬 검색: ✕ 클릭 / Esc 로 닫기, 추가 후 결과 유지
                ShowTab(TabBuy);
                Activate();
                tbSearch.Text = "화염";
                Pump(300);
                var clear = searchBox.Controls.Cast<Control>().First(c => c != tbSearch);
                log.AppendLine($"검색 '화염': 결과 카드 {(searchCard != null ? "있음" : "없음")}, ✕ {(clear.Visible ? "보임" : "안 보임")}");
                Cursor.Position = clear.PointToScreen(new Point(clear.Width / 2, clear.Height / 2));
                mouse_event(0x02, 0, 0, 0, IntPtr.Zero);
                mouse_event(0x04, 0, 0, 0, IntPtr.Zero);
                Pump(300);
                log.AppendLine($"✕ 클릭 후: 검색어 '{tbSearch.Text}', 결과 카드 {(searchCard != null ? "있음" : "없음")}, ✕ {(clear.Visible ? "보임" : "안 보임")}");

                tbSearch.Text = "fire";
                tbSearch.Focus();
                Pump(200);
                SendKeys.SendWait("{ESC}");
                Pump(300);
                log.AppendLine($"Esc 후: 검색어 '{tbSearch.Text}', 결과 카드 {(searchCard != null ? "있음" : "없음")}, 창 {(Visible ? "열려 있음" : "닫힘")}");

                tbSearch.Text = "화염";
                Pump(300);
                var addBtn = searchCard.Body.Controls.Cast<Control>().SelectMany(r => r.Controls.Cast<Control>()).OfType<FlatButton>().First(b => b.Enabled);
                string addedName = addBtn.Parent.Controls[0].Text;
                addBtn.PerformClick();
                Pump(400);
                var sameRow = searchCard?.Body.Controls.Cast<Control>().FirstOrDefault(r => r.Controls.Count > 0 && r.Controls[0].Text == addedName);
                var btnNow = sameRow?.Controls.OfType<FlatButton>().FirstOrDefault();
                log.AppendLine($"'{addedName}' 추가 후: 결과 카드 {(searchCard != null ? "그대로 있음" : "닫힘")}, 버튼 '{btnNow?.Text}', 구매 목록에 {(buy.Any(id => data.GemName(id) == addedName) ? "들어감" : "없음")}");
            }
            catch (Exception ex) { log.AppendLine("오류: " + ex); }
            finally { Cursor.Position = saved; }
            return log.ToString();
        }

        public void DevShotTabs(string dir)
        {
            // 메인 화면
            ShowHome();
            Refresh();
            var w0 = System.Diagnostics.Stopwatch.StartNew();
            while (w0.ElapsedMilliseconds < 2500) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
            using (var bmp = new Bitmap(Width, Height))
            {
                using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(Location, Point.Empty, Size);
                bmp.Save(Path.Combine(dir, "home.png"));
            }
            ShowEditor();
            regexCustom = "이동 속도|생명력"; regexOn = new HashSet<string> { "link4", "move", "add:물리", "add:화염", "gemlvl:번개" }; SyncRegexUi();   // 정규식 탭 예시
            foreach (var (tab, name) in new[] { (TabRewards, "tab_rewards.png"), (TabBuy, "tab_buy.png"), (TabLinks, "tab_links.png"), (TabTree, "tab_tree.png"), (TabTips, "tab_tips.png"), (TabRegex, "tab_regex.png") })
            {
                ShowTab(tab);
                Refresh();
                var wait = System.Diagnostics.Stopwatch.StartNew();
                while (wait.ElapsedMilliseconds < 300) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                using (var bmp = new Bitmap(Width, Height))
                {
                    using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(Location, Point.Empty, Size);
                    bmp.Save(Path.Combine(dir, name));
                }
            }
            // 업데이트 진행 창
            using (var up = new UpdateForm("1.0.1") { StartPosition = FormStartPosition.Manual, Location = new Point(Left + 200, Top + 200) })
            {
                up.Show(this);
                up.SetProgress("받는 중… 45%  (330 / 732 KB)", 40);
                up.Refresh();
                var wu = System.Diagnostics.Stopwatch.StartNew();
                while (wu.ElapsedMilliseconds < 300) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                using (var bmp = new Bitmap(up.Width, up.Height))
                {
                    using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(up.Location, Point.Empty, up.Size);
                    bmp.Save(Path.Combine(dir, "update.png"));
                }
            }
            // 정규식 탭 아래쪽 (자주 쓰는 정규식)
            ShowTab(TabRegex);
            regexList.AutoScrollPosition = new Point(0, 10000);
            Refresh();
            var waitR = System.Diagnostics.Stopwatch.StartNew();
            while (waitR.ElapsedMilliseconds < 300) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
            using (var bmp = new Bitmap(Width, Height))
            {
                using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(Location, Point.Empty, Size);
                bmp.Save(Path.Combine(dir, "tab_regex2.png"));
            }
            // 보상 탭 2장 (도적 선택 줄)
            ShowTab(TabRewards);
            var act2 = list.Controls.OfType<Card>().FirstOrDefault(c => c.Body.Controls.Count > 0 && c.Body.Controls[0].Text == "액트 2");
            if (act2 != null) list.AutoScrollPosition = new Point(0, act2.Top - list.AutoScrollPosition.Y - 8);
            Refresh();
            var w1 = System.Diagnostics.Stopwatch.StartNew();
            while (w1.ElapsedMilliseconds < 300) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
            using (var bmp = new Bitmap(Width, Height))
            {
                using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(Location, Point.Empty, Size);
                bmp.Save(Path.Combine(dir, "tab_rewards_act2.png"));
            }
            // 구매 탭 + 검색 결과
            ShowTab(TabBuy);
            tbSearch.Text = "화염";
            Refresh();
            var w2 = System.Diagnostics.Stopwatch.StartNew();
            while (w2.ElapsedMilliseconds < 300) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
            using (var bmp = new Bitmap(Width, Height))
            {
                using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(Location, Point.Empty, Size);
                bmp.Save(Path.Combine(dir, "tab_buy_search.png"));
            }
            tbSearch.Text = "";
            // 젬 고르기 창
            using (var picker = new GemPickerForm(data, SelectedClass?.id) { StartPosition = FormStartPosition.Manual, Location = new Point(Left + 100, Top + 100), TopMost = true })
            {
                picker.Show(this);
                picker.DevSearch("화염");
                picker.Refresh();
                var wait = System.Diagnostics.Stopwatch.StartNew();
                while (wait.ElapsedMilliseconds < 300) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                using (var bmp = new Bitmap(picker.Width, picker.Height))
                {
                    using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(picker.Location, Point.Empty, picker.Size);
                    bmp.Save(Path.Combine(dir, "gem_picker.png"));
                }
                picker.Close();
            }
            // 단축키 변경 창 (한 칸을 바꾸는 중)
            using (var hk = new HotkeyForm(settings) { StartPosition = FormStartPosition.Manual, Location = new Point(Left + 100, Top + 100), TopMost = true })
            {
                hk.Show(this);
                hk.DevCapture(Hotkeys.Links);
                hk.Refresh();
                var wait = System.Diagnostics.Stopwatch.StartNew();
                while (wait.ElapsedMilliseconds < 300) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                using (var bmp = new Bitmap(hk.Width, hk.Height))
                {
                    using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(hk.Location, Point.Empty, hk.Size);
                    bmp.Save(Path.Combine(dir, "hotkeys.png"));
                }
                hk.Close();
            }
        }

        // 두 화면을 겹쳐 두고 앞으로 가져오기만 한다. Visible 을 끄면 보상 버튼 수백 개에 표시 변경이 전달돼서
        // 구매 탭으로 넘어갈 때 0.2초 넘게 걸렸다.
        bool buyShown;

        void ShowTab(int tab)
        {
            buyShown = tab == TabBuy;
            linksShown = tab == TabLinks;
            if (buyShown && buyDirty) RebuildBuy();   // 앞으로 가져오기 전에 만들어 두어 빈 화면이 보이지 않게
            if (linksShown && linkDirty) RebuildLinks();
            if (tab == TabTips && !tipsBuilt) RebuildTips();   // 지역이 많아(150여 곳) 처음 열 때 만든다
            treesShown = tab == TabTree;
            tipsShownNow = tab == TabTips;
            regexShown = tab == TabRegex;
            if (regexShown) { SyncRegexUi(); UpdateRegexPreview(); }   // 구매 목록·고른 보상이 바뀌었을 수 있다
            var panel = tab == TabBuy ? (Control)buyPanel : tab == TabLinks ? linkPanel : tab == TabTips ? tipPanel : tab == TabTree ? treePanel : tab == TabRegex ? regexPanel : list;
            LayoutPanel(panel);
            if (treesShown && treeDirty) RebuildTrees();
            panel.BringToFront();
            foreach (var (b, on) in new[] { (tabRewards, tab == TabRewards), (tabBuy, tab == TabBuy), (tabLinks, tab == TabLinks), (tabTree, tab == TabTree), (tabTips, tab == TabTips), (tabRegex, tab == TabRegex) })
                b.Selected = on;
        }

        // ── 패시브 트리 ──────────────────────────────────────────
        // PoB 트리(Spec)들을 단계로 보여 준다. 대략적인 모양만: 찍은 노드 금색, 앞 단계보다 새로 찍은 노드 초록.
        // 고른 단계(★)가 게임 중 오버레이 미니맵에 나온다. 노드 편집은 하지 않는다 (PoB 에서 고쳐서 다시 불러오기).

        readonly Panel treePanel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Back };
        readonly FlowLayoutPanel treeBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(14, 10, 14, 6), WrapContents = true, BackColor = Theme.Back,
        };
        readonly Label lbTreeInfo = new Label { Dock = DockStyle.Top, Height = 26, Padding = new Padding(16, 4, 0, 0), ForeColor = Theme.Dim, BackColor = Theme.Back };
        readonly TreeBox treeBox = new TreeBox { Dock = DockStyle.Fill };
        List<TreeStage> trees = new List<TreeStage>();
        int treeStage;
        bool treesShown, treeDirty = true;

        void BuildTreePanel()
        {
            var wrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 4, 14, 14), BackColor = Theme.Back };
            wrap.Controls.Add(treeBox);
            treePanel.Controls.Add(wrap);
            treePanel.Controls.Add(lbTreeInfo);
            treePanel.Controls.Add(treeBar);
            treeBox.Empty = data.Tree == null ? "트리 데이터(data/tree.json)가 없습니다." : "PoB 를 불러오면 패시브 트리 단계가 여기에 표시됩니다.";
        }

        void RebuildTrees()
        {
            treeDirty = false;
            treeBar.SuspendLayout();
            foreach (Control c in treeBar.Controls) c.Dispose();
            treeBar.Controls.Clear();
            if (trees.Count > 0) treeStage = Math.Max(0, Math.Min(treeStage, trees.Count - 1));
            for (int i = 0; i < trees.Count; i++)
            {
                int idx = i;
                var b = new FlatButton((i == treeStage ? "★ " : "") + trees[i].name, i == treeStage ? FlatButton.Kind.Primary : FlatButton.Kind.Normal) { Margin = new Padding(0, 0, 6, 6) };
                tip.SetToolTip(b, "누르면 이 단계를 오버레이 미니맵에 표시");
                b.Click += (s, e) => { treeStage = idx; RebuildTrees(); };
                treeBar.Controls.Add(b);
            }
            if (trees.Count > 0)
            {
                var del = new FlatButton("단계 삭제", FlatButton.Kind.Danger) { Margin = new Padding(12, 0, 0, 6) };
                del.Click += (s, e) =>
                {
                    if (!Confirm($"'{trees[treeStage].name}' 트리 단계를 삭제할까요?")) return;
                    trees.RemoveAt(treeStage);
                    treeStage = Math.Max(0, treeStage - 1);
                    RebuildTrees();
                };
                treeBar.Controls.Add(del);
            }
            treeBar.ResumeLayout();

            if (trees.Count == 0 || data.Tree == null)
            {
                lbTreeInfo.Text = "";
                treeBox.SetTree(null, null, null);
                return;
            }
            var cur = trees[treeStage];
            var prev = treeStage > 0 ? trees[treeStage - 1] : null;
            int n = TreeRender.Count(data.Tree, cur.nodes);
            int added = prev == null ? n : TreeRender.Count(data.Tree, cur.nodes.Except(prev.nodes));
            lbTreeInfo.Text = $"{cur.name} · 노드 {n}개" + (prev != null ? $" (앞 단계보다 새로 {added}개, 초록)" : "") +
                              $"   ·   게임 중 {Hotkeys.Text(settings, Hotkeys.Tree)} 트리 창 · {Hotkeys.Short(settings, Hotkeys.PrevTree)}/{Hotkeys.Short(settings, Hotkeys.NextTree)} 이전/다음 단계 · {Hotkeys.Short(settings, Hotkeys.TreeSmaller)}/{Hotkeys.Short(settings, Hotkeys.TreeBigger)} 크기";
            treeBox.SetTree(data.Tree, cur, prev);
        }

        // ── 정규식 ───────────────────────────────────────────────
        // 상점 검색칸에 붙여 넣을 정규식. 게임 중 단축키로 클립보드에 복사 → 검색칸에서 Ctrl+V (키 입력은 보내지 않는다).
        //   카테고리마다 따옴표로 묶어 | 로 잇는다: "직접 입력"|"이 액트 구매 젬"|"자주 쓰는 정규식"
        //   구매 젬은 게임 중에는 지금 액트 기준, 여기 미리보기는 고른 액트 기준.

        readonly Panel regexPanel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Back };
        readonly FlowLayoutPanel regexList = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            AutoScroll = true, BackColor = Theme.Back, Padding = new Padding(14, 10, 14, 14),
        };
        bool regexShown, regexSyncing;
        string regexCustom = "";
        HashSet<string> regexOn = new HashSet<string>();
        readonly FieldBox regexBox = new FieldBox(500);
        readonly Label lbBuyKeys = new Label { AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0, 0, 0, 4) };
        readonly Dictionary<string, FlatCheck> regexChecks = new Dictionary<string, FlatCheck>();
        readonly Label lbRegexParts = new Label { AutoSize = true, ForeColor = Theme.Dim, Font = new Font("Consolas", 9.5f), Margin = new Padding(0, 6, 0, 0) };   // 맑은 고딕은  를 ₩ 로 그린다
        readonly Label lbRegexOut = new Label { AutoSize = true, ForeColor = Theme.Text, Font = new Font("Consolas", 10f), Margin = new Padding(0, 8, 0, 0) };
        readonly Label lbRegexLen = new Label { AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
        readonly Label lbRegexKeys = new Label { AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0, 2, 0, 6) };

        Profile RegexProfile() => new Profile
        {
            classId = SelectedClass?.id ?? "", picks = picks, buy = buy, buyOff = buyOff.ToList(),
            regexCustom = regexCustom, regexPresets = regexOn.ToList(),
        };

        void BuildRegexPanel()
        {
            // 카드 본문에 바로 넣으면 높이 계산에서 빠져 보이지 않는다 → 다른 탭처럼 줄(FlowLayoutPanel)에 넣는다
            FlowLayoutPanel Wrap(Control c) { var row = CardRow(0); row.WrapContents = false; row.Controls.Add(c); return row; }
            Card RCard(string title, string sub)
            {
                var c = new Card();
                c.Body.Controls.Add(new Label { Text = title, Font = Theme.Header, ForeColor = Theme.Accent, AutoSize = true, Margin = new Padding(0, 0, 0, 2) });
                if (sub != null) c.Body.Controls.Add(new Label { Text = sub, ForeColor = Theme.Dim, AutoSize = true, Margin = new Padding(0, 0, 0, 6) });
                return c;
            }

            // 미리보기 (맨 위)
            var preview = RCard("미리보기", null);
            preview.Body.Controls.Add(lbRegexKeys);
            preview.Body.Controls.Add(lbRegexParts);
            preview.Body.Controls.Add(lbRegexOut);
            preview.Body.Controls.Add(lbRegexLen);

            // 직접 입력
            var custom = RCard("직접 입력", "양 끝 따옴표는 Siosa 가 붙입니다.");
            regexBox.Box.TextChanged += (s, e) => { if (regexSyncing) return; regexCustom = regexBox.Box.Text; UpdateRegexPreview(); };
            custom.Body.Controls.Add(Wrap(regexBox));

            // 구매 젬 (맨 위, 단축키 따로)
            var buyCard = RCard("구매 젬", null);
            buyCard.Body.Controls.Add(lbBuyKeys);
            buyCard.Body.Controls.Add(new Label
            {
                Text = "지금 있는 액트에서 살 젬 이름만 복사합니다 ('젬 구매' 탭에서 체크한 젬, 오버레이의 '구매' 줄과 같은 목록).",
                ForeColor = Theme.Dim, AutoSize = true,
            });

            // 자주 쓰는 정규식
            var pre = RCard("자주 쓰는 정규식", "레벨링 중 상점에서 많이 찾는 옵션 · # 으로 표시된 옵션은 수치 상관없이 전체 강조");
            if (data.RegexPresets.Count == 0)
                pre.Body.Controls.Add(new Label { Text = "regex.json 이 없거나 읽지 못했습니다 (형식 확인).", ForeColor = Color.FromArgb(235, 110, 90), AutoSize = true });
            foreach (var x in data.RegexPresets)
            {
                var row = CardRow(0);
                row.WrapContents = false;
                // 체크 하나 = 키 하나. 묶음은 이름 + 선택지마다 체크 (식은 보여 주지 않는다. 합친 결과는 미리보기에)
                void AddCheck(string text, string key, int minWidth)
                {
                    var cb = new FlatCheck(text) { Margin = new Padding(0, 0, 8, 0) };
                    cb.Width = Math.Max(cb.Width, minWidth);
                    cb.CheckedChanged += (s, e) =>
                    {
                        if (regexSyncing) return;
                        if (cb.Checked) regexOn.Add(key); else regexOn.Remove(key);
                        UpdateRegexPreview();
                    };
                    regexChecks[key] = cb;
                    if (!string.IsNullOrEmpty(x.note)) tip.SetToolTip(cb, x.note);
                    row.Controls.Add(cb);
                }
                if (x.IsGroup)
                {
                    var name = new Label { Text = x.name, AutoSize = false, Size = new Size(230, 28), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Margin = new Padding(0, 0, 8, 0) };
                    if (!string.IsNullOrEmpty(x.note)) tip.SetToolTip(name, x.note);
                    row.Controls.Add(name);
                    foreach (var o in x.options) AddCheck(o, x.Key(o), 0);
                }
                else AddCheck(x.name, x.id, 230);
                pre.Body.Controls.Add(row);
            }

            foreach (var c in new[] { buyCard, preview, custom, pre }) regexList.Controls.Add(c);
            regexList.Resize += (s, e) => { if (!resizing) FitRegex(); };
            regexPanel.Controls.Add(regexList);
            KeepTopVisible(regexList);
            UpdateHotkeyTexts();
        }

        void FitRegex()
        {
            int w = CardWidthOf(regexList);
            regexBox.Width = Math.Max(200, w - 28 - 6);
            FitCards(regexList);
        }

        // 프리셋을 열거나 새로 만들 때 화면을 값에 맞춘다
        void SyncRegexUi()
        {
            if (regexChecks.Count == 0) return;   // 아직 탭을 만들기 전
            regexSyncing = true;
            try
            {
                if (regexBox.Box.Text != regexCustom) regexBox.Box.Text = regexCustom;
                foreach (var kv in regexChecks) kv.Value.Checked = regexOn.Contains(kv.Key);
            }
            finally { regexSyncing = false; }
            UpdateRegexPreview();
        }

        void UpdateRegexPreview()
        {
            if (regexChecks.Count == 0) return;
            var prof = RegexProfile();
            var parts = RegexSearch.Parts(data, prof);
            var text = RegexSearch.Build(data, prof);
            lbRegexParts.Text = parts.Count == 0 ? "넣을 정규식이 없습니다. 아래에서 입력하거나 고르세요."
                : string.Join("\n", parts.Select(x => $"· {x.label}: {x.part}"));
            lbRegexOut.Text = text.Length == 0 ? "(비어 있음)" : text;
            bool over = text.Length > RegexSearch.MaxLength;
            lbRegexLen.Text = $"{text.Length} / {RegexSearch.MaxLength}자" + (over ? " — 너무 깁니다. 게임 검색칸에서 뒤가 잘릴 수 있습니다" : "");
            lbRegexLen.ForeColor = over ? Color.FromArgb(235, 110, 90) : Theme.Dim;
            FitCards(regexList);
        }

        // ── 지역 팁 ──────────────────────────────────────────────
        // 지역에 들어가면 오버레이의 경험치 패널티 아래에 한 줄로 보여 주는 팁. 기본값은 data/tips.json,
        // 고친 것만 settings.tips 에 저장한다 (모든 프리셋 공통, 비우면 그 지역은 팁을 숨김).

        readonly Panel tipPanel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Back };
        readonly FlowLayoutPanel tipList = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            AutoScroll = true, BackColor = Theme.Back, Padding = new Padding(14, 4, 14, 14),
        };
        bool tipsBuilt;
        readonly List<FieldBox> tipBoxes = new List<FieldBox>();
        const int TipLabelWidth = 170, TipResetWidth = 64;

        void BuildTipPanel()
        {
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(14, 10, 14, 0), WrapContents = false, BackColor = Theme.Back };
            var resetAll = new FlatButton("모두 기본값", FlatButton.Kind.Normal) { Margin = new Padding(0) };
            resetAll.Click += (s, e) =>
            {
                if (settings.tips == null || settings.tips.Count == 0) return;
                if (!Confirm($"고친 팁 {settings.tips.Count}개를 모두 기본값으로 되돌릴까요?")) return;
                settings.tips.Clear();
                RebuildTips();
            };
            bar.Controls.Add(resetAll);
            bar.Controls.Add(lbTipKeys);
            tipList.Resize += (s, e) => { if (!resizing) FitTips(); };
            tipPanel.Controls.Add(tipList);
            tipPanel.Controls.Add(bar);
        }

        // 입력칸 폭을 창 폭에 맞춘다 (이름 칸·기본값 버튼을 뺀 나머지)
        void FitTips()
        {
            int w = Math.Max(160, CardWidthOf(tipList) - 32 - TipLabelWidth - TipResetWidth - 12);
            if (tipBoxes.Count > 0 && tipBoxes[0].Width == w) { FitCards(tipList); return; }
            // 입력칸 150여 개의 폭을 바꾸는 동안은 배치·그리기를 멈췄다가 한 번에 (칸마다 다시 배치하면 0.4초)
            Native.SendMessage(tipList.Handle, Native.WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            tipList.SuspendLayout();
            var rows = tipBoxes.Select(b => b.Parent).Where(p => p != null).Distinct().ToList();
            foreach (var r in rows) r.SuspendLayout();
            foreach (var b in tipBoxes) b.Width = w;
            foreach (var r in rows) r.ResumeLayout(false);
            FitCards(tipList);
            tipList.ResumeLayout(true);
            Native.SendMessage(tipList.Handle, Native.WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
            tipList.Invalidate(true);
        }

        void RebuildTips()
        {
            tipsBuilt = true;
            if (settings.tips == null) settings.tips = new Dictionary<string, string>();
            Native.SendMessage(tipList.Handle, Native.WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            tipList.SuspendLayout();
            foreach (Control c in tipList.Controls) c.Dispose();
            tipList.Controls.Clear();
            tipBoxes.Clear();

            // 액트마다 카드 하나, 마을이 맨 위·나머지는 레벨 순 (숨겨진 작은 지역·미궁 입구 등 레벨 1 지역은 뺀다)
            //   (황혼의 해안은 레벨 1 이지만 기본 팁이 있어 넣는다)
            foreach (var act in data.Areas.Where(kv => kv.Value.act >= 1 && kv.Value.act <= 10 && (kv.Value.level > 1 || (data.Tips?.ContainsKey(kv.Key) ?? false)))
                                          .GroupBy(kv => kv.Value.act).OrderBy(g => g.Key))
            {
                var card = new Card();
                card.Body.Controls.Add(new Label { Text = $"액트 {act.Key}", Font = Theme.Header, ForeColor = Theme.Accent, AutoSize = true, Margin = new Padding(0, 0, 0, 4) });
                foreach (var kv in act.OrderBy(kv => kv.Value.town ? 0 : 1).ThenBy(kv => kv.Value.level))
                {
                    string id = kv.Key;
                    string def = data.Tips != null && data.Tips.TryGetValue(id, out var d) ? d : "";
                    var row = CardRow(2);
                    row.WrapContents = false;
                    row.Controls.Add(new Label
                    {
                        Text = kv.Value.ko + (kv.Value.town ? " (마을)" : ""), AutoSize = false, Size = new Size(TipLabelWidth, 28),
                        TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Margin = new Padding(0, 0, 6, 0),
                        ForeColor = kv.Value.town ? Theme.Accent : Theme.Text,
                    });
                    var box = new FieldBox(400);
                    box.Box.Text = settings.tips.TryGetValue(id, out var mine) ? mine : def;
                    tipBoxes.Add(box);
                    row.Controls.Add(box);
                    var reset = new FlatButton("기본값", FlatButton.Kind.Ghost, TipResetWidth) { Margin = new Padding(0), Visible = settings.tips.ContainsKey(id) };
                    tip.SetToolTip(reset, def.Length > 0 ? "기본 팁: " + def : "기본 팁 없음");
                    reset.Click += (s, e) => { box.Box.Text = def; };
                    row.Controls.Add(reset);
                    // 기본값과 같으면 저장하지 않는다 (나중에 기본 팁이 바뀌면 그대로 따라가도록)
                    box.Box.TextChanged += (s, e) =>
                    {
                        var text = box.Box.Text.Trim();
                        if (text == def) settings.tips.Remove(id); else settings.tips[id] = text;
                        reset.Visible = settings.tips.ContainsKey(id);
                    };
                    card.Body.Controls.Add(row);
                }
                AddCard(tipList, card);
            }
            FitTips();
            tipList.ResumeLayout();
            Native.SendMessage(tipList.Handle, Native.WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
            tipList.Invalidate(true);
        }

        // ── 링크 ─────────────────────────────────────────────────
        // 단계(Act 1, 8Lv …)마다 링크 묶음. PoB 에서 가져오고, 이름 바꾸기·단계/묶음/젬 추가·삭제로 직접 고칠 수 있다.
        // 오버레이는 고른 단계 하나를 보여 준다 (게임 중에는 Ctrl+Shift+K 로 다음 단계).

        readonly Panel linkPanel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Back };
        readonly FlowLayoutPanel linkList = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            AutoScroll = true, BackColor = Theme.Back, Padding = new Padding(8),
        };
        bool linksShown, linkDirty = true;

        void BuildLinkPanel()
        {
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(14, 10, 14, 0), WrapContents = false, BackColor = Theme.Back };
            var add = new FlatButton("+ 단계 추가", FlatButton.Kind.Primary) { Margin = new Padding(0) };
            add.Click += (s, e) =>
            {
                links.Add(new LinkStage { name = $"단계 {links.Count + 1}", groups = new List<SkillGroup> { new SkillGroup() } });
                RebuildLinks();
            };
            bar.Controls.Add(add);
            bar.Controls.Add(lbLinkKeys);
            linkList.Padding = new Padding(14, 4, 14, 14);
            linkList.Resize += (s, e) => { if (!resizing) FitCards(); };
            linkPanel.Controls.Add(linkList);
            linkPanel.Controls.Add(bar);
        }

        int CardWidth => CardWidthOf(linkList);
        static int CardWidthOf(FlowLayoutPanel list) => list.ClientSize.Width - list.Padding.Horizontal - 2;

        void FitCards() => FitCards(linkList);
        static void FitCards(FlowLayoutPanel list)
        {
            foreach (Control c in list.Controls) if (c is Card card) card.FitWidth(CardWidthOf(list));
        }

        FlowLayoutPanel CardRow(int top = 0) => new FlowLayoutPanel
        {
            AutoSize = true, WrapContents = true, BackColor = Ui.Card, Margin = new Padding(0, top, 0, 0),
        };

        void RebuildLinks()
        {
            linkDirty = false;
            Native.SendMessage(linkList.Handle, Native.WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            linkList.SuspendLayout();
            foreach (Control c in linkList.Controls) c.Dispose();
            linkList.Controls.Clear();

            if (links.Count == 0)
                linkList.Controls.Add(new Label
                {
                    Text = "링크가 없습니다. 'PoB 불러오기'로 가져오거나 '+ 단계 추가'로 직접 만드세요.",
                    ForeColor = Theme.Dim, AutoSize = true, Margin = new Padding(0, 8, 0, 0),
                });

            for (int si = 0; si < links.Count; si++)
            {
                var st = links[si];
                int stageIndex = si;
                bool on = si == linkStage;
                var card = new Card { Highlight = on };

                // 머리: 이름 · 오버레이 표시 · (오른쪽) 위/아래 · 묶음 추가 · 단계 삭제
                var head = CardRow();
                head.WrapContents = false;
                var name = new FieldBox(220, Theme.Bold);
                name.Box.Text = st.name;
                name.Box.TextChanged += (s, e) => st.name = name.Box.Text;
                head.Controls.Add(name);
                var pick = new FlatButton(on ? "★ 오버레이에 표시 중" : "오버레이에 표시", on ? FlatButton.Kind.Primary : FlatButton.Kind.Ghost);
                pick.Click += (s, e) => { linkStage = stageIndex; RebuildLinks(); };
                tip.SetToolTip(pick, $"게임 중 {Hotkeys.Text(settings, Hotkeys.Links)} 로 이 단계의 링크를 보여 줍니다");
                head.Controls.Add(pick);
                head.Controls.Add(new Label { AutoSize = false, Width = 24, Height = 1, Margin = new Padding(0) });
                var up = new FlatButton("▲", FlatButton.Kind.Normal, 30) { Enabled = si > 0 };
                var down = new FlatButton("▼", FlatButton.Kind.Normal, 30) { Enabled = si < links.Count - 1 };
                up.Click += (s, e) => MoveStage(stageIndex, -1);
                down.Click += (s, e) => MoveStage(stageIndex, +1);
                tip.SetToolTip(up, "단계를 위로");
                tip.SetToolTip(down, "단계를 아래로");
                head.Controls.Add(up);
                head.Controls.Add(down);
                var addGroup = new FlatButton("+ 묶음");
                addGroup.Click += (s, e) => { st.groups.Add(new SkillGroup { set = st.name }); RebuildLinks(); };
                head.Controls.Add(addGroup);
                var delStage = new FlatButton("단계 삭제", FlatButton.Kind.Danger);
                delStage.Click += (s, e) =>
                {
                    if (!Confirm($"'{st.name}' 단계를 삭제할까요?\n(묶음 {st.groups.Count}개가 함께 삭제됩니다)")) return;
                    links.RemoveAt(stageIndex);
                    if (linkStage >= links.Count) linkStage = Math.Max(0, links.Count - 1);
                    RebuildLinks();
                };
                head.Controls.Add(delStage);
                card.Body.Controls.Add(head);

                if (st.groups.Count == 0)
                    card.Body.Controls.Add(new Label { Text = "묶음이 없습니다. '+ 묶음'으로 추가하세요.", ForeColor = Theme.Dim, AutoSize = true, Margin = new Padding(2, 10, 0, 0) });

                int slots = st.groups.Count == 0 ? 1 : Math.Max(1, st.groups.Max(x => x.gems.Count));
                for (int gi = 0; gi < st.groups.Count; gi++)
                {
                    var g = st.groups[gi];
                    var row = CardRow(gi == 0 ? 12 : 6);
                    row.Controls.Add(new SocketDots(g.gems.Select(id => data.Gems.TryGetValue(id, out var info) ? info.color : null).ToArray(), slots));
                    for (int k = 0; k < g.gems.Count; k++)
                    {
                        string id = g.gems[k];
                        if (k > 0) row.Controls.Add(new Label { Text = "—", ForeColor = Color.FromArgb(78, 82, 90), AutoSize = true, Margin = new Padding(2, 5, 2, 0) });
                        var pill = new GemPill(data.GemName(id), data.Gems.TryGetValue(id, out var info) ? info.color : null);
                        tip.SetToolTip(pill, (info?.en ?? id) + "   (✕ 를 누르면 묶음에서 빠집니다)");
                        int gemIndex = k;
                        string gemName = data.GemName(id);
                        pill.RemoveClicked += () =>
                        {
                            if (!Confirm($"'{gemName}' 젬을 이 묶음에서 뺄까요?")) return;
                            g.gems.RemoveAt(gemIndex);
                            RebuildLinks();
                        };
                        row.Controls.Add(pill);
                    }
                    var addGem = new FlatButton("+ 젬", FlatButton.Kind.Ghost) { Margin = new Padding(8, 0, 0, 0) };
                    addGem.Click += (s, e) =>
                    {
                        using (var f = new GemPickerForm(data, SelectedClass?.id))
                            if (f.ShowDialog(this) == DialogResult.OK) { g.gems.Add(f.Result); RebuildLinks(); }
                    };
                    row.Controls.Add(addGem);
                    var delGroup = new FlatButton("묶음 삭제", FlatButton.Kind.Ghost) { Margin = new Padding(2, 0, 0, 0) };
                    delGroup.Click += (s, e) =>
                    {
                        string what = g.gems.Count == 0 ? "빈 묶음" : string.Join(" ─ ", g.gems.Select(data.GemName));
                        if (!Confirm($"이 묶음을 삭제할까요?\n\n{what}")) return;
                        st.groups.Remove(g);
                        RebuildLinks();
                    };
                    row.Controls.Add(delGroup);
                    card.Body.Controls.Add(row);
                }
                linkList.Controls.Add(card);
                card.FitWidth(CardWidth);
            }
            linkList.ResumeLayout();
            Native.SendMessage(linkList.Handle, Native.WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
            linkList.Invalidate(true);
        }

        void MoveStage(int i, int d)
        {
            int j = i + d;
            if (j < 0 || j >= links.Count) return;
            (links[i], links[j]) = (links[j], links[i]);
            if (linkStage == i) linkStage = j;
            else if (linkStage == j) linkStage = i;
            RebuildLinks();
        }

        // ── 젬 구매 ───────────────────────────────────────────────

        readonly Panel buyPanel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Back };
        readonly FieldBox searchBox = new FieldBox(280, clearable: true) { BackColor = Theme.Back, Margin = new Padding(10, 0, 0, 0) };   // ✕ 또는 Esc 로 검색 닫기
        TextBox tbSearch => searchBox.Box;
        Card searchCard;   // 검색 결과 (구매 목록 맨 위)
        readonly FlowLayoutPanel buyList = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            AutoScroll = true, BackColor = Theme.Back, Padding = new Padding(14, 4, 14, 14),
        };

        void BuildBuyPanel()
        {
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(14, 10, 14, 0), WrapContents = false, BackColor = Theme.Back };
            bar.Controls.Add(new Label { Text = "젬 검색", Font = Theme.Bold, AutoSize = true, Margin = new Padding(0, 6, 0, 0) });
            bar.Controls.Add(searchBox);
            bar.Controls.Add(new Label { Text = "한국어·영문 일부 (예: 화염, fire) · ✕ 또는 Esc 로 검색 닫기", ForeColor = Theme.Dim, AutoSize = true, Margin = new Padding(10, 6, 0, 0) });
            tbSearch.TextChanged += (s, e) => RefreshSearch();
            buyPanel.Controls.Add(buyList);
            buyPanel.Controls.Add(bar);
            buyList.Resize += (s, e) => { if (!resizing) FitCards(buyList); };
        }

        // 지금 고른 퀘스트 보상에서 받는 젬 ID
        HashSet<string> RewardGems()
        {
            var cls = SelectedClass;
            var set = new HashSet<string>();
            if (cls == null) return set;
            foreach (var kv in picks)
            {
                var o = data.Offer(kv.Key);
                if (o != null && o.rewards.TryGetValue(cls.id, out var l))
                {
                    var r = GameData.FindReward(l, kv.Value);
                    if (r?.id != null) set.Add(r.id);
                }
            }
            if (cls.start != null) set.UnionWith(cls.start);   // 처음 시작할 때 받는 젬도 살 필요 없음
            return set;
        }

        Color GemColor(string id) => Theme.GemText(data.Gems.TryGetValue(id, out var g) ? g.color : null);

        // 구매 목록을 다시 만들어야 하는지 (보상 선택·구매 목록·클래스·프리셋이 바뀌면 true).
        // 만드는 데 0.1초쯤 걸리므로 탭을 열 때마다 만들지 않고, 바뀐 뒤 잠시 조용해지면 뒤에서 미리 만들어 둔다
        // (보상을 연달아 누르는 동안에는 기다린다).
        bool buyDirty = true;
        readonly Timer prebuild = new Timer { Interval = 400 };

        void MarkBuyDirty()
        {
            buyDirty = true;
            if (buyShown) { RebuildBuy(); return; }
            UpdateBuyTab();
            prebuild.Stop();
            prebuild.Start();
        }

        void Prebuild()
        {
            prebuild.Stop();
            if (buyDirty && !buyShown && editor.Visible) RebuildBuy();
        }

        // 구매 목록: 살 수 있는 곳(액트 · NPC · 여는 퀘스트)별로 묶어서. 보상으로 받는 젬은 따로 표시

        void RebuildBuy()
        {
            var cls = SelectedClass;
            if (cls == null) return;
            buyDirty = false;
            Native.SendMessage(buyList.Handle, Native.WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            buyList.SuspendLayout();
            foreach (Control c in buyList.Controls) if (c != searchCard) c.Dispose();
            buyList.Controls.Clear();
            if (searchCard != null) buyList.Controls.Add(searchCard);

            var fromReward = RewardGems();
            if (buy.Count == 0)
                buyList.Controls.Add(new Label
                {
                    Text = "구매할 젬이 없습니다. 'PoB 불러오기'로 빌드를 불러오거나, 위에서 젬을 검색해 추가하세요.",
                    ForeColor = Theme.Dim, AutoSize = true, Margin = new Padding(0, 8, 0, 0),
                });

            // 살 수 있는 곳(액트 · NPC)마다 카드 하나, 그 안에 판매를 여는 퀘스트별 줄
            var items = buy.Where(id => !fromReward.Contains(id)).Select(id => (id, v: data.WhereToBuy(id, cls.id))).ToList();
            foreach (var g in items.Where(x => x.v != null).GroupBy(x => (x.v.act, x.v.npc)).OrderBy(g => g.Key.act))
            {
                var card = BuyCard($"{g.Key.act}장 · {g.Key.npc}", null);
                foreach (var q in g.GroupBy(x => x.v.questKo))
                    card.Body.Controls.Add(BuyRow($"{q.Key} 완료 후", q.Select(x => x.id), true));
                AddCard(buyList, card);
            }
            var none = items.Where(x => x.v == null).Select(x => x.id).ToList();
            if (none.Count > 0)
            {
                var card = BuyCard("상점에서 판매하지 않음", "드롭·다른 방법으로 구하기");
                card.Body.Controls.Add(BuyRow(null, none, true));
                AddCard(buyList, card);
            }
            var start = new HashSet<string>(cls.start ?? new List<string>());
            var startGot = buy.Where(start.Contains).ToList();
            if (startGot.Count > 0)
            {
                var card = BuyCard("처음 시작할 때 받음", $"{cls.ko}는 시작할 때 이 젬을 갖고 있어 사지 않아도 됩니다");
                card.Body.Controls.Add(BuyRow(null, startGot, false));
                AddCard(buyList, card);
            }
            var got = buy.Where(id => fromReward.Contains(id) && !start.Contains(id)).ToList();
            if (got.Count > 0)
            {
                var card = BuyCard("퀘스트 보상으로 받음", "퀘스트 보상 탭에서 고른 젬이라 사지 않아도 됩니다");
                card.Body.Controls.Add(BuyRow(null, got, false));
                AddCard(buyList, card);
            }
            buyList.ResumeLayout();
            Native.SendMessage(buyList.Handle, Native.WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
            buyList.Invalidate(true);
            UpdateBuyTab();
        }

        void AddCard(FlowLayoutPanel list, Card card)
        {
            list.Controls.Add(card);
            card.FitWidth(CardWidthOf(list));
        }

        Card BuyCard(string title, string sub)
        {
            var card = new Card();
            var head = CardRow();
            head.WrapContents = false;
            head.Controls.Add(new Label { Text = title, Font = Theme.Header, ForeColor = Theme.Accent, AutoSize = true, Margin = new Padding(0, 0, 0, 4) });
            if (sub != null) head.Controls.Add(new Label { Text = sub, ForeColor = Theme.Dim, AutoSize = true, Margin = new Padding(10, 5, 0, 0) });
            card.Body.Controls.Add(head);
            return card;
        }

        // 젬 한 줄. when = "○○ 완료 후" (판매를 여는 퀘스트). editable = 체크(오버레이에 표시)·✕(목록에서 빼기)
        Control BuyRow(string when, IEnumerable<string> ids, bool editable)
        {
            var row = CardRow(6);
            var pills = row;
            if (when != null)
            {
                // 왼쪽에 퀘스트 이름, 오른쪽 영역 안에서 젬이 줄바꿈되도록
                row.WrapContents = false;
                row.Controls.Add(new Label { Text = when, ForeColor = Theme.Dim, AutoSize = false, Size = new Size(170, 28), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Margin = new Padding(0) });
                pills = new FlowLayoutPanel { AutoSize = true, WrapContents = true, BackColor = Ui.Card, Margin = new Padding(0), Tag = 170 };
                row.Controls.Add(pills);
            }
            foreach (var id in ids)
            {
                string gid = id, gname = data.GemName(id);
                var info = data.Gems.TryGetValue(id, out var gi) ? gi : null;
                var pill = new GemPill(gname, info?.color, removable: editable, checkable: editable, isChecked: !buyOff.Contains(id)) { Margin = new Padding(0, 1, 8, 1) };
                tip.SetToolTip(pill, (info?.en ?? id) + (editable ? "   (누르면 켜기/끄기 · 끈 젬은 오버레이에 안 나옵니다 · ✕ 목록에서 빼기)" : ""));
                pill.CheckedChanged += () => { if (pill.Checked) buyOff.Remove(gid); else buyOff.Add(gid); UpdateBuyTab(); };
                pill.RemoveClicked += () =>
                {
                    if (!Confirm($"'{gname}' 젬을 구매 목록에서 뺄까요?")) return;
                    buy.Remove(gid);
                    buyOff.Remove(gid);
                    RebuildBuy();
                    RefreshSearch();
                };
                pills.Controls.Add(pill);
            }
            return row;
        }

        // 탭 이름에 살 젬 수 (체크한 것 중 상점에서 파는 것)
        void UpdateBuyTab()
        {
            if (tabBuy == null) return;
            var cls = SelectedClass;
            var fromReward = RewardGems();
            int n = cls == null ? 0 : buy.Count(id => !fromReward.Contains(id) && !buyOff.Contains(id) && data.WhereToBuy(id, cls.id) != null);
            tabBuy.Text = n > 0 ? $"젬 구매 ({n})" : "젬 구매";
        }

        // 젬 검색: 한국어·영문 이름 일부. 결과 카드를 구매 목록 맨 위에 띄운다 (이 클래스가 어디서 살 수 있는지 + 추가)
        void RefreshSearch()
        {
            var q = tbSearch.Text.Trim();
            var cls = SelectedClass;
            buyList.SuspendLayout();
            if (searchCard != null) { buyList.Controls.Remove(searchCard); searchCard.Dispose(); searchCard = null; }
            if (q.Length > 0 && cls != null)
            {
                searchCard = new Card { Highlight = true };
                var hits = data.Gems
                    .Where(kv => !kv.Key.Contains("Royale") &&
                                 (kv.Value.ko.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || kv.Value.en.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0))
                    .OrderBy(kv => kv.Value.ko.StartsWith(q) || kv.Value.en.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(kv => data.WhereToBuy(kv.Key, cls.id) == null ? 1 : 0)
                    .ThenBy(kv => kv.Value.ko)
                    .Take(8).ToList();
                if (hits.Count == 0)
                    searchCard.Body.Controls.Add(new Label { Text = $"'{q}'에 해당하는 젬이 없습니다.", ForeColor = Theme.Dim, AutoSize = true });
                var fromReward = RewardGems();
                for (int i = 0; i < hits.Count; i++)
                {
                    var kv = hits[i];
                    var v = data.WhereToBuy(kv.Key, cls.id);
                    var r = CardRow(i == 0 ? 0 : 4);
                    r.WrapContents = false;
                    r.Controls.Add(new Label { Text = kv.Value.ko, Font = Theme.Bold, ForeColor = Theme.GemText(kv.Value.color), AutoSize = false, Size = new Size(200, 28), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Margin = new Padding(0) });
                    string where = cls.start != null && cls.start.Contains(kv.Key) ? "처음 시작할 때 받음"
                        : fromReward.Contains(kv.Key) ? "퀘스트 보상으로 받음"
                        : v != null ? $"{v.act}장 · {v.npc} ({v.questKo} 완료 후)" : "상점에서 판매하지 않음";
                    r.Controls.Add(new Label { Text = where, ForeColor = Theme.Dim, AutoSize = false, Size = new Size(320, 28), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Margin = new Padding(0) });
                    bool added = buy.Contains(kv.Key);
                    string gid = kv.Key;
                    var add = new FlatButton(added ? "추가됨" : "추가", added ? FlatButton.Kind.Normal : FlatButton.Kind.Primary, 70) { Enabled = !added };
                    add.Click += (s, e) => { if (!buy.Contains(gid)) buy.Add(gid); buyOff.Remove(gid); RebuildBuy(); RefreshSearch(); };
                    r.Controls.Add(add);
                    searchCard.Body.Controls.Add(r);
                }
                buyList.Controls.Add(searchCard);
                buyList.Controls.SetChildIndex(searchCard, 0);
                searchCard.FitWidth(CardWidthOf(buyList));
            }
            buyList.ResumeLayout();
        }

        // ── 보상 목록 ─────────────────────────────────────────────

        void RebuildList()
        {
            var cls = SelectedClass;
            if (cls == null) return;

            Native.SendMessage(list.Handle, Native.WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            list.SuspendLayout();
            foreach (Control c in list.Controls) c.Dispose();
            list.Controls.Clear();
            list.Padding = new Padding(14, 8, 14, 14);
            buttons.Clear();

            // 액트마다 카드 하나: 퀘스트 제목 줄 + 보상 칩 줄
            Card card = null;
            int lastAct = 0;
            foreach (var offer in data.Offers)
            {
                if (!offer.rewards.TryGetValue(cls.id, out var rewards) || rewards.Count == 0) continue;
                if (!cbItems.Checked && IsItemOffer(offer, cls.id)) continue;

                if (offer.act != lastAct || card == null)
                {
                    if (card != null) AddCard(list, card);
                    lastAct = offer.act;
                    card = new Card();
                    card.Body.Controls.Add(new Label { Text = $"액트 {offer.act}", Font = Theme.Header, ForeColor = Theme.Accent, AutoSize = true, Margin = new Padding(0, 0, 0, 2) });
                    if (offer.act == 2) AddBanditRow(card);
                }

                var title = CardRow(10);
                title.WrapContents = false;
                title.Controls.Add(new Label { Text = offer.ko, Font = Theme.Bold, AutoSize = true, Margin = new Padding(0, 0, 0, 6) });
                title.Controls.Add(new Label { Text = $"{offer.npc}  [{offer.option}]", ForeColor = Theme.Dim, AutoSize = true, Margin = new Padding(8, 0, 0, 6) });
                card.Body.Controls.Add(title);

                var row = CardRow();
                var group = new List<ChoiceChip>();
                string offerId = offer.id;
                foreach (var r in rewards)
                {
                    var chip = new ChoiceChip(r.ko, r.gem) { Tag = r };
                    tip.SetToolTip(chip, $"{r.en}\n{r.@class} · {(r.gem != null ? "젬" : $"{r.w}x{r.h}")} · Lv {r.level}");
                    chip.Click += (s, e) => OnPick(offerId, (ChoiceChip)s);
                    group.Add(chip);
                    row.Controls.Add(chip);
                }
                buttons[offer.id] = group;
                card.Body.Controls.Add(row);

                // 이전에 고른 보상이 이 클래스 목록에도 있으면 다시 체크
                if (picks.TryGetValue(offer.id, out var en))
                {
                    var found = GameData.FindReward(rewards, en);
                    var hit = group.FirstOrDefault(b => b.Tag == found);
                    if (hit != null) { hit.Selected = true; picks[offer.id] = found.en; }
                    else picks.Remove(offer.id);
                }
            }
            if (card != null) AddCard(list, card);
            list.ResumeLayout();
            Native.SendMessage(list.Handle, Native.WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
            list.Invalidate(true);
        }

        // 2장 맨 위: 도적 선택 (항상 하나). 오버레이의 패시브 포인트 목표에 쓰인다
        void AddBanditRow(Card card)
        {
            var title = CardRow(10);
            title.WrapContents = false;
            title.Controls.Add(new Label { Text = "도적 처리", Font = Theme.Bold, AutoSize = true, Margin = new Padding(0, 0, 0, 6) });
            title.Controls.Add(new Label { Text = "모두 처치하면 패시브 포인트 +1", ForeColor = Theme.Dim, AutoSize = true, Margin = new Padding(8, 0, 0, 6) });
            card.Body.Controls.Add(title);

            var row = CardRow();
            var chips = new List<ChoiceChip>();
            foreach (var kv in Profile.Bandits)
            {
                var chip = new ChoiceChip(kv.Value, null) { Tag = kv.Key, Selected = kv.Key == bandit };
                chip.Click += (s, e) =>
                {
                    bandit = (string)((ChoiceChip)s).Tag;
                    foreach (var c in chips) c.Selected = c == s;
                };
                chips.Add(chip);
                row.Controls.Add(chip);
            }
            card.Body.Controls.Add(row);
        }

        // 같은 퀘스트 안에서는 하나만 고를 수 있다. 이미 고른 것을 다시 누르면 선택 해제.
        void OnPick(string offerId, ChoiceChip clicked)
        {
            bool on = !clicked.Selected;
            foreach (var b in buttons[offerId]) b.Selected = false;
            if (on)
            {
                clicked.Selected = true;
                picks[offerId] = ((Reward)clicked.Tag).en;
            }
            else picks.Remove(offerId);
            MarkBuyDirty();   // 보상으로 받게 된 젬은 구매 목록에서 빠진다
        }

        // ── 프리셋 ───────────────────────────────────────────────

        Profile BuildProfile()
        {
            var cls = SelectedClass;
            return new Profile
            {
                id = presetId,
                name = string.IsNullOrWhiteSpace(tbName.Text) ? $"{cls.ko} 프리셋" : tbName.Text.Trim(),
                classId = cls.id,
                picks = new Dictionary<string, string>(picks),
                buy = new List<string>(buy),
                buyOff = buyOff.Where(buy.Contains).ToList(),
                links = links,
                linkStage = linkStage,
                trees = trees,
                treeStage = treeStage,
                bandit = bandit,
                regexCustom = regexCustom,
                regexPresets = RegexSearch.Keys(data.RegexPresets).Where(regexOn.Contains).ToList(),
            };
        }

        bool applying;

        static bool IsItemOffer(Offer o, string classId) =>
            o.rewards.TryGetValue(classId, out var l) && l.Count > 0 && l.All(r => r.gem == null);

        void OnItemsToggled()
        {
            if (suppressItems) return;
            var cls = SelectedClass;
            if (!cbItems.Checked && cls != null)
            {
                // 숨기면서 고른 장비 보상이 남아 있으면 게임에서는 계속 강조되므로 같이 해제한다
                var itemPicks = picks.Keys.Where(id => data.Offer(id) is Offer o && IsItemOffer(o, cls.id)).ToList();
                if (itemPicks.Count > 0)
                {
                    if (MessageBox.Show(this, $"고른 장비·플라스크 보상 {itemPicks.Count}개를 해제할까요?", "Siosa", MessageBoxButtons.YesNo) != DialogResult.Yes)
                    {
                        suppressItems = true; cbItems.Checked = true; suppressItems = false;
                        return;
                    }
                    foreach (var id in itemPicks) picks.Remove(id);
                }
            }
            settings.showItems = cbItems.Checked;
            RebuildList();
        }

        void Apply(Profile p)
        {
            p = Profile.FromJson(p.ToJson());
            picks.Clear();
            foreach (var kv in p.picks) picks[kv.Key] = kv.Value;
            presetId = string.IsNullOrEmpty(p.id) ? presetId : p.id;
            bandit = Profile.Bandits.ContainsKey(p.bandit ?? "") ? p.bandit : "None";
            buy.Clear();
            buy.AddRange(p.buy ?? new List<string>());
            buyOff.Clear();
            foreach (var id in p.buyOff ?? new List<string>()) buyOff.Add(id);
            // 예전 프리셋은 평면 묶음(groups)만 있으므로 단계별로 옮긴다
            links = p.links != null && p.links.Count > 0 ? p.links : LinkStage.FromGroups(p.groups ?? new List<SkillGroup>());
            trees = p.trees ?? new List<TreeStage>();
            treeStage = Math.Max(0, Math.Min(p.treeStage, trees.Count - 1));
            regexCustom = p.regexCustom ?? "";
            regexOn = new HashSet<string>(p.regexPresets ?? new List<string>());
            SyncRegexUi();
            treeDirty = true;
            linkStage = Math.Max(0, Math.Min(p.linkStage, links.Count - 1));
            linkDirty = true;
            buyDirty = true;
            tbName.Text = p.name;
            var cls = data.Classes.FirstOrDefault(c => c.id == p.classId) ?? data.Classes[0];
            // 장비·플라스크 보상을 고른 프리셋이면 장비 보상도 보이게
            if (!cbItems.Checked && p.picks.Keys.Any(id => data.Offer(id) is Offer o && IsItemOffer(o, cls.id)))
            {
                suppressItems = true; cbItems.Checked = true; suppressItems = false;
                settings.showItems = true;
            }
            applying = true;
            try
            {
                if (cbClass.SelectedItem == cls) RebuildList();
                else cbClass.SelectedItem = cls;   // SelectedIndexChanged → RebuildList
            }
            finally { applying = false; }
            MarkSaved();
        }

        // 지금 클래스에서 고른 보상을 모두 해제 (창 닫기는 X 버튼)
        void ResetPicks()
        {
            if (picks.Count > 0 &&
                MessageBox.Show(this, "선택한 보상을 모두 해제할까요?", "Siosa", MessageBoxButtons.YesNo) != DialogResult.Yes)
                return;
            picks.Clear();
            bandit = "None";
            RebuildList();
        }

        // 저장. 프리셋 ID 는 진행 기록 파일 이름이라 파일마다 달라야 한다.
        //   - 연 프리셋의 이름을 바꿔 저장: 이름 바꾸기(ID·진행 그대로, 예전 파일은 휴지통) 또는 복사본(새 ID)을 고른다
        //     (예전: 이름만 바꿔 저장하면 같은 ID 의 파일이 둘이 되어 두 프리셋이 진행 기록을 같이 썼다)
        //   - 같은 이름의 다른 프리셋이 있으면 덮어쓸지 묻고, 덮어쓰는 파일은 휴지통으로
        //     (예전: 이름을 비우면 "위치 프리셋" 같은 기본 이름이라 새 프리셋이 예전 것을 묻지 않고 덮어썼다)
        // 저장했으면 true (취소하면 false). quiet: "저장했습니다" 알림 없이
        bool SaveProfile(bool quiet = false)
        {
            var p = BuildProfile();
            Directory.CreateDirectory(profilesDir);
            var file = string.Concat(p.name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
            var path = Path.Combine(profilesDir, file + ".json");
            string renamedFrom = null;
            if (!SamePath(presetPath, path))
            {
                if (presetPath != null && File.Exists(presetPath))
                {
                    var r = Ask(
                        $"프리셋 이름이 '{Path.GetFileNameWithoutExtension(presetPath)}' 에서 '{p.name}' 으로 바뀌었습니다.\n\n" +
                        "예: 이름 바꾸기 (캐릭터 진행 기록 그대로, 예전 이름의 파일은 휴지통으로)\n" +
                        "아니요: 복사본으로 저장 (새 프리셋, 진행 기록은 따로)",
                        MessageBoxButtons.YesNoCancel);
                    if (r == DialogResult.Cancel) return false;
                    if (r == DialogResult.Yes) renamedFrom = presetPath;
                    else { presetId = NewId(); p.id = presetId; }
                }
                if (File.Exists(path))
                {
                    if (!Confirm($"'{p.name}' 이름의 프리셋이 이미 있습니다. 덮어쓸까요?\n(덮어쓰는 프리셋 파일은 휴지통으로 이동합니다)")) return false;
                    if (!Recycle(path)) return false;
                }
            }
            p.Save(path);
            if (renamedFrom != null) Recycle(renamedFrom);
            presetPath = path;
            settings.lastProfile = path;
            MarkSaved();
            if (!quiet && !DevQuiet) MessageBox.Show(this, $"저장했습니다.\n{path}", "Siosa");
            return true;
        }

        // 저장하지 않은 변경 확인용: 마지막으로 열었거나(새로 만들기 포함) 저장한 상태
        string savedJson;
        void MarkSaved() => savedJson = SelectedClass != null ? BuildProfile().ToJson() : null;
        bool Unsaved => savedJson != null && SelectedClass != null && BuildProfile().ToJson() != savedJson;

        // ← 처음으로: 저장하지 않은 변경이 있으면 저장할지 묻는다 (취소하면 편집 화면에 남음)
        void BackToHome()
        {
            if (Unsaved)
            {
                var r = MessageBox.Show(this, "저장하지 않은 변경이 있습니다. 저장하고 나갈까요?", "Siosa",
                    MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button3);
                if (r == DialogResult.Cancel) return;
                if (r == DialogResult.Yes && !SaveProfile()) return;
            }
            ShowHome();
        }

        bool LoadProfile()
        {
            using (var dlg = new OpenFileDialog { InitialDirectory = profilesDir, Filter = "Siosa 프리셋 (*.json)|*.json" })
                return dlg.ShowDialog(this) == DialogResult.OK && Open(dlg.FileName);
        }

        // PoB 빌드로 젬 보상 자동 선택. 클래스를 빌드 클래스로 바꾸고 젬 보상은 모두 PoB 기준으로 덮어쓴다.
        // (편집 중이고 같은 클래스면 장비·플라스크 선택은 유지. 메인 화면에서는 빈 프리셋에서 시작)
        bool ImportPob(bool fresh)
        {
            Pob.Selection sel;
            using (var f = new PobImportForm(data))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return false;
                sel = f.Result;
            }
            if (fresh) ClearEditor();
            var cls = sel.Class;
            if (SelectedClass != cls) picks.Clear();
            foreach (var o in data.Offers)
                if (o.rewards.TryGetValue(cls.id, out var l) && l.Count > 0 && l.Exists(r => r.gem != null)) picks.Remove(o.id);
            foreach (var kv in sel.Picks) picks[kv.Key] = kv.Value;
            bandit = sel.Bandit;
            // 구매 목록·스킬 묶음도 PoB 기준으로 (직접 추가했던 젬은 뒤에 남긴다)
            var manual = buy.Where(id => !sel.Buy.Contains(id)).ToList();
            buy.Clear();
            buy.AddRange(sel.Buy);
            buy.AddRange(manual);
            buyOff.Clear();
            links = sel.Links;
            linkStage = 0;
            trees = sel.Trees ?? new List<TreeStage>();
            treeStage = 0;
            treeDirty = true;
            if (treesShown) RebuildTrees();
            linkDirty = true;
            if (linksShown) RebuildLinks();
            if (string.IsNullOrWhiteSpace(tbName.Text)) tbName.Text = $"{cls.ko} PoB";

            applying = true;
            try
            {
                if (cbClass.SelectedItem == cls) RebuildList();
                else cbClass.SelectedItem = cls;
            }
            finally { applying = false; }
            MarkBuyDirty();

            int toBuy = sel.Buy.Count(id => data.WhereToBuy(id, cls.id) != null);
            var msg = $"{cls.ko} · 젬 보상 퀘스트 {sel.GemOffers}개 중 {sel.Picks.Count}개를 골랐습니다.\n도적:{Profile.Bandits[bandit]}";
            if (sel.Buy.Count > 0)
                msg += $"\n상점에서 살 젬 {toBuy}개" + (sel.Buy.Count > toBuy ? $", 상점에서 판매하지 않는 젬 {sel.Buy.Count - toBuy}개" : "") + "는 '젬 구매' 탭에서 확인하세요.";
            MessageBox.Show(this, msg, "PoB 불러오기");
            return true;
        }

        void BrowseLog()
        {
            using (var dlg = new OpenFileDialog { Filter = "PoE 로그 (KakaoClient.txt;Client.txt)|KakaoClient.txt;Client.txt|모든 파일|*.*" })
            {
                if (File.Exists(tbLog.Text)) dlg.InitialDirectory = Path.GetDirectoryName(tbLog.Text);
                if (dlg.ShowDialog(this) == DialogResult.OK) tbLog.Text = dlg.FileName;
            }
        }

        // 칸이 비어 있으면 자동 감지 결과를 회색 안내문(cue banner)으로 보여준다.
        void UpdateLogHint()
        {
            if (tbLog.Text.Length > 0)
            {
                lbLogHint.Text = File.Exists(tbLog.Text) ? "직접 지정" : "파일 없음";
                return;
            }
            var found = LogDetect.Find();
            lbLogHint.Text = found != null ? "자동 감지됨" : "자동 감지 실패";
            var cue = found ?? "게임을 켜면 자동으로 찾습니다. 또는 '찾기'로 지정";
            if (tbLog.IsHandleCreated) Native.SendMessage(tbLog.Handle, Native.EM_SETCUEBANNER, (IntPtr)1, cue);
            else tbLog.HandleCreated += (s, e) => Native.SendMessage(tbLog.Handle, Native.EM_SETCUEBANNER, (IntPtr)1, cue);
        }

        void StartOverlay()
        {
            if (tbLog.Text.Length > 0 && !File.Exists(tbLog.Text))
            {
                MessageBox.Show(this, "지정한 게임 로그 파일이 없습니다. '자동'을 누르거나 '찾기'로 다시 지정해 주세요.", "Siosa");
                return;
            }
            if (picks.Count == 0 &&
                MessageBox.Show(this, "선택한 보상이 없습니다. 그래도 시작할까요?", "Siosa", MessageBoxButtons.YesNo) != DialogResult.Yes)
                return;

            if (Unsaved || presetPath == null)
            {
                var r = Ask(
                    (presetPath == null ? "아직 저장하지 않은 프리셋입니다. 저장하지 않으면 다음에 이 프리셋을 다시 열 수 없습니다."
                                        : "저장하지 않은 변경이 있습니다. 저장하지 않으면 다음에 열 때 변경 전 내용이 됩니다.") +
                    "\n\n저장하고 시작할까요?",
                    MessageBoxButtons.YesNoCancel);
                if (r == DialogResult.Cancel) return;
                if (r == DialogResult.Yes && !SaveProfile(quiet: true)) return;
            }

            settings.logPath = tbLog.Text;
            Result = BuildProfile();
            ResultPath = presetPath;
            StartRequested = true;
            Close();
        }
    }
}
