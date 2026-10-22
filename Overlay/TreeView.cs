using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Siosa
{
    // 패시브 트리 대략 그리기: 전체 트리는 흐린 점·선, 찍은 노드는 금색, 바로 앞 단계보다 새로 찍은 노드는 초록.
    // 노드 이름·설명은 그리지 않는다 (어느 쪽으로 가는지 모양만).
    public static class TreeRender
    {
        static readonly Color Faint = Color.FromArgb(58, 60, 66), FaintNode = Color.FromArgb(78, 80, 88);
        static readonly Color Taken = Color.FromArgb(232, 190, 92), Fresh = Color.FromArgb(120, 220, 130);

        static readonly Font SideFont = new Font("Malgun Gothic", 8f);
        static readonly Font SideBold = new Font("Malgun Gothic", 8f, FontStyle.Bold);
        static readonly Color MasteryName = Color.FromArgb(150, 190, 240);   // 숙련 이름 (고정 색)
        static readonly Color SideText = Color.FromArgb(205, 200, 185), SideDim = Color.FromArgb(130, 132, 140);

        // 오른쪽 열 폭 (트리 높이 기준)
        public static int SideWidth(int size) => Math.Min(300, Math.Max(150, (int)(size * 0.72)));

        // 왼쪽: 본 트리 (size × size). 오른쪽 열: 위에 찍은 전직 트리, 아래에 고른 마스터리 효과.
        // 마스터리가 많아 트리 높이를 넘으면 다음 열로 넘긴다 (예전: 아래로 늘어나 편집 화면·트리 창 밖으로 잘림).
        public static Bitmap Render(TreeData t, TreeStage cur, TreeStage prev, int size, Color back)
        {
            var have = new HashSet<int>(cur?.nodes ?? new List<int>());
            var before = prev != null ? new HashSet<int>(prev.nodes) : null;
            float big = size >= 400 ? 1.6f : 1f;   // 편집 화면처럼 크게 그릴 때는 점을 조금 크게
            int side = SideWidth(size), gap = 10, width = size + gap + side;

            // 전직: 찍은 노드가 가장 많은 전직 트리 하나
            var asc = t.Asc.Values.Select(a => (a, n: a.Id.Count(have.Contains))).Where(x => x.n > 0).OrderByDescending(x => x.n).Select(x => x.a).FirstOrDefault();
            int ascBox = asc != null ? Math.Min(side, (int)(size * 0.5)) : 0;

            // 마스터리: 항목마다 이름 줄(굵게, 색: 새로 고른 것 초록 · 그 전부터 있던 것 금색) + 효과 줄들(효과마다 한 줄)
            // 같은 숙련(예: 생명력 숙련 마스터리 노드가 여러 개)은 이름을 한 번만 쓰고 옵션을 모은다.
            //   이름은 고정 색, 옵션은 새로 고른 것 초록 · 그 전부터 있던 것 금색
            var items = new List<(string name, List<(string text, bool fresh)> effects)>();
            if (cur?.masteries != null)
                foreach (var kv in cur.masteries)
                {
                    if (!int.TryParse(kv.Key, out var node)) continue;
                    var parts = t.MasteryParts(node, kv.Value);
                    if (parts == null) continue;
                    bool fresh = prev != null && (prev.masteries == null || !prev.masteries.TryGetValue(kv.Key, out var pe) || pe != kv.Value);
                    int gi = items.FindIndex(x => x.name == parts.Value.name);
                    if (gi < 0) { items.Add((parts.Value.name, new List<(string, bool)>())); gi = items.Count - 1; }
                    foreach (var e in parts.Value.effects) items[gi].effects.Add((e, fresh));
                }
            const TextFormatFlags F = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl;   // 잴 때와 그릴 때 줄바꿈이 같도록
            int textTop = ascBox > 0 ? ascBox + 8 : 0;
            int titleH = TextRenderer.MeasureText("마스터리", SideFont, Size.Empty, TextFormatFlags.NoPadding).Height + 4;   // 줄바꿈 옵션을 주면 폭 0 이라 글자마다 줄이 바뀐다
            int nameH = TextRenderer.MeasureText("가", SideBold, Size.Empty, TextFormatFlags.NoPadding).Height + 1;
            const int itemGap = 6;
            // 잴 때도 그림(비트맵)에 그릴 때와 같은 Graphics 로 (화면 기준으로 재면 줄바꿈 위치가 달라 빈 줄이 생겼다)
            List<int[]> effH;
            using (var mb = new Bitmap(1, 1))
            using (var mg = Graphics.FromImage(mb))
                effH = items.Select(it => it.effects.Select(e => TextRenderer.MeasureText(mg, e.text, SideFont, new Size(side - 16, 0), F).Height).ToArray()).ToList();
            var heights = items.Select((it, i) => nameH + effH[i].Sum() + itemGap).ToList();

            // 항목마다 위치: 첫 열은 전직 상자·제목 아래부터, 트리 높이를 넘으면 오른쪽 열 맨 위부터 (항목은 나누지 않음)
            var pos = new List<Point>();
            int col = 0, y0 = textTop + titleH, yy = y0, bottom = items.Count > 0 ? y0 : 0;
            for (int i = 0; i < items.Count; i++)
            {
                bool colEmpty = col == 0 ? yy == y0 : yy == 0;
                if (yy + heights[i] - itemGap > size && !colEmpty) { col++; yy = 0; }
                pos.Add(new Point(size + gap + col * (side + gap), yy));
                yy += heights[i];
                bottom = Math.Max(bottom, yy - itemGap);
            }
            width = size + gap + (col + 1) * side + col * gap;
            int height = Math.Max(size, bottom);

            var bmp = new Bitmap(width, height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(back);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int pad = Math.Max(4, size / 40);
                Draw(g, t, new RectangleF(pad, pad, size - 2 * pad, size - 2 * pad), have, before, big);

                if (asc != null)
                {
                    var rect = new RectangleF(size + gap, 0, ascBox - 1, ascBox - 1);
                    using (var bg = new SolidBrush(Color.FromArgb(Math.Min(255, back.R + 8), Math.Min(255, back.G + 8), Math.Min(255, back.B + 10))))
                        g.FillRectangle(bg, rect);
                    using (var border = new Pen(Faint)) g.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);
                    rect.Inflate(-ascBox * 0.1f, -ascBox * 0.1f);
                    Draw(g, asc, rect, have, before, big * 1.4f);
                }

                if (items.Count > 0)
                {
                    TextRenderer.DrawText(g, "마스터리", SideFont, new Point(size + gap, textTop), SideDim, back, F);
                    for (int i = 0; i < items.Count; i++)
                    {
                        int x = pos[i].X, y = pos[i].Y;
                        TextRenderer.DrawText(g, items[i].name, SideBold, new Point(x, y), MasteryName, back, TextFormatFlags.NoPadding);
                        y += nameH;
                        for (int k = 0; k < items[i].effects.Count; k++)
                        {
                            var (text, fresh) = items[i].effects[k];
                            // 옵션마다 앞에 "·" (두 줄로 넘어간 옵션과 다음 옵션이 구분되게)
                            TextRenderer.DrawText(g, "·", SideBold, new Point(x + 6, y), fresh ? Fresh : Taken, back, TextFormatFlags.NoPadding);
                            TextRenderer.DrawText(g, text, SideFont, new Rectangle(x + 16, y, side - 16, effH[i][k]), fresh ? Fresh : Taken, back, F);
                            y += effH[i][k];
                        }
                    }
                }
            }
            return bmp;
        }

        // 트리 하나를 area 안에 맞춰 그린다: 흐린 전체 → 찍은 길(금색, 새로 찍은 건 초록). 노드 크기는 찍었어도 같음
        static void Draw(Graphics g, TreeData t, RectangleF area, HashSet<int> have, HashSet<int> before, float big)
        {
            double w = Math.Max(1, t.MaxX - t.MinX), h = Math.Max(1, t.MaxY - t.MinY);
            double s = Math.Min(area.Width / w, area.Height / h);
            double ox = area.X + (area.Width - w * s) / 2, oy = area.Y + (area.Height - h * s) / 2;
            PointF P(int i) => new PointF((float)(ox + (t.X[i] - t.MinX) * s), (float)(oy + (t.Y[i] - t.MinY) * s));
            bool On(int i) => have.Contains(t.Id[i]);
            bool New(int i) => before != null && !before.Contains(t.Id[i]);
            float R(int i) => (t.Kind[i] == 2 || t.Kind[i] == 4 ? 1.8f : t.Kind[i] == 1 || t.Kind[i] == 5 ? 1.4f : 0.8f) * big;

            using (var faint = new Pen(Faint, 1f))
                foreach (var (a, b) in t.Edges) g.DrawLine(faint, P(a), P(b));
            using (var fb = new SolidBrush(FaintNode))
            using (var fr = new Pen(FaintNode, 1f))
                for (int i = 0; i < t.Id.Length; i++)
                {
                    float r = R(i);
                    var p = P(i);
                    if (t.Kind[i] == 5) g.DrawEllipse(fr, p.X - r, p.Y - r, r * 2, r * 2);   // 마스터리는 고리
                    else g.FillEllipse(fb, p.X - r, p.Y - r, r * 2, r * 2);
                }

            // 찍은 길: 두 끝이 모두 찍힌 연결 (클래스·전직 시작 노드는 찍은 목록에 없으므로 시작점과 이어진 것도 포함)
            using (var taken = new Pen(Taken, 1f))   // 안 찍은 선과 같은 굵기 (색으로만 구분)
            using (var fresh = new Pen(Fresh, 1f))
                foreach (var (a, b) in t.Edges)
                {
                    bool ea = On(a) || t.Kind[a] == 4, eb = On(b) || t.Kind[b] == 4;
                    if (!(ea && eb) || (!On(a) && !On(b))) continue;
                    g.DrawLine((On(a) && New(a)) || (On(b) && New(b)) ? fresh : taken, P(a), P(b));
                }
            using (var tb = new SolidBrush(Taken))
            using (var nb = new SolidBrush(Fresh))
            using (var tr = new Pen(Taken, 1f))
            using (var nr = new Pen(Fresh, 1f))
                for (int i = 0; i < t.Id.Length; i++)
                {
                    if (!On(i)) continue;
                    float r = R(i);
                    var p = P(i);
                    if (t.Kind[i] == 5) g.DrawEllipse(New(i) ? nr : tr, p.X - r, p.Y - r, r * 2, r * 2);
                    else g.FillEllipse(New(i) ? nb : tb, p.X - r, p.Y - r, r * 2, r * 2);
                }
        }

        // 트리 데이터에 있는 노드만 센다 (전직 노드는 본 트리 데이터에 없음)
        public static int Count(TreeData t, IEnumerable<int> nodes) => nodes?.Count(t.Index.ContainsKey) ?? 0;
    }

    // 편집 화면용: 창 크기에 맞춰 정사각형으로 그린다 (크기가 바뀔 때만 다시 그림)
    public class TreeBox : Control
    {
        TreeData tree;
        TreeStage cur, prev;
        Bitmap cache;
        Size cacheFor;   // 이 칸 크기에 맞춰 그린 그림
        public string Empty = "";

        public TreeBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Ui.Card;
        }

        public void SetTree(TreeData t, TreeStage cur, TreeStage prev)
        {
            tree = t; this.cur = cur; this.prev = prev;
            cache?.Dispose(); cache = null;
            Invalidate();
        }

        protected override void OnResize(EventArgs e) { cache?.Dispose(); cache = null; base.OnResize(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            // 트리 크기: 높이와 (폭 - 오른쪽 열) 중 작은 쪽
            int size = Math.Min(Height, (int)((Width - 10) / 1.72));
            if (tree == null || cur == null || size < 40)
            {
                TextRenderer.DrawText(g, Empty, Theme.Body, ClientRectangle, Theme.Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }
            if (cache == null || cacheFor != ClientSize)
            {
                cache?.Dispose();
                cache = TreeRender.Render(tree, cur, prev, size, BackColor);
                // 마스터리 열이 늘어 칸보다 넓거나 높으면 트리 크기를 줄여 다시 (두세 번이면 맞는다)
                for (int k = 0; k < 4 && (cache.Width > Width || cache.Height > Height) && size > 120; k++)
                {
                    size = (int)(size * Math.Min((double)Width / cache.Width, (double)Height / cache.Height) * 0.98);
                    cache.Dispose();
                    cache = TreeRender.Render(tree, cur, prev, Math.Max(120, size), BackColor);
                }
                cacheFor = ClientSize;
            }
            g.DrawImageUnscaled(cache, Math.Max(0, (Width - cache.Width) / 2), Math.Max(0, (Height - cache.Height) / 2));
        }

        protected override void Dispose(bool disposing) { if (disposing) cache?.Dispose(); base.Dispose(disposing); }
    }
}
