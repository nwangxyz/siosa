using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Siosa
{
    // 보상 창 칸 위치 계산. 1920x1080 실측값을 게임 창 높이 비율로 환산한다 (PoE UI 는 높이 기준으로 커진다).
    //   - 보상 창은 오른쪽 인벤토리를 뺀 왼쪽 영역의 가운데에 뜬다.
    //   - 보상 칸은 67px, 칸 간격 101.25px, 한 줄 가운데 정렬, 첫 줄 위쪽 y=490.
    //   - 여러 줄: 줄 사이 간격은 21px (실측: 보조 젬 7개 → 4개 + 3개, 둘째 줄도 가운데 정렬).
    //     줄을 나누는 규칙은 7개 하나만 실측했으므로 줄 수(1~4) x 나누는 방식을 모두 대입해 본다.
    public struct RewardLayout
    {
        public int[] Sizes;         // 줄마다 보상 개수 (예: 4,3)
        public bool LastRowLeft;    // 마지막 줄을 첫 줄 왼쪽에 맞추는지 (false: 가운데)

        public RewardLayout(int[] sizes, bool lastRowLeft) { Sizes = sizes; LastRowLeft = lastRowLeft; }

        public int FirstRow => Sizes[0];

        // 줄 수 rows 로 고르게 나누기. frontLoaded: 남는 개수를 앞줄부터 (13 → 5,4,4) / 뒷줄부터 (13 → 4,4,5)
        static int[] Balanced(int n, int rows, bool frontLoaded)
        {
            int q = n / rows, r = n % rows;
            return Enumerable.Range(0, rows).Select(i => q + ((frontLoaded ? i < r : i >= rows - r) ? 1 : 0)).ToArray();
        }

        // 한 줄 per 개씩 채우기 (13, 5 → 5,5,3)
        static int[] Chunked(int n, int per) =>
            Enumerable.Range(0, (n + per - 1) / per).Select(i => Math.Min(per, n - i * per)).ToArray();

        // 화면을 보지 않고 그릴 때(수동 강조·위치 확인) 쓰는 추정: 5개 이하는 한 줄, 그 이상은 5개 기준 줄 수로 고르게
        public static RewardLayout Default(int count)
        {
            if (count <= 5) return new RewardLayout(new[] { Math.Max(1, count) }, false);
            return new RewardLayout(Balanced(count, (count + 4) / 5, true), false);
        }

        // 화면과 비교해 볼 배치 후보 (중복 제거). 한 줄은 최대 8개.
        public static IEnumerable<RewardLayout> Hypotheses(int count)
        {
            var seen = new HashSet<string>();
            var list = new List<RewardLayout>();
            void Add(int[] sizes)
            {
                if (sizes.Length == 0 || sizes.Any(x => x < 1 || x > 8)) return;
                foreach (var left in new[] { false, true })
                {
                    if (left && (sizes.Length < 2 || sizes[sizes.Length - 1] >= sizes[0])) continue;   // 왼쪽 정렬이 의미 있을 때만
                    var l = new RewardLayout(sizes, left);
                    if (seen.Add(l.ToString())) list.Add(l);
                }
            }
            Add(Default(count).Sizes);
            for (int rows = 1; rows <= Math.Min(4, count); rows++)
            {
                Add(Balanced(count, rows, true));
                Add(Balanced(count, rows, false));
                Add(Chunked(count, (count + rows - 1) / rows));
            }
            for (int per = 3; per <= 8; per++) Add(Chunked(count, per));
            return list;
        }

        public override string ToString() => string.Join("+", Sizes) + (LastRowLeft ? " (마지막 줄 왼쪽)" : "");
    }

    public static class RewardGeometry
    {
        const double Slot = 67, Pitch = 101.25, Top = 490, InventoryWidth = 663, CenterFix = 1.5;
        // 칸 크기 = 60.5 x 칸 수 + 6.5 (1칸 67px, 2칸 127.5px 실측). 칸 사이 간격 34.25px, 줄 사이 간격 21px.
        const double Unit = 60.5, Border = 6.5, Gap = Pitch - Slot, RowGap = 21;

        static double Size(int cells) => Unit * Math.Max(1, cells) + Border;

        static List<List<Reward>> Rows(IList<Reward> items, RewardLayout layout)
        {
            var rows = new List<List<Reward>>();
            int start = 0;
            foreach (var n in layout.Sizes)
            {
                if (start >= items.Count) break;
                rows.Add(items.Skip(start).Take(n).ToList());
                start += n;
            }
            if (start < items.Count) rows.Add(items.Skip(start).ToList());   // 개수가 안 맞으면 나머지를 마지막 줄로
            return rows;
        }

        // 보상 창은 내용 높이에 맞춰 위아래 가운데 정렬된다. 젬 한 줄(67px)일 때를 기준으로
        // 창 전체(띠/명판/칸)가 위로 올라가는 양 (1080p 단위). 예: 플라스크(2칸 높이) 한 줄 → 30.25
        public static double Shift(IList<Reward> items, RewardLayout layout)
        {
            var rows = Rows(items, layout);
            double block = rows.Sum(r => r.Max(it => Size(it.h))) + RowGap * (rows.Count - 1);
            return (block - Slot) / 2;
        }

        static IntPtr gameWindow;

        // 게임 창의 그리기 영역(화면 좌표). 창 모드여도 맞게 계산된다. 게임 창을 못 찾으면 주 모니터.
        public static Rectangle GameClient()
        {
            if (gameWindow == IntPtr.Zero || !Native.IsWindow(gameWindow)) gameWindow = FindGameWindow();
            if (gameWindow != IntPtr.Zero && Native.GetClientRect(gameWindow, out var rc) && rc.Right > 0 && rc.Bottom > 0)
            {
                var pt = new Native.POINT();
                Native.ClientToScreen(gameWindow, ref pt);
                return new Rectangle(pt.X, pt.Y, rc.Right, rc.Bottom);
            }
            return Screen.PrimaryScreen.Bounds;
        }

        static IntPtr FindGameWindow()
        {
            var found = IntPtr.Zero;
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (found == IntPtr.Zero &&
                        p.ProcessName.StartsWith("PathOfExile", StringComparison.OrdinalIgnoreCase) &&
                        !p.ProcessName.StartsWith("PathOfExile2", StringComparison.OrdinalIgnoreCase))
                        found = p.MainWindowHandle;
                }
                catch { }
                finally { p.Dispose(); }
            }
            return found;
        }

        // 각 보상의 화면 좌표 (해상도로만 계산. 예전의 수동 미세 조정값은 없앰)
        public static List<Rectangle> Place(Rectangle client, IList<Reward> items, RewardLayout layout)
        {
            double s = client.Height / 1080.0, gap = Gap * s;
            double cx = client.Left + (client.Width - InventoryWidth * s) / 2 + CenterFix * s;
            double top = client.Top + (Top - Shift(items, layout)) * s;
            var rows = Rows(items, layout);
            double firstRowLeft = 0;
            var result = new List<Rectangle>();
            for (int ri = 0; ri < rows.Count; ri++)
            {
                var row = rows[ri];
                double width = row.Sum(it => Size(it.w) * s) + gap * (row.Count - 1);
                bool lastPartial = ri > 0 && ri == rows.Count - 1 && row.Count < rows[0].Count;
                double x = lastPartial && layout.LastRowLeft ? firstRowLeft : cx - width / 2;
                double rowH = row.Max(it => Size(it.h)) * s;
                if (ri == 0) firstRowLeft = x;
                foreach (var it in row)
                {
                    double w = Size(it.w) * s, h = Size(it.h) * s;
                    // 한 줄 안에서 높이가 다른 아이템은 세로 가운데 정렬로 가정 (아직 실측 전)
                    result.Add(new Rectangle((int)Math.Round(x), (int)Math.Round(top + (rowH - h) / 2), (int)Math.Round(w), (int)Math.Round(h)));
                    x += w + gap;
                }
                top += rowH + RowGap * s;
            }
            return result;
        }

        // 보상 창이 열렸는지 확인할 작은 영역 4곳과 기준 색 (1080p 실측, 보상 창 가운데 기준 상대 좌표).
        //   "보상을 선택하십시오" 띠의 무늬 없는 부분 2곳 + 위쪽 "보상" 금색 명판 2곳.
        static readonly (int dx, int y, int w, int h, Color c)[] Probes =
        {
            (-210, 434, 100, 12, Color.FromArgb(41, 16, 12)),
            (105, 434, 100, 12, Color.FromArgb(35, 15, 11)),
            (-70, 372, 20, 10, Color.FromArgb(180, 132, 74)),
            (45, 372, 20, 10, Color.FromArgb(170, 119, 68)),
        };

        // "퀘스트 완료" 띠 (화면 아래 가운데, 게임 화면 전체 기준 가운데 정렬). 1080p 실측:
        //   글자 양옆의 주황색 띠 (72,29,10), 가운데 글자 영역에는 밝은 픽셀이 약 18%.
        // panelOpen: 오른쪽 인벤토리가 열려 있으면 가운데 정렬 요소가 왼쪽 빈 영역의 가운데로 옮겨질 수 있다.
        public static (Rectangle left, Rectangle right, Rectangle text) QuestCompleteProbes(Rectangle client, bool panelOpen = false)
        {
            double s = client.Height / 1080.0, top = client.Top;
            double cx = panelOpen ? client.Left + (client.Width - InventoryWidth * s) / 2 + CenterFix * s : client.Left + client.Width / 2.0;
            Rectangle R(double dx, double y, double w, double h) =>
                new Rectangle((int)Math.Round(cx + dx * s), (int)Math.Round(top + y * s), Math.Max(2, (int)(w * s)), Math.Max(2, (int)(h * s)));
            return (R(-150, 835, 60, 14), R(90, 835, 60, 14), R(-55, 832, 110, 24));
        }

        public static readonly Color QuestCompleteColor = Color.FromArgb(72, 29, 10);

        public static IEnumerable<(Rectangle rect, Color color)> WindowProbes(Rectangle client, double shift = 0)
        {
            double s = client.Height / 1080.0;
            double cx = client.Left + (client.Width - InventoryWidth * s) / 2 + CenterFix * s;
            foreach (var p in Probes)
                yield return (new Rectangle((int)Math.Round(cx + p.dx * s), (int)Math.Round(client.Top + (p.y - shift) * s),
                                            Math.Max(2, (int)(p.w * s)), Math.Max(2, (int)(p.h * s))), p.c);
        }

        // 첫 줄 양옆의 "있으면 안 되는" 칸 (개수가 다른 창과 구분용)
        // 모든 줄의 양옆 칸 = "있으면 안 되는" 칸. 개수가 다른 창, 그리고 창의 일부만 설명하는 배치를 걸러낸다.
        // (예: 젬 8개(4+4) 창에 힐록 보상 5개를 "4 + 왼쪽 1"로 대 보면 둘째 줄 두 번째 칸에 젬이 있어 걸린다)
        public static Rectangle[] Neighbors(Rectangle client, List<Rectangle> placed, int[] rowSizes)
        {
            double s = client.Height / 1080.0, pitch = Pitch * s;
            int slot = (int)Math.Round(Slot * s);
            // 보상 창 안쪽(가운데에서 좌우 약 326px)에 판정 영역(칸 안쪽 70%)이 들어오는 칸만 확인한다.
            // 바깥은 게임 화면이라 밝을 수 있다.
            double cx = client.Left + (client.Width - InventoryWidth * s) / 2 + CenterFix * s, half = 326 * s, inset = slot * 0.15;
            var result = new List<Rectangle>();
            int start = 0;
            foreach (var n in rowSizes)
            {
                if (start >= placed.Count) break;
                var first = placed[start];
                var last = placed[Math.Min(start + n, placed.Count) - 1];
                result.Add(new Rectangle((int)(first.X - pitch), first.Y, slot, slot));
                result.Add(new Rectangle((int)(last.Right + (Pitch - Slot) * s), last.Y, slot, slot));
                start += n;
            }
            return result.Where(r => r.Left + inset >= cx - half && r.Right - inset <= cx + half).ToArray();
        }
    }

    // 화면 전체를 덮는 투명 창. 칸에 테두리를 그린다. 클릭은 게임으로 통과.
    public class HighlightForm : Form
    {
        static readonly Color Key = Color.FromArgb(255, 0, 255);
        List<Rectangle> boxes = new List<Rectangle>();
        HashSet<int> mains = new HashSet<int>();   // 금색으로 강조할 칸 (나머지는 노란 위치 확인용)
        string label = "";

        public HighlightForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Bounds = SystemInformation.VirtualScreen;
            BackColor = Key;
            TransparencyKey = Key;
            DoubleBuffered = true;
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
            Native.ExcludeFromCapture(Handle);
        }

        // 고른 보상 칸(들)을 강조. 어느 퀘스트 창인지 확정할 수 없으면 후보마다 하나씩 여러 칸.
        public void ShowMarked(List<Rectangle> marked, string text) =>
            ShowBoxes(marked, Enumerable.Range(0, marked.Count), text);

        // 고르지 않은 보상 창: 테두리 없이 칸 아래에 회색 안내 문구만 (판별은 됐다는 표시)
        public void ShowNote(List<Rectangle> cells, string text)
        {
            ShowBoxes(cells, Enumerable.Empty<int>(), text);
            note = true;
        }
        bool note;

        // 개발용(--notetest): 스크린샷 위에 강조·문구를 그린 그림
        public Bitmap DevRender(Bitmap shot)
        {
            // 창 좌표는 가상 화면(모니터 여러 개면 음수 시작) 기준. TextRenderer 는 좌표 변환을 무시하므로
            // 가상 화면 크기 캔버스에 스크린샷을 (0,0) 화면 자리에 놓고 그린 뒤 잘라 낸다.
            using (var canvas = new Bitmap(Bounds.Width, Bounds.Height))
            {
                using (var g = Graphics.FromImage(canvas))
                {
                    g.DrawImageUnscaled(shot, -Bounds.X, -Bounds.Y);
                    OnPaint(new PaintEventArgs(g, new Rectangle(Point.Empty, canvas.Size)));
                }
                return canvas.Clone(new Rectangle(-Bounds.X, -Bounds.Y, shot.Width, shot.Height), canvas.PixelFormat);
            }
        }

        // 여러 칸(위치 확인용) + 그중 강조할 칸
        public void ShowBoxes(List<Rectangle> screenRects, IEnumerable<int> mainIndices, string text)
        {
            note = false;
            boxes = screenRects.Select(r => new Rectangle(r.X - Bounds.X, r.Y - Bounds.Y, r.Width, r.Height)).ToList();
            mains = new HashSet<int>(mainIndices);
            label = text;
            Invalidate();
            if (!Visible) Show();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            for (int i = 0; i < boxes.Count && !note; i++)
            {
                if (mains.Contains(i))
                {
                    using (var pen = new Pen(Theme.Accent, 4)) g.DrawRectangle(pen, Rectangle.Inflate(boxes[i], 4, 4));
                    using (var pen = new Pen(Color.Black, 1)) g.DrawRectangle(pen, Rectangle.Inflate(boxes[i], 7, 7));
                }
                else using (var pen = new Pen(Color.Yellow, 2)) g.DrawRectangle(pen, boxes[i]);
            }
            if (string.IsNullOrEmpty(label) || boxes.Count == 0) return;

            // 설명은 보상 칸 아래쪽에 (보상 창의 "보상을 선택하십시오" 줄을 가리지 않도록)
            var anchor = mains.Count > 0 ? boxes[mains.Min()] : note ? boxes.Aggregate(Rectangle.Union) : boxes[0];
            int bottom = boxes.Max(b => b.Bottom);
            var sz = TextRenderer.MeasureText(label, Theme.Bold);
            var lr = new Rectangle(anchor.X + anchor.Width / 2 - sz.Width / 2 - 6, bottom + 14, sz.Width + 12, sz.Height + 6);
            using (var b = new SolidBrush(Color.FromArgb(20, 20, 22))) g.FillRectangle(b, lr);
            TextRenderer.DrawText(g, label, Theme.Bold, new Point(lr.X + 6, lr.Y + 3), note ? Color.FromArgb(160, 162, 168) : Theme.Accent, Color.FromArgb(20, 20, 22));
        }
    }
}
