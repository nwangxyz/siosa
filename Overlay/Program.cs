using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Siosa
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // 윈도우 배율(125%, 150% …)에서도 화면 좌표가 실제 픽셀과 일치하도록
            Native.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 개발: 저장소(data\rewards.json 이 있는 위쪽 폴더). 배포: exe 하나 → 사용자 파일은 exe 옆 SiosaData\
            var dev = FindRoot();
            if (dev != null) { AppPaths.Root = dev; AppPaths.Data = Path.Combine(dev, "data"); }
            else
            {
                AppPaths.Portable = true;
                AppPaths.Root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SiosaData");
                AppPaths.Data = null;   // 게임 데이터는 exe 안의 것만
                try { Directory.CreateDirectory(AppPaths.Root); }
                catch (Exception ex)
                {
                    MessageBox.Show("SiosaData 폴더를 만들 수 없습니다. 쓰기 가능한 폴더(바탕 화면, 문서 등)로 Siosa.exe 를 옮겨 주세요.\n\n" + ex.Message, "Siosa");
                    return;
                }
            }
            var root = AppPaths.Root;
            if ((args.Length == 4 || args.Length == 5) && args[0] == "--detecttest")
            {
                DetectTest(root, args[1], args[2], int.Parse(args[3]), args.Length == 5 ? args[4] : null);
                return;
            }
            if (args.Length == 4 && args[0] == "--notetest")
            {
                // 개발용: 스크린샷의 보상 창을 판별해서 "고르지 않은 보상" 안내 문구를 그려 저장.  --notetest <png> <클래스ID> <액트>
                Native.SetProcessDPIAware();
                var data = GameData.Load(AppPaths.Data);
                var cands = data.Offers.Where(o => o.act == int.Parse(args[3]) && o.rewards.TryGetValue(args[2], out var l) && l.Count > 0)
                    .Select((o, i) => new RewardDetector.Candidate { Offer = o, Items = o.rewards[args[2]], Priority = i }).ToList();
                using (var bmp = new Bitmap(args[1]))
                {
                    var client = new Rectangle(0, 0, bmp.Width, bmp.Height);
                    var det = new RewardDetector(new Settings());
                    var r = det.DetectIn(bmp, Point.Empty, client, cands, RewardDetector.WindowOpenIn(bmp, Point.Empty, client, det.Shifts(cands)));
                    if (r.Match == null) return;
                    using (var hf = new HighlightForm())
                    {
                        hf.ShowNote(r.Rects, $"고르지 않은 보상 · [{r.Match.Offer.option}]");
                        using (var outBmp = hf.DevRender(bmp)) outBmp.Save(args[1] + ".note.png");
                    }
                }
                return;
            }
            if (args.Length == 2 && args[0] == "--hotkeytest")
            {
                // 개발용: 단축키 변경 창에 키 입력을 흉내 내고 결과를 파일로.  --hotkeytest <결과.txt>
                var st = new Settings();
                var log = new System.Text.StringBuilder();
                using (var f = new HotkeyForm(st))
                {
                    f.CreateControl();
                    var h = f.Handle;
                    f.DevCapture(Hotkeys.Links); f.DevPress(Keys.Control | Keys.Shift | Keys.Q);
                    log.AppendLine("Ctrl+Shift+Q → 링크: " + f.DevState());
                    f.DevCapture(Hotkeys.Hide); f.DevPress(Keys.Q);
                    log.AppendLine("Q 만 → 숨기기: " + f.DevState());
                    f.DevPress(Keys.Escape);
                    log.AppendLine("Esc: " + f.DevState());
                    f.DevCapture(Hotkeys.Hide); f.DevPress(Keys.Control | Keys.Shift | Keys.Q);
                    log.AppendLine("Ctrl+Shift+Q → 숨기기 (중복): " + f.DevState());
                    f.DevCapture(Hotkeys.Hide); f.DevPress(Keys.Alt | Keys.F2);
                    log.AppendLine("Alt+F2 → 숨기기: " + f.DevState());
                    f.DevSave();
                }
                log.AppendLine("저장된 값: " + string.Join(", ", st.hotkeys.Select(kv => $"{kv.Key}={Hotkeys.Text((Keys)kv.Value)}")));
                log.AppendLine("오버레이 안내: " + Hotkeys.Summary(st, new[] { (Hotkeys.Move, "이동"), (Hotkeys.Hide, "숨기기"), (Hotkeys.Links, "링크"), (Hotkeys.Tree, "트리"), (Hotkeys.Tips, "팁"), (Hotkeys.BuyRegex, "구매 젬"), (Hotkeys.Regex, "정규식") }));
                { var gd = GameData.Load(AppPaths.Data); log.AppendLine($"tips {gd.Tips.Count} 1_1_1={gd.Tips["1_1_1"]}"); }
                {
                    // 정규식 단축키 결과: PoB 샘플(위치)로 액트마다
                    var gd2 = GameData.Load(AppPaths.Data);
                    var sel2 = Pob.Select(gd2, Pob.Parse(File.ReadAllText(Path.Combine(root, "tools", "testdata", "pob", "witch_act_sets.txt"))));
                    var pr = new Profile { classId = sel2.Class.id, picks = sel2.Picks, buy = sel2.Buy, regexCustom = "이동.속도", regexPresets = new System.Collections.Generic.List<string> { "link4", "move", "add:물리", "add:화염", "gemlvl:번개" } };
                    log.AppendLine("정규식 (F): " + RegexSearch.Build(gd2, pr));
                    for (int a = 1; a <= 4; a++) log.AppendLine($"구매 젬 {a}장 (G): " + RegexSearch.BuildBuy(gd2, pr, a, out _));
                }
                log.AppendLine("링크 줄: " + Hotkeys.Short(st, Hotkeys.PrevStage) + " 이전 · " + Hotkeys.Short(st, Hotkeys.NextStage) + " 다음");
                File.WriteAllText(args[1], log.ToString(), System.Text.Encoding.UTF8);
                return;
            }
            if (args.Length == 2 && args[0] == "--charpick")
            {
                // 개발용: 실제 로그로 "이전 진행 불러오기" 창을 띄워 화면 캡처.  --charpick <png>
                Native.SetProcessDPIAware();
                var f = new CharPickForm(LogWatcher.ScanCharacters(LogDetect.Find())) { StartPosition = FormStartPosition.Manual, Location = new Point(0, 0), TopMost = true };
                f.Shown += (s2, e2) =>
                {
                    f.Refresh(); Application.DoEvents();
                    using (var bmp = new Bitmap(f.Width, f.Height))
                    {
                        f.DrawToBitmap(bmp, new Rectangle(Point.Empty, f.Size));
                        bmp.Save(args[1]);
                    }
                    f.Close();
                };
                Application.Run(f);
                return;
            }
            if (args.Length == 2 && args[0] == "--chartest")
            {
                // 개발용: 캐릭터 여러 개를 번갈아 한 로그로 프리셋 캐릭터 구분을 확인.  --chartest <결과.txt>
                CharTest(root, args[1]);
                return;
            }
            if (args.Length == 2 && args[0] == "--tabshot")
            {
                // 개발용: PoB 샘플 프리셋 설정창을 화면 왼쪽 위에 잠깐 띄워 두 탭을 실제 화면 캡처로 저장.  --tabshot <폴더>
                Native.SetProcessDPIAware();
                var data = GameData.Load(AppPaths.Data);
                var build = Pob.Parse(File.ReadAllText(Path.Combine(root, "tools", "testdata", "pob", "witch_act_sets.txt")));
                var sel = Pob.Select(data, build);
                var p = new Profile { name = "위치 PoB 예시", classId = sel.Class.id, picks = sel.Picks, buy = sel.Buy, links = sel.Links, bandit = sel.Bandit, trees = sel.Trees };
                var f = new SetupForm(data, Path.Combine(root, "profiles"), new Settings(), p) { StartPosition = FormStartPosition.Manual, Location = new Point(0, 0), TopMost = true };
                Directory.CreateDirectory(args[1]);
                f.ProgressInfo = _ => Environment.GetEnvironmentVariable("SIOSA_CHAR");   // 아래쪽 캐릭터 줄 (없으면 "아직 없음")
                f.ScanChars = () => null;
                f.RecoverProgress = (_, __) => { };
                f.AppRoot = Environment.GetEnvironmentVariable("SIOSA_ROOT");   // 개발용: 업데이트 줄 모양 보기 (배포 폴더 경로)
                f.Shown += (s, e) => { f.DevShotTabs(args[1]); f.Close(); };
                Application.Run(f);
                return;
            }
            if (args.Length == 2 && args[0] == "--combotest")
            {
                // 개발용: 클래스 드롭다운 열고 닫기·고르기 (예전에 프로그램이 꺼지던 경로).  --combotest <결과.txt>
                Native.SetProcessDPIAware();
                var data = GameData.Load(AppPaths.Data);
                var build = Pob.Parse(File.ReadAllText(Path.Combine(root, "tools", "testdata", "pob", "witch_act_sets.txt")));
                var sel = Pob.Select(data, build);
                var p = new Profile { name = "t", classId = sel.Class.id, picks = sel.Picks, buy = sel.Buy, links = sel.Links, bandit = sel.Bandit, trees = sel.Trees };
                var f = new SetupForm(data, Path.Combine(root, "profiles"), new Settings(), p) { StartPosition = FormStartPosition.Manual, Location = new Point(0, 0), TopMost = true };
                string result = "";
                f.Shown += (s, e) => { result = f.DevComboTest(); f.Close(); };
                Application.Run(f);
                File.WriteAllText(args[1], result, System.Text.Encoding.UTF8);
                return;
            }
            if (args.Length == 2 && (args[0] == "--tabtime" || args[0] == "--flickertest" || args[0] == "--scrolltest"))
            {
                // 개발용: PoB 샘플 프리셋으로 설정창 탭 전환 시간을 잰다.  --tabtime <결과.txt>
                var data = GameData.Load(AppPaths.Data);
                var build = Pob.Parse(File.ReadAllText(Path.Combine(root, "tools", "testdata", "pob", "witch_act_sets.txt")));
                var sel = Pob.Select(data, build);
                var p = new Profile { name = "t", classId = sel.Class.id, picks = sel.Picks, buy = sel.Buy, links = sel.Links, bandit = sel.Bandit, trees = sel.Trees };
                var f = new SetupForm(data, Path.Combine(root, "profiles"), new Settings(), p) { StartPosition = FormStartPosition.Manual, Location = new Point(-4000, 0) };
                string result = "";
                f.Shown += (s, e) => { result = args[0] == "--flickertest" ? f.DevFlickerTest() : args[0] == "--scrolltest" ? f.DevScrollTest() : f.DevTimeTabs(); f.Close(); };
                Application.Run(f);
                File.WriteAllText(args[1], result, System.Text.Encoding.UTF8);
                return;
            }
            if (args.Length >= 2 && args[0] == "--affinitytest")
            {
                // 개발용: 오버레이 창의 화면 캡처 제외 여부 (--showcapture 확인).  --affinitytest <결과.txt> [--showcapture]
                Native.ShowInCapture = args.Contains("--showcapture");
                var d = GameData.Load(AppPaths.Data);
                var tr = new Tracker(d, Path.Combine(Path.GetTempPath(), "siosa_aff.json"), "aff");
                var ov = new OverlayForm(d, new Settings(), new Profile(), tr, Path.GetTempPath());
                ov.Show();
                Application.DoEvents();
                Native.GetWindowDisplayAffinity(ov.Handle, out uint aff);
                File.WriteAllText(args[1], $"ShowInCapture {Native.ShowInCapture} → affinity 0x{aff:X} ({(aff == 0x11 ? "캡처에서 제외" : "캡처에 찍힘")})", System.Text.Encoding.UTF8);
                ov.Close();
                return;
            }
            if (args.Length == 2 && args[0] == "--datatest")
            {
                // 개발용: 파일 위치와 읽은 데이터 개수 (배포 exe 하나로도 데이터가 다 읽히는지).  --datatest <결과.txt>
                var d = GameData.Load(AppPaths.Data);
                File.WriteAllText(args[1], $"포터블 {AppPaths.Portable}\nRoot {AppPaths.Root}\nData {AppPaths.Data}\n" +
                    $"보상 {d.Offers.Count} 지역 {d.Areas.Count} 젬 {d.Gems.Count} 팁 {d.Tips.Count} 판매 {d.Vendor.Count} 포인트 {d.Points.Count} " +
                    $"트리 {(d.Tree != null ? "있음" : "없음")} 정규식 {d.RegexPresets.Count} 아이콘 {(d.Icons != null && d.Icons.Has(d.Offers[0].rewards.First().Value[0].en) ? "있음" : "없음")} 직업 {d.Classes.Count}", System.Text.Encoding.UTF8);
                return;
            }
            if (args.Length == 2 && args[0] == "--checktest")
            {
                // 개발용: GitHub 최신 릴리스 확인 결과.  --checktest <결과.txt>
                string r;
                try { var i = Updater.CheckAsync().Result; r = $"최신 {i.Tag} ({i.Version}), zip {i.ZipUrl ?? "없음"}, 지금 {AppInfo.Version}, 새 버전 {Updater.IsNewer(i)}"; }
                catch (Exception ex) { r = "확인 실패: " + (ex.InnerException ?? ex).Message; }
                File.WriteAllText(args[1], r, System.Text.Encoding.UTF8);
                return;
            }
            if (args.Length == 3 && args[0] == "--installtest")
            {
                // 개발용: 배포 폴더 복사본에서 받은 zip 으로 바꾸기만 해 본다 (GitHub 없이).  --installtest <zip> <결과.txt>
                var tmp = Path.Combine(Path.GetTempPath(), "siosa_installtest");
                if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
                string result;
                try { Updater.Install(args[1], Application.ExecutablePath, tmp, null); result = "바꿈"; }
                catch (Exception ex) { result = "오류: " + ex; }
                File.WriteAllText(args[2], result, System.Text.Encoding.UTF8);
                return;
            }
            if (args.Length == 3 && args[0] == "--savetest")
            {
                // 개발용: 빈 폴더에서 프리셋 저장 흐름(이름 바꾸기·복사본·덮어쓰기).  --savetest <빈 폴더> <결과.txt>
                var data = GameData.Load(AppPaths.Data);
                Directory.CreateDirectory(args[1]);
                var f = new SetupForm(data, args[1], new Settings(), null) { StartPosition = FormStartPosition.Manual, Location = new Point(-4000, 0) };
                string result = "";
                f.Shown += (s, e) => { try { result = f.DevSaveTest(); } catch (Exception ex) { result = "오류: " + ex; } f.Close(); };
                Application.Run(f);
                File.WriteAllText(args[2], result, System.Text.Encoding.UTF8);
                return;
            }
            if (args.Length == 3 && args[0] == "--pobtest")
            {
                PobTest(root, args[1], args[2]);
                return;
            }
            if (args.Length >= 2 && args[0] == "--snapshot")
            {
                Snapshot(root, args[1], args.Length > 2 && args[2] != "-" ? args[2] : null, args.Length > 3 ? args[3] : null);
                return;
            }
            Hotkeys.DevMode = args.Contains("--dev");   // 개발용: D(화면 저장) 단축키
            Native.ShowInCapture = args.Contains("--showcapture");   // 화면 캡처(PrintScreen 등)에 Siosa 창도 찍히게 (README 이미지용)
            Updater.CleanupAfterUpdate(args);            // 업데이트 뒤 다시 시작: 예전 exe(.old) 지우기
            Application.Run(new SiosaApp(root));
        }

        // 개발용: 설정창과 오버레이를 화면 밖에 띄워 PNG 로 저장한다. (--snapshot <폴더> [로그파일])
        //   --snapshot <폴더> [로그 경로 또는 -] [프리셋 json]  (프리셋을 주면 overlay.png 를 그 프리셋으로)
        static void Snapshot(string root, string outDir, string logPath, string profilePath = null)
        {
            var data = GameData.Load(AppPaths.Data);
            var settings = Settings.Load(Path.Combine(root, "settings.json"));
            var profile = new Profile { name = "테스트", classId = "Int" };
            foreach (var o in data.Offers)
                if (o.rewards.TryGetValue("Int", out var list) && list.Count > 0) profile.picks[o.id] = list[0].en;
            if (profilePath != null) profile = Profile.Load(profilePath);

            Directory.CreateDirectory(outDir);
            void Save(Form f, string name)
            {
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(-4000, 0);
                f.Show();
                Application.DoEvents();
                using (var bmp = new Bitmap(f.Width, f.Height))
                {
                    f.DrawToBitmap(bmp, new Rectangle(Point.Empty, f.Size));
                    bmp.Save(Path.Combine(outDir, name));
                }
                f.Close();
            }

            Save(new SetupForm(data, Path.Combine(root, "profiles"), settings, null), "home.png");
            Save(new SetupForm(data, Path.Combine(root, "profiles"), settings, profile), "setup.png");

            // PoB 샘플(있으면)을 레벨링 세트로 불러온 프리셋 → 아래 오버레이(구매 줄) 확인용.
            // 구매 탭 화면은 --tabshot 으로 (DrawToBitmap 은 겹쳐 둔 두 탭 화면의 순서를 거꾸로 그린다)
            var sample = Path.Combine(root, "tools", "testdata", "pob", "witch_act_sets.txt");
            Profile pobProfile = null;
            if (File.Exists(sample))
            {
                var build = Pob.Parse(File.ReadAllText(sample));
                var sel = Pob.Select(data, build, Enumerable.Range(0, build.Sets.Count).Where(i => build.Sets[i].Leveling).ToList());
                pobProfile = new Profile { name = "위치 PoB", classId = sel.Class.id, picks = sel.Picks, buy = sel.Buy, links = sel.Links };
            }

            var tracker = new Tracker(data, Path.Combine(outDir, "progress.json"));
            var ov = new OverlayForm(data, settings, profile, tracker, Path.Combine(outDir, "debug"));
            ov.CreateControl();
            using (var w = new LogWatcher(logPath ?? LogDetect.Find()))
            {
                w.AreaEntered += tracker.OnAreaEntered;
                w.LevelUp += tracker.OnLevelUp;
                w.PointsGained += tracker.OnPointsGained;
                w.Start(0);
            }
            ov.Opacity = 1;
            Save(ov, "overlay.png");
            if (pobProfile != null)
            {
                var ov2 = new OverlayForm(data, settings, pobProfile, tracker, Path.Combine(outDir, "debug")) { Opacity = 1, ShowLinks = true };
                ov2.CreateControl();
                Save(ov2, "overlay_pob.png");
            }
            // 머라우더 PoB (구매 줄이 여러 줄로 나뉘는 경우, 시작 젬은 구매 목록에서 빠짐)
            var sample2 = Path.Combine(root, "tools", "testdata", "pob", "marauder_level_sections.txt");
            if (File.Exists(sample2))
            {
                var sel2 = Pob.Select(data, Pob.Parse(File.ReadAllText(sample2)));
                var p2 = new Profile { name = "머라우더 PoB", classId = sel2.Class.id, picks = sel2.Picks, buy = sel2.Buy, links = sel2.Links, trees = sel2.Trees, treeStage = 4 };
                settings.showTree = true;   // 트리 창이 보이도록 (스냅샷용, 저장하지 않음)
                var ov3 = new OverlayForm(data, settings, p2, tracker, Path.Combine(outDir, "debug")) { Opacity = 1 };
                ov3.CreateControl();
                ov3.StartPosition = FormStartPosition.Manual;
                ov3.Location = new Point(-4000, 0);
                ov3.Show();
                Application.DoEvents();
                // 트리 창 (오버레이와 따로 뜨는 창): 기본 크기와 두 단계 키운 크기
                foreach (var (sz, name) in new[] { (240, "tree_window.png"), (320, "tree_window_320.png") })
                {
                    settings.treeSize = sz;
                    ov3.DevUpdateTree();
                    var tw = ov3.DevTreeWindow;
                    tw.Location = new Point(-4000, 400);
                    Application.DoEvents();
                    using (var bmp = new Bitmap(tw.Width, tw.Height))
                    {
                        tw.DrawToBitmap(bmp, new Rectangle(Point.Empty, tw.Size));
                        bmp.Save(Path.Combine(outDir, name));
                    }
                }
                ov3.Close();
                // 마스터리가 많은 단계 (레인저 샘플 마지막 트리): 트리 창 그림 + 편집 화면 칸(낮고 넓게)
                var sample3 = Path.Combine(root, "tools", "testdata", "pob", "ranger_act_sets.txt");
                if (File.Exists(sample3))
                {
                    var tr3 = Pob.Parse(File.ReadAllText(sample3)).Trees;
                    var last = tr3[tr3.Count - 1];
                    using (var img = TreeRender.Render(data.Tree, last, tr3[tr3.Count - 2], 240, Theme.Back)) img.Save(Path.Combine(outDir, "tree_many_masteries.png"));
                    // 같은 숙련 여러 개: 생명력 숙련 3개 + 독 숙련 2개 (앞 단계에는 생명력 하나만 있었다고 침)
                    var dup = new TreeStage { name = "같은 숙련", nodes = last.nodes };
                    var prevDup = new TreeStage { name = "앞", nodes = last.nodes };
                    foreach (var name in new[] { "생명력 숙련", "독 숙련" })
                        foreach (var kv in data.Tree.Mastery.Where(m => m.Value.name == name).Take(name == "독 숙련" ? 2 : 3))
                        {
                            dup.masteries[kv.Key] = int.Parse(kv.Value.effects.Keys.First());
                            if (name == "생명력 숙련" && prevDup.masteries.Count == 0) prevDup.masteries[kv.Key] = dup.masteries[kv.Key];
                        }
                    using (var img = TreeRender.Render(data.Tree, dup, prevDup, 240, Theme.Back)) img.Save(Path.Combine(outDir, "tree_same_mastery.png"));
                    using (var box = new TreeBox { Size = new Size(860, 420) })
                    using (var bmp = new Bitmap(860, 420))
                    {
                        box.SetTree(data.Tree, last, tr3[tr3.Count - 2]);
                        box.CreateControl();
                        box.DrawToBitmap(bmp, new Rectangle(0, 0, 860, 420));
                        bmp.Save(Path.Combine(outDir, "treebox_many_masteries.png"));
                    }
                }
                Save(new OverlayForm(data, settings, p2, tracker, Path.Combine(outDir, "debug")) { Opacity = 1 }, "overlay_pob_marauder.png");
            }
        }

        // 개발용: 1번 캐릭터(프리셋 A) → 2번 캐릭터 새로 만듦 → 1번으로 돌아옴. 프리셋 A 기록에 2번이 섞이지 않는지.
        static void CharTest(string root, string outFile)
        {
            var data = GameData.Load(AppPaths.Data);
            var dir = Path.Combine(Path.GetTempPath(), "siosa_chartest");
            Directory.CreateDirectory(dir);
            var log = Path.Combine(dir, "Client.txt");
            var sb = new System.Text.StringBuilder();
            int t = 0;
            string L(string body) => $"2026/10/20 10:{t / 60:00}:{t++ % 60:00} 1 a {body}";
            string Login() => L("[INFO Client 1] Async connecting to tok.login.pathofexile.com:20496");
            string Area(int lv, string id) => L($"[DEBUG Client 1] Generating level {lv} area \"{id}\" with seed 1");
            string Up(string name, string cls, int lv) => L($"[INFO Client 1] : {name}({cls}) 님이 {lv}레벨이 되었습니다.");
            string Pas() => L("[INFO Client 1] : 패시브 스킬 포인트를 획득했습니다.");
            string Die(string name) => L($"[INFO Client 1] : {name} 님이 사망했습니다.");
            var lines = new System.Collections.Generic.List<string>
            {
                // 1번 캐릭터 (프리셋 A)
                Login(), Area(1, "1_1_1"), Up("가캐릭", "머라우더", 2), Area(2, "1_1_2"), Area(4, "1_1_3"), Pas(),
                // 2번 캐릭터 새로 만듦 (같은 프리셋으로 켜져 있음)
                Login(), Area(1, "1_1_1"), Up("나캐릭", "위치", 2), Area(2, "1_1_2"), Area(5, "1_1_3a"), Pas(), Pas(),
                // 파티원 레벨업 (무시)
                Up("파티원", "레인저", 9),
                // 1번으로 다시 접속: 레벨업 전까지는 다른 캐릭터로 봄 → 사망 줄로 다시 1번
                Login(), Area(13, "1_1_town"), Die("가캐릭"), Area(6, "1_1_4_1"), Pas(),
            };
            File.WriteAllLines(log, lines, new System.Text.UTF8Encoding(false));
            var prog = Path.Combine(dir, "A.json");
            if (File.Exists(prog)) File.Delete(prog);
            var tr = new Tracker(data, prog, "A");
            var states = new System.Collections.Generic.List<string>();
            using (var w = new LogWatcher(log))
            {
                w.AreaEntered += tr.OnAreaEntered;
                w.LevelUp += tr.OnLevelUp;
                w.PointsGained += tr.OnPointsGained;
                w.Died += tr.OnDied;
                w.AreaEntered += (a, b, c, d) => states.Add($"{c}:{(tr.Foreign ? "다른" : "A")}");
                w.Start(0);   // 켜 둔 채로 쭉 플레이한 것처럼 (처음 읽기 규칙은 아래에서 따로)
            }
            sb.AppendLine($"캐릭터 {tr.CharName}, 다른 캐릭터 {tr.Foreign}, 레벨 {tr.CharLevel}, 패시브 {tr.PassiveTotal}");
            sb.AppendLine("방문: " + string.Join(", ", tr.P.visited));
            sb.AppendLine("지역별 상태: " + string.Join(" ", states));

            // 새 프리셋 처음 시작: 예전 로그로 캐릭터를 정하지 않는다 (끝부터)
            var oldLog = Path.Combine(dir, "old.txt");
            File.WriteAllLines(oldLog, new System.Collections.Generic.List<string> {
                Up("옛캐릭", "루미너리", 96),
                Login(), Area(1, "1_1_1"), Up("새캐릭", "쉐도우", 2), Area(13, "1_1_town"), Pas(),
                Up("파티원", "레인저", 40),
                Login(), Area(30, "1_3_1"), Up("본캐", "위치", 31), Area(31, "1_3_2"), Pas(),
            }, new System.Text.UTF8Encoding(false));
            var pf = Path.Combine(dir, "fresh.json");
            if (File.Exists(pf)) File.Delete(pf);
            var tf = new Tracker(data, pf, "fresh");
            long end = SiosaApp.ReadOnce(tf, oldLog, -1);
            sb.AppendLine($"새 프리셋: 캐릭터 {tf.CharName ?? "없음"}, 방문 {tf.P.visited.Count}, 읽은 위치 {end} / {new FileInfo(oldLog).Length}");
            // 캐릭터 목록과 고른 캐릭터로 불러오기
            var found = LogWatcher.ScanCharacters(oldLog);
            sb.AppendLine("목록: " + string.Join(" | ", found.Select(c => $"{c.Name}({c.Class}) Lv{c.Level} {(c.Created ? "새로만듦" : "-")} @{c.Start}")));
            foreach (var c in found.Where(x => x.Name != "파티원"))
            {
                var p2 = Path.Combine(dir, "r_" + c.Name + ".json");
                if (File.Exists(p2)) File.Delete(p2);
                var t2 = new Tracker(data, p2, "r");
                t2.Adopt(c.Name);
                SiosaApp.ReadOnce(t2, oldLog, c.Start);
                sb.AppendLine($"불러오기 {c.Name}: 캐릭터 {t2.CharName}, 다른 {t2.Foreign}, Lv {t2.CharLevel}, 방문 {string.Join(",", t2.P.visited)}, 패시브 {t2.PassiveTotal}");
            }
            // 4MB 씩 나눠 읽기: 한글 줄이 경계에 걸려도 줄 수·내용이 그대로인지 (9MB)
            {
                var big = Path.Combine(dir, "big.txt");
                int nLines = 0;
                using (var sw2 = new StreamWriter(big, false, new System.Text.UTF8Encoding(false)))
                    for (long written = 0; written < 9L << 20; nLines++)
                    {
                        var ln = $"2026/10/20 10:00:00 1 a [INFO Client 1] : 시오사한글이름{nLines % 7}(쉐도우) 님이 {nLines % 90 + 2}레벨이 되었습니다.";
                        sw2.Write(ln + "\r\n");
                        written += System.Text.Encoding.UTF8.GetByteCount(ln) + 2;
                    }
                int got = 0, bad = 0;
                using (var w = new LogWatcher(big))
                {
                    w.LevelUp += (nm, cl, lv) => { got++; if (!nm.StartsWith("시오사한글이름") || cl != "쉐도우") bad++; };
                    w.Start(0);
                }
                sb.AppendLine($"큰 로그 {new FileInfo(big).Length / 1048576.0:0.0}MB: 줄 {nLines}, 읽은 레벨업 {got}, 깨진 줄 {bad} → {(got == nLines && bad == 0 ? "OK" : "실패")}");

                // 마지막 줄을 쓰는 중이면 저장 위치는 그 줄 앞 (다음 실행 때 그 줄을 처음부터)
                var half = Path.Combine(dir, "half.txt");
                var full = Up("반쪽", "위치", 5) + "\n";
                File.WriteAllText(half, full + "2026/10/20 10:00:01 1 a [INFO Client 1] : 반쪽(위", new System.Text.UTF8Encoding(false));
                long posHalf;
                using (var w = new LogWatcher(half)) { w.Start(0); posHalf = w.Position; }
                long expect = System.Text.Encoding.UTF8.GetByteCount(full);
                sb.AppendLine($"쓰는 중인 줄: 저장 위치 {posHalf} (기대 {expect}, 파일 {new FileInfo(half).Length}) → {(posHalf == expect ? "OK" : "실패")}");
            }

            var real = Path.Combine(@"C:\Kakaogames\Path of Exile\logs", "KakaoClient.txt");
            if (File.Exists(real))
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var rc = LogWatcher.ScanCharacters(real);
                sb.AppendLine($"실제 로그 목록 ({sw.ElapsedMilliseconds}ms): " + string.Join(" | ", rc.Select(c => $"{c.Name}({c.Class}) Lv{c.Level} {c.Time} {(c.Created ? "새로만듦" : "-")}")));
                var sh = rc.FirstOrDefault(c => c.Name == "시오사쉐도우테스트");
                if (sh != null)
                {
                    var p3 = Path.Combine(dir, "real.json");
                    if (File.Exists(p3)) File.Delete(p3);
                    var t3 = new Tracker(data, p3, "real");
                    t3.Adopt(sh.Name);
                    SiosaApp.ReadOnce(t3, real, sh.Start);
                    sb.AppendLine($"실제 불러오기 {sh.Name}: 다른 {t3.Foreign}, Lv {t3.CharLevel}, 방문 {string.Join(",", t3.P.visited)}");
                }
            }
            File.WriteAllText(outFile, sb.ToString(), System.Text.Encoding.UTF8);
        }

        // 개발용: 게임 스크린샷으로 판별 테스트. 결과는 <이미지>.txt 로 저장.
        //   --detecttest <png> <클래스ID> <액트> [닫은 뒤 png]  (닫은 뒤 화면을 주면 칸별 아이콘 거리를 비교)
        static void DetectTest(string root, string png, string classId, int act, string closedPng = null)
        {
            var data = GameData.Load(AppPaths.Data);
            var cands = data.Offers
                .Where(o => o.act == act && o.rewards.TryGetValue(classId, out var l) && l.Count > 0)
                .Select((o, i) => new RewardDetector.Candidate { Offer = o, Items = o.rewards[classId], Pick = o.rewards[classId][0], Priority = i })
                .ToList();
            using (var bmp = new Bitmap(png))
            {
                // 실제 동작과 같게: 먼저 창 높이를 찾고, 그 높이를 만드는 후보만 판별
                var det0 = new RewardDetector(new Settings());
                var shift0 = RewardDetector.WindowOpenIn(bmp, Point.Empty, new Rectangle(0, 0, bmp.Width, bmp.Height), det0.Shifts(cands));
                var r = det0.DetectIn(bmp, Point.Empty, new Rectangle(0, 0, bmp.Width, bmp.Height), cands, shift0);
                string iconText = "";
                if (r.Match != null && r.Ties.Count > 0)
                {
                    var byIcon = new RewardDetector(new Settings()) { Icons = data.Icons }.ResolveByIconsIn(bmp, Point.Empty, r, out var why);
                    iconText = $"  아이콘 비교: {why} → " + (byIcon == null ? "확정 못 함"
                        : byIcon.Ties.Count == 0 ? byIcon.Match.Offer.id
                        : "구분불가:" + string.Join("/", new[] { byIcon }.Concat(byIcon.Ties).Select(t => t.Match.Offer.id)));
                }
                var det = new RewardDetector(new Settings());
                var shift = RewardDetector.WindowOpenIn(bmp, Point.Empty, new Rectangle(0, 0, bmp.Width, bmp.Height), det.Shifts(cands));
                bool open = shift != null;
                bool done = RewardDetector.QuestCompleteIn(bmp, Point.Empty, new Rectangle(0, 0, bmp.Width, bmp.Height));
                string still = "";
                if (r.Match != null)
                {
                    var di = new RewardDetector(new Settings()) { Icons = data.Icons };
                    still = "\n칸별 아이콘 거리 (열림): " + string.Join(" ", di.CellDistancesIn(bmp, Point.Empty, r).Select(d => d.ToString("0.0")));
                    if (closedPng != null)
                        using (var b2 = new Bitmap(closedPng))
                        {
                            var shift2 = RewardDetector.WindowOpenIn(b2, Point.Empty, new Rectangle(0, 0, b2.Width, b2.Height), det.Shifts(cands));
                            still += $"\n칸별 아이콘 거리 (닫힘, 띠 {(shift2 != null ? "보임" : "안 보임")}): " + string.Join(" ", di.CellDistancesIn(b2, Point.Empty, r).Select(d => d.ToString("0.0")));
                            bool there = di.FramesVisibleIn(b2, Point.Empty, r);
                            still += $"\n닫은 뒤: {di.LastStillInfo} → {(there ? "창 있음" : "창 없음")}";
                        }
                    di.FramesVisibleIn(bmp, Point.Empty, r);
                    still += $"\n열림: {di.LastStillInfo}";
                    still += "\n칸: " + string.Join(" ", r.Rects.Select(q => $"{q.X},{q.Y},{q.Width}x{q.Height}"));
                }
                if (shift0 != null && r.Match == null)
                {
                    // 띠는 보이는데 판별 실패: 테두리 확인을 빼고 다시 판별해 칸 자리와 테두리 선 밝기를 적는다
                    var detN = new RewardDetector(new Settings()) { SkipFrames = true };
                    var rn = detN.DetectIn(bmp, Point.Empty, new Rectangle(0, 0, bmp.Width, bmp.Height), cands, shift0);
                    if (rn.Match != null) still += $"\n테두리 빼고: {rn.Match.Offer.id} {rn.Score:0.00} ({rn.Layout})\n테두리 선: " + RewardDetector.FrameInfoIn(bmp, rn);
                }
                if (shift0 == null)
                {
                    // 띠가 가려진 화면 (툴팁 등): 높이마다 판별해 보고, 이미 열려 있던 창이면 유지될지 본다
                    var detF = new RewardDetector(new Settings()) { SkipFrames = true };
                    var best = detF.Shifts(cands).Distinct()
                        .Select(s => (s, res: detF.DetectIn(bmp, Point.Empty, new Rectangle(0, 0, bmp.Width, bmp.Height), cands, s)))
                        .Where(x => x.res.Match != null).OrderByDescending(x => x.res.Score).FirstOrDefault();
                    if (best.res != null)
                    {
                        var di = new RewardDetector(new Settings()) { Icons = data.Icons };
                        bool there = di.HeldIn(bmp, Point.Empty, best.res);
                        still = $"\n가려진 창: {best.res.Match.Offer.id} {best.res.Score:0.00} (높이 이동 {best.s}) {di.LastStillInfo} → {(there ? "유지" : "닫힘")}";
                        // 닫은 뒤 화면들(;로 구분)에서 이 창이 남아 있다고 잘못 보지 않는지
                        if (closedPng != null)
                            foreach (var cp in closedPng.Split(';'))
                                using (var b2 = new Bitmap(cp))
                                {
                                    bool held = di.HeldIn(b2, Point.Empty, best.res);
                                    still += $"\n다른 화면 {Path.GetFileName(cp)}: {di.LastStillInfo} → {(held ? "유지(오판)" : "닫힘")}";
                                }
                    }
                }
                File.WriteAllText(png + ".txt", $"창 열림: {open} (높이 이동 {shift})  퀘스트 완료: {done}\n" + (r.Match != null ? $"{r.Match.Offer.id} {r.Score:0.00} ({r.Layout})" + (r.Ties.Count > 0 ? " 구분불가: " + string.Join(",", r.Ties.Select(t => t.Match.Offer.id)) : "") + iconText : $"none {r.Score:0.00}") + still);
            }
        }

        // 개발용: PoB 코드(또는 코드 파일·링크) → 자동 선택 결과를 파일로.  --pobtest <코드|파일|링크> <결과.txt>
        static void PobTest(string root, string input, string outFile)
        {
            var data = GameData.Load(AppPaths.Data);
            var sb = new System.Text.StringBuilder();
            try
            {
                if (File.Exists(input)) input = File.ReadAllText(input);
                var build = Pob.Parse(Pob.Resolve(input));
                // 실제 불러오기 창과 같게: 레벨링으로 보이는 세트만
                var useSets = Enumerable.Range(0, build.Sets.Count).Where(i => build.Sets[i].Leveling).ToList();
                sb.AppendLine("스킬 세트: " + string.Join(", ", build.Sets.Select(x => (x.Leveling ? "[v] " : "[ ] ") + x.Title)));
                var sel = Pob.Select(data, build, useSets);
                sb.AppendLine($"클래스 {build.ClassName} ({build.Ascendancy}) → {sel.Class.ko}, 빌드 젬 {build.Gems.Select(g => g.Id).Distinct().Count()}개");
                sb.AppendLine($"도적 {sel.Bandit}");
                sb.AppendLine($"트리 {sel.Trees.Count}단계: " + string.Join(", ", sel.Trees.Select(t => $"{t.name}({(data.Tree != null ? TreeRender.Count(data.Tree, t.nodes) : 0)})")));
                sb.AppendLine($"젬 보상 퀘스트 {sel.GemOffers}개 중 {sel.Picks.Count}개 선택");
                foreach (var o in data.Offers.Where(o => sel.Picks.ContainsKey(o.id)))
                {
                    var r = o.rewards[sel.Class.id].First(x => x.en == sel.Picks[o.id]);
                    var others = o.rewards[sel.Class.id].Where(x => x != r && x.id != null && build.Gems.Any(g => g.Id == x.id)).Select(x => x.en);
                    sb.AppendLine($"  {o.id,-6} {o.ko} → {r.ko} ({r.en})" + (others.Any() ? "   [빌드에 있는 다른 후보: " + string.Join(", ", others) + "]" : ""));
                }
                sb.AppendLine("보상으로 받지 않는 빌드 젬: " + string.Join(", ", sel.NotFromRewards));
                sb.AppendLine("구매:");
                foreach (var grp in sel.Buy.Select(id => (id, v: data.WhereToBuy(id, sel.Class.id)))
                             .GroupBy(x => x.v == null ? "상점에서 판매하지 않음" : $"{x.v.act}장 · {x.v.npc} ({x.v.questKo} 완료 후)")
                             .OrderBy(g => g.First().v?.act ?? 99))
                    sb.AppendLine($"  {grp.Key}: " + string.Join(", ", grp.Select(x => data.GemName(x.id))));
                sb.AppendLine("링크:");
                foreach (var st in sel.Links)
                {
                    sb.AppendLine($"  [{st.name}]");
                    foreach (var g in st.groups)
                        sb.AppendLine("    " + string.Join("-", g.gems.Select(id => data.Gems.TryGetValue(id, out var gi) ? (gi.color ?? "w").ToUpperInvariant() : "?")).PadRight(12)
                                      + string.Join(" ─ ", g.gems.Select(data.GemName)));
                }
            }
            catch (Exception ex) { sb.AppendLine("오류: " + ex.Message); }
            File.WriteAllText(outFile, sb.ToString(), System.Text.Encoding.UTF8);
        }

        // 개발 실행: exe 위치에서 위로 올라가며 data\rewards.json 이 있는 폴더(저장소)를 찾는다. 배포(exe 하나)면 null
        //   exe 가 그 저장소의 Overlay\ 아래(개발 빌드 위치: Overlay\bin\out 등)일 때만 개발 실행으로 본다.
        //   (예전: 위쪽 어디든 data 가 있으면 개발 실행이라, 저장소 안 dist\Siosa\Siosa.exe 도 개발용 설정·프리셋을 읽었다)
        static string FindRoot()
        {
            var exeDir = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
            for (var d = new DirectoryInfo(exeDir); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "data", "rewards.json")))
                    return exeDir.StartsWith(Path.Combine(d.FullName, "Overlay") + "\\", StringComparison.OrdinalIgnoreCase) ? d.FullName : null;
            return null;
        }
    }

    // 설정창 ↔ 오버레이 전환, 로그 연결, 트레이 아이콘을 관리한다.
    class SiosaApp : ApplicationContext
    {
        readonly string root;
        readonly GameData data;
        readonly Settings settings;
        readonly NotifyIcon tray;
        Tracker tracker;   // 지금 프리셋의 진행 기록 (프리셋을 바꾸면 바뀐다)
        readonly Timer retry = new Timer { Interval = 5000 };
        OverlayForm overlay;
        LogWatcher watcher;
        string watchedPath;
        bool live;   // 이어 읽기(복원)가 끝나고 실시간 감시 중인지
        Profile profile;
        string profilePath;   // 시작한 프리셋의 파일 (저장하지 않고 시작했으면 null)

        string SettingsPath => Path.Combine(root, "settings.json");

        public SiosaApp(string root)
        {
            this.root = root;
            data = GameData.Load(AppPaths.Data);
            settings = Settings.Load(SettingsPath);
            // 실행할 때마다 설정창은 빈 상태로 시작한다 (이전 프리셋은 '불러오기'로).
            // 오버레이 진행 상황(지역·레벨·포인트)은 프리셋마다 progress\<프리셋 ID>.json 에서 이어서 읽는다.
            settings.lastSession = "";
            settings.showItems = false;

            var menu = new ContextMenuStrip();
            menu.Items.Add("설정 열기", null, (s, e) => ShowSetup());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("종료", null, (s, e) => Quit());
            tray = new NotifyIcon { Icon = Ui.AppIcon(SystemInformation.SmallIconSize.Width), Text = "Siosa", ContextMenuStrip = menu };
            tray.DoubleClick += (s, e) => ShowSetup();
            retry.Tick += (s, e) => ConnectLog();

            // 설정창은 메시지 루프(Application.Run)가 시작된 뒤에 띄운다.
            // 생성자에서 바로 띄우면 X 로 닫았을 때의 종료 신호(ExitThread)를 받을 루프가 아직 없어서
            // 창 없는 프로세스가 메모리에 남는다.
            EventHandler first = null;
            first = (s, e) => { Application.Idle -= first; ShowSetup(); };
            Application.Idle += first;
        }

        bool balloonShown;
        SetupForm openSetup;   // 지금 떠 있는 설정창 (트레이에서 또 누르면 새로 열지 않고 앞으로)

        void ShowSetup()
        {
            // 예전: 설정창이 모달이어도 트레이 메뉴는 계속 눌려서, 누를 때마다 설정창이 하나씩 더 열렸다
            if (openSetup != null)
            {
                if (openSetup.WindowState == FormWindowState.Minimized) openSetup.WindowState = FormWindowState.Normal;
                openSetup.Activate();
                return;
            }
            // 설정창을 열면 오버레이를 중지한다 (진행 기록·로그 위치는 저장 → '시작'을 누르면 그 위치부터 이어 읽어 빠지는 것이 없다).
            //   예전: 오버레이를 숨기기만 하고 버튼은 '중지'라서, 다시 보려면 중지 → 시작을 눌러야 했다 (X 는 프로그램 종료)
            if (overlay != null) StopOverlay();
            using (var f = new SetupForm(data, Path.Combine(root, "profiles"), settings, profile, profilePath) { AppRoot = AppDomain.CurrentDomain.BaseDirectory })
            {
                openSetup = f;
                f.ProgressInfo = ProgressInfo;
                f.ResetProgress = ResetProgress;
                f.ScanChars = ScanChars;
                f.RecoverProgress = RecoverProgress;
                f.Running = false;
                f.Stop = StopOverlay;
                f.HotkeysChanged = () => settings.Save(SettingsPath);
                try { f.ShowDialog(); }
                finally { openSetup = null; }
                settings.Save(SettingsPath);
                // 업데이트를 마쳤으면 새 exe 를 띄우고 끝낸다 (새 프로세스는 이 프로세스가 끝나기를 기다린다)
                if (f.RestartRequested)
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Application.ExecutablePath,
                            "--after-update " + System.Diagnostics.Process.GetCurrentProcess().Id) { UseShellExecute = false, WorkingDirectory = root });
                    }
                    catch (Exception ex) { MessageBox.Show("다시 시작하지 못했습니다. Siosa.exe 를 직접 실행해 주세요.\n" + ex.Message, "Siosa"); }
                    Quit();
                    return;
                }
                // 시작을 누르지 않고 닫았으면(X) 오버레이가 실행 중이었어도 종료
                if (!f.StartRequested) { Quit(); return; }
                profile = f.Result;
                profilePath = f.ResultPath;
                StartOverlay();
            }
            overlay.SetHotkeys(true);
            overlay.Show();
            TrimMemory();
        }

        // ── 프리셋별 진행 기록 ──
        string ProgressDir => Path.Combine(root, "progress");

        static string SafeId(string id) =>
            string.Concat((string.IsNullOrEmpty(id) ? "default" : id).Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));

        string ProgressPath(string id)
        {
            Directory.CreateDirectory(ProgressDir);
            var path = Path.Combine(ProgressDir, SafeId(id) + ".json");
            // 예전 버전의 progress.json (진행 기록 하나) → 처음 시작하는 프리셋이 이어받는다
            var old = Path.Combine(root, "progress.json");
            if (!File.Exists(path) && File.Exists(old)) File.Move(old, path);
            return path;
        }

        // 편집 화면에 보여 줄 이 프리셋의 캐릭터 ("이름 · Lv 31"). 기록이 없으면 null
        string ProgressInfo(string id)
        {
            var p = tracker != null && tracker.Id == SafeId(id) ? tracker.P
                  : File.Exists(Path.Combine(ProgressDir, SafeId(id) + ".json")) ? Progress.Load(Path.Combine(ProgressDir, SafeId(id) + ".json")) : null;
            if (p == null || string.IsNullOrEmpty(p.charName)) return null;
            return p.charLevel > 0 ? $"{p.charName} · Lv {p.charLevel}" : p.charName;
        }

        // 편집 화면 "진행 초기화": 다음에 레벨업하는 캐릭터가 이 프리셋 캐릭터가 된다 (로그 읽는 위치는 그대로)
        void ResetProgress(string id)
        {
            if (tracker != null && tracker.Id == SafeId(id))
            {
                tracker.ResetProgress();
                tracker.Save(watchedPath ?? tracker.P.logPath, live ? watcher.Position : tracker.P.offset);
                return;
            }
            var path = Path.Combine(ProgressDir, SafeId(id) + ".json");
            if (!File.Exists(path)) return;
            var t = new Tracker(data, path, SafeId(id));
            t.ResetProgress();
            t.Save(t.P.logPath, t.P.offset);
        }

        void StartOverlay()
        {
            // 프리셋이 바뀌면 그 프리셋의 진행 기록으로 바꾸고, 그 기록의 읽던 위치부터 로그를 이어 읽는다
            string id = SafeId(profile.id);
            if (tracker == null || tracker.Id != id)
            {
                if (tracker != null && live) tracker.Save(watchedPath, watcher.Position);
                watcher?.Dispose();
                watcher = null;
                watchedPath = null;
                live = false;
                tracker = new Tracker(data, ProgressPath(profile.id), id);
                var t = tracker;
                t.Changed += () => { if (live && t == tracker) t.Save(watchedPath, watcher.Position); };
                overlay?.SetTracker(tracker);
            }
            if (overlay == null)
            {
                DebugLog.Path = Path.Combine(root, "debug", "siosa.log");
                overlay = new OverlayForm(data, settings, profile, tracker, Path.Combine(root, "debug"));
                overlay.SettingsChanged = () => settings.Save(SettingsPath);
                overlay.StageChanged = SaveStages;
                tray.Visible = true;
                if (!balloonShown) tray.ShowBalloonTip(3000, "Siosa 실행 중", "트레이 아이콘을 더블클릭하면 설정을 다시 열 수 있습니다.", ToolTipIcon.Info);
                balloonShown = true;   // 설정창을 열 때마다 중지 → 다시 시작하므로 처음 한 번만
            }
            else overlay.SetProfile(profile);
            ConnectLog();
        }

        // 단축키(J/K, U/I)로 바꾼 링크·트리 단계를 프리셋 파일에 저장 → 다시 켜도 그 단계부터
        //   (예전: 실행 중에만 유지되고 재시작하면 프리셋에 저장된 단계로 돌아갔다). 단계 두 값만 바꾼다
        void SaveStages()
        {
            try
            {
                if (profilePath == null || !File.Exists(profilePath)) return;
                var p = Profile.Load(profilePath);
                if (Profile.IdOf(p, profilePath) != profile.id) return;   // 그사이 다른 프리셋으로 바뀐 파일
                if (p.linkStage == profile.linkStage && p.treeStage == profile.treeStage) return;
                p.linkStage = profile.linkStage;
                p.treeStage = profile.treeStage;
                p.Save(profilePath);
            }
            catch (Exception ex) { DebugLog.Write("단계 저장 실패: " + ex.Message); }
        }

        // 오버레이와 로그 감시를 끈다 (진행 상황은 저장). 설정창의 "중지".
        void StopOverlay()
        {
            if (live && tracker != null) tracker.Save(watchedPath, watcher.Position);
            retry.Stop();
            watcher?.Dispose();
            watcher = null;
            watchedPath = null;
            live = false;
            overlay?.Close();
            overlay?.Dispose();
            overlay = null;
            tray.Visible = false;
        }

        // 로그 파일 연결. 못 찾으면 5초마다 다시 찾는다 (게임을 나중에 켜는 경우).
        void ConnectLog()
        {
            var path = !string.IsNullOrEmpty(settings.logPath) ? settings.logPath : LogDetect.Find();
            if (path == null || !File.Exists(path))
            {
                overlay.SetStatus("게임 로그를 찾는 중… (게임을 켜면 자동으로 연결됩니다)");
                retry.Start();
                return;
            }
            retry.Stop();
            if (watcher != null && string.Equals(path, watchedPath, StringComparison.OrdinalIgnoreCase)) return;

            watcher?.Dispose();
            live = false;
            watchedPath = path;
            watcher = new LogWatcher(path);
            watcher.AreaEntered += tracker.OnAreaEntered;
            watcher.LevelUp += tracker.OnLevelUp;
            watcher.PointsGained += tracker.OnPointsGained;
            watcher.Died += tracker.OnDied;
            // 같은 로그를 이전에 읽었다면 그 위치부터 이어 읽는다 → 꺼져 있던 동안의 진행도 반영.
            // 처음이면 지금 끝부터 (예전 진행은 설정창 "이전 진행 불러오기"에서 캐릭터를 골라 읽는다)
            long resume = string.Equals(tracker.P.logPath, path, StringComparison.OrdinalIgnoreCase) && tracker.P.offset >= 0 ? tracker.P.offset : -1;
            watcher.Start(resume);
            live = true;
            tracker.Save(watchedPath, watcher.Position);
        }

        // 설정창(버튼 수백 개)을 닫은 뒤 쓰지 않는 메모리를 돌려준다. 상주 중에는 오버레이만 남는다.
        static void TrimMemory()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Native.SetProcessWorkingSetSize(System.Diagnostics.Process.GetCurrentProcess().Handle, (IntPtr)(-1), (IntPtr)(-1));
        }

        // 최근 로그를 처음부터 다시 읽어 방문 기록(퀘스트 완료 판정)을 다시 만든다
        string LogPath()
        {
            var path = !string.IsNullOrEmpty(settings.logPath) ? settings.logPath : LogDetect.Find();
            return path != null && File.Exists(path) ? path : null;
        }

        // 설정창 "이전 진행 불러오기": 로그에 나온 캐릭터 목록
        System.Collections.Generic.List<LogWatcher.LogChar> ScanChars()
        {
            var path = LogPath();
            return path == null ? null : LogWatcher.ScanCharacters(path);
        }

        // 고른 캐릭터의 시작부터 로그를 다시 읽어 이 프리셋의 진행 기록을 만든다
        void RecoverProgress(string id, LogWatcher.LogChar c)
        {
            var path = LogPath();
            if (path == null) return;
            if (tracker != null && tracker.Id == SafeId(id))
            {
                // 실행 중인 프리셋: 감시를 고른 위치부터 다시 시작
                watcher?.Dispose();
                watcher = null;
                watchedPath = null;
                live = false;
                tracker.Adopt(c.Name);
                tracker.Save(path, c.Start);
                if (overlay != null) ConnectLog();
                else tracker.Save(path, ReadOnce(tracker, path, c.Start));
                return;
            }
            var t = new Tracker(data, ProgressPath(id), SafeId(id));
            t.Adopt(c.Name);
            t.Save(path, ReadOnce(t, path, c.Start));
        }

        // 로그를 from 부터 끝까지 한 번 읽어 진행 기록에 반영하고 끝 위치를 돌려준다
        internal static long ReadOnce(Tracker t, string path, long from)
        {
            using (var w = new LogWatcher(path))
            {
                w.AreaEntered += t.OnAreaEntered;
                w.LevelUp += t.OnLevelUp;
                w.PointsGained += t.OnPointsGained;
                w.Died += t.OnDied;
                w.Start(from);
                return w.Position;
            }
        }

        void Quit()
        {
            if (live && tracker != null) tracker.Save(watchedPath, watcher.Position);
            retry.Dispose();
            tray.Visible = false;
            watcher?.Dispose();
            overlay?.Close();
            ExitThread();
        }
    }
}
