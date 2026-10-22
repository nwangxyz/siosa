using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Siosa
{
    // 설정창용 직접 그리는 부품 (Windows 기본 버튼에 색만 입힌 투박한 모양 대신).
    // 추가 라이브러리 없이 GDI+ 로 그린다.
    static class Ui
    {
        // 저장된 창 위치가 지금 화면 어디에도 보이지 않으면(모니터·해상도가 바뀜, 보조 모니터를 뺌) 기본 위치로.
        //   왼쪽 위 모서리 근처(120×30)가 어느 화면의 작업 영역 안에 60×20 이상 보이면 그대로 둔다
        public static Point OnScreen(Point saved, Point fallback)
        {
            var head = new Rectangle(saved, new Size(120, 30));
            foreach (var sc in Screen.AllScreens)
            {
                var r = Rectangle.Intersect(sc.WorkingArea, head);
                if (r.Width >= 60 && r.Height >= 20) return saved;
            }
            return fallback;
        }

        public static readonly Color Card = Color.FromArgb(37, 39, 43);        // 카드 배경
        public static readonly Color CardBorder = Color.FromArgb(52, 55, 61);
        public static readonly Color Field = Color.FromArgb(47, 50, 55);       // 입력칸·태그 배경
        public static readonly Color Danger = Color.FromArgb(232, 110, 100);

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // 소켓 색 (점·태그 테두리)
        public static Color Socket(string gem)
        {
            switch (gem)
            {
                case "r": return Color.FromArgb(214, 88, 78);
                case "g": return Color.FromArgb(96, 186, 96);
                case "b": return Color.FromArgb(92, 140, 232);
                case "w": return Color.FromArgb(215, 215, 215);
                default: return Color.FromArgb(120, 122, 128);
            }
        }

        public static Color Mix(Color a, Color b, float t) => Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

        // 앱 아이콘 (assets\siosa.ico, exe 에 포함). 원하는 크기에 가장 가까운 그림을 고른다
        public static Icon AppIcon(int size)
        {
            using (var s = typeof(Ui).Assembly.GetManifestResourceStream("Siosa.siosa.ico"))
                return s == null ? SystemIcons.Application : new Icon(s, size, size);
        }

        internal static void Smooth(Control c)
        {
            typeof(Control).GetMethod("SetStyle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(c, new object[] { ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true });
        }
    }

    // 둥근 버튼: 기본 / 강조(금색) / 삭제(빨강) / 테두리 없음
    public class FlatButton : Control, IButtonControl
    {
        // 창의 AcceptButton / CancelButton (Enter / Esc) 으로 쓸 수 있도록
        public DialogResult DialogResult { get; set; }
        public void NotifyDefault(bool value) { }
        public void PerformClick() { if (Enabled) OnClick(EventArgs.Empty); }
        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (DialogResult != DialogResult.None && FindForm() is Form f) f.DialogResult = DialogResult;
        }

        public enum Kind { Normal, Primary, Danger, Ghost, Tab }
        Kind kind;
        bool hover, down;

        public FlatButton(string text, Kind kind = Kind.Normal, int width = 0)
        {
            Ui.Smooth(this);
            this.kind = kind;
            Font = Theme.Body;
            Cursor = Cursors.Hand;
            Height = 28;
            Margin = new Padding(4, 0, 0, 0);
            fixedWidth = width;
            Text = text;
        }

        readonly int fixedWidth;

        public Kind Style { get => kind; set { kind = value; Invalidate(); } }

        bool selected;
        public bool Selected { get => selected; set { selected = value; Invalidate(); } }   // 탭: 지금 보이는 탭

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Width = fixedWidth > 0 ? fixedWidth : TextRenderer.MeasureText(Text, Font).Width + 22;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (kind == Kind.Tab) { PaintTab(g); return; }
            Color fill, border, fg;
            switch (kind)
            {
                case Kind.Primary:
                    fill = Color.FromArgb(140, 104, 38); border = Theme.Accent; fg = Color.FromArgb(255, 244, 220); break;
                case Kind.Danger:
                    fill = Color.FromArgb(52, 44, 46); border = Color.FromArgb(110, 62, 60); fg = Ui.Danger; break;
                case Kind.Ghost:
                    fill = Parent?.BackColor ?? Theme.Back; border = fill; fg = Theme.Dim; break;
                default:
                    fill = Color.FromArgb(52, 55, 61); border = Color.FromArgb(66, 70, 77); fg = Theme.Text; break;
            }
            if (!Enabled) { fill = Ui.Mix(fill, Parent?.BackColor ?? Theme.Back, 0.6f); border = fill; fg = Color.FromArgb(90, 92, 98); }
            else if (down) fill = Ui.Mix(fill, Color.Black, 0.18f);
            else if (hover)
            {
                fill = kind == Kind.Ghost ? Color.FromArgb(52, 55, 61) : Ui.Mix(fill, Color.White, 0.10f);
                if (kind == Kind.Ghost) fg = Theme.Text;
            }
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Ui.Round(r, 6))
            using (var b = new SolidBrush(fill))
            using (var p = new Pen(border))
            {
                g.FillPath(b, path);
                g.DrawPath(p, path);
            }
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        // 탭: 고른 탭은 카드 색 바탕 + 금색 글자 + 아래 금색 줄, 나머지는 흐린 글자
        void PaintTab(Graphics g)
        {
            if (selected || hover)
                using (var path = Ui.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height + 8), 7))
                using (var b = new SolidBrush(selected ? Ui.Card : Color.FromArgb(40, 42, 47)))
                    g.FillPath(b, path);
            if (selected)
                using (var bar = new SolidBrush(Theme.Accent)) g.FillRectangle(bar, 10, Height - 3, Width - 20, 3);
            var fg = selected ? Theme.Accent : hover ? Theme.Text : Theme.Dim;
            TextRenderer.DrawText(g, Text, selected ? Theme.Bold : Font, ClientRectangle, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    // 둥근 체크 상자 (CheckBox 대신)
    public class FlatCheck : Control
    {
        bool isChecked, hover;
        public event EventHandler CheckedChanged;

        public FlatCheck(string text)
        {
            Ui.Smooth(this);
            Text = text;
            Font = Theme.Body;
            Cursor = Cursors.Hand;
            Height = 28;
            Width = TextRenderer.MeasureText(text, Font).Width + 30;
        }

        public bool Checked
        {
            get => isChecked;
            set { if (isChecked == value) return; isChecked = value; Invalidate(); CheckedChanged?.Invoke(this, EventArgs.Empty); }
        }

        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var box = new RectangleF(1.5f, (Height - 16) / 2f, 16, 16);
            using (var path = Ui.Round(box, 4))
            {
                if (isChecked)
                {
                    using (var b = new SolidBrush(Theme.Accent)) g.FillPath(b, path);
                    using (var p = new Pen(Color.FromArgb(40, 30, 10), 2f))
                        g.DrawLines(p, new[] { new PointF(box.X + 3.8f, box.Y + 8.4f), new PointF(box.X + 6.8f, box.Y + 11.2f), new PointF(box.X + 12.4f, box.Y + 5f) });
                }
                else
                {
                    using (var b = new SolidBrush(Ui.Field)) g.FillPath(b, path);
                    using (var p = new Pen(hover ? Theme.Accent : Color.FromArgb(90, 94, 102), 1.2f)) g.DrawPath(p, path);
                }
            }
            TextRenderer.DrawText(g, Text, Font, new Rectangle(24, 0, Width - 24, Height), hover ? Color.White : Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
        }
    }

    // 드롭다운 (ComboBox 대신). ComboBox 는 테두리·화살표 칸을 Windows 가 하얗게 그려서 다크 테마와 안 맞는다.
    // 둥근 칸 + ▾, 누르면 어두운 메뉴로 고른다. ComboBox 와 같은 이름의 멤버만 쓴다.
    public class FlatCombo : Control
    {
        public readonly System.Collections.Generic.List<object> Items = new System.Collections.Generic.List<object>();
        int index = -1;
        bool hover, open;
        public event EventHandler SelectedIndexChanged;

        public FlatCombo(int width)
        {
            Ui.Smooth(this);
            Font = Theme.Bold;
            Cursor = Cursors.Hand;
            Size = new Size(width, 28);
        }

        public int SelectedIndex
        {
            get => index;
            set
            {
                if (value == index) return;
                index = value;
                Invalidate();
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public object SelectedItem
        {
            get => index >= 0 && index < Items.Count ? Items[index] : null;
            set => SelectedIndex = Items.IndexOf(value);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        // 메뉴는 하나를 계속 다시 쓴다. 닫힐 때(Closed) 지우면 Windows 가 닫은 직후 그 메뉴를 다시 건드려서
        // ObjectDisposedException 으로 프로그램이 꺼졌다.
        ContextMenuStrip menu;

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (menu == null)
            {
                menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = false, Font = Theme.Body };
                menu.Closed += (s, a) => { open = false; Invalidate(); };
            }
            menu.Items.Clear();
            for (int i = 0; i < Items.Count; i++)
            {
                int k = i;
                var item = new ToolStripMenuItem(Items[i].ToString()) { ForeColor = Theme.Text, AutoSize = false, Size = new Size(Width - 2, 26) };
                if (i == index) item.Font = Theme.Bold;
                // 고른 뒤의 처리(목록 다시 만들기)는 메뉴가 다 닫힌 다음에
                item.Click += (s, a) => BeginInvoke((Action)(() => SelectedIndex = k));
                menu.Items.Add(item);
            }
            open = true;
            Invalidate();
            menu.Show(this, new Point(0, Height + 2));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) menu?.Dispose();
            base.Dispose(disposing);
        }

        // 개발용(--combotest): 메뉴 열기 / 항목 고르기
        public void DevOpen() => OnClick(EventArgs.Empty);
        public void DevPick(int i) => menu.Items[i].PerformClick();

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Ui.Round(r, 6))
            using (var b = new SolidBrush(hover ? Ui.Mix(Ui.Field, Color.White, 0.06f) : Ui.Field))
            using (var p = new Pen(open ? Theme.Accent : Color.FromArgb(66, 70, 77)))
            {
                g.FillPath(b, path);
                g.DrawPath(p, path);
            }
            TextRenderer.DrawText(g, SelectedItem?.ToString() ?? "", Font, new Rectangle(10, 0, Width - 32, Height), Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            // ▾
            float cx = Width - 15, cy = Height / 2f + 1;
            using (var arrow = new SolidBrush(open || hover ? Theme.Accent : Theme.Dim))
                g.FillPolygon(arrow, new[] { new PointF(cx - 4.5f, cy - 2.5f), new PointF(cx + 4.5f, cy - 2.5f), new PointF(cx, cy + 3f) });
        }
    }

    // 어두운 메뉴 (드롭다운 목록)
    public class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Selected ? Color.White : Theme.Text;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { }   // 체크 표시 대신 굵은 글씨

        class DarkColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Ui.Field;
            public override Color MenuBorder => Color.FromArgb(70, 74, 82);
            public override Color MenuItemBorder => Color.FromArgb(70, 74, 82);
            public override Color MenuItemSelected => Color.FromArgb(68, 72, 80);
            public override Color MenuItemSelectedGradientBegin => Color.FromArgb(68, 72, 80);
            public override Color MenuItemSelectedGradientEnd => Color.FromArgb(68, 72, 80);
            public override Color ImageMarginGradientBegin => Ui.Field;
            public override Color ImageMarginGradientMiddle => Ui.Field;
            public override Color ImageMarginGradientEnd => Ui.Field;
        }
    }


    // 젬 태그: 소켓 색 테두리의 알약 모양. 필요하면 왼쪽 체크(켜기/끄기)와 오른쪽 ✕(빼기)
    public class GemPill : Control
    {
        readonly Color accent;
        readonly bool removable, checkable;
        bool isChecked = true, overX, hover;
        public event Action RemoveClicked;
        public event Action CheckedChanged;
        const int XW = 18, CW = 20;

        public GemPill(string name, string socket, bool removable = true, bool checkable = false, bool isChecked = true)
        {
            Ui.Smooth(this);
            Text = name;
            Font = Theme.Body;
            accent = Ui.Socket(socket);
            this.removable = removable;
            this.checkable = checkable;
            this.isChecked = isChecked;
            Height = 26;
            Width = TextRenderer.MeasureText(name, Theme.Bold).Width + 20 + (removable ? XW : 0) + (checkable ? CW : 0);
            Margin = new Padding(0, 1, 0, 1);
            Cursor = checkable ? Cursors.Hand : Cursors.Default;
        }

        public bool Checked
        {
            get => isChecked;
            set { if (isChecked == value) return; isChecked = value; Invalidate(); CheckedChanged?.Invoke(); }
        }

        bool OnX(int x) => removable && x >= Width - XW - 4;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool x = OnX(e.X);
            if (x != overX) { overX = x; Cursor = x || checkable ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = overX = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (OnX(e.X)) RemoveClicked?.Invoke();
                else if (checkable) Checked = !Checked;
            }
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Ui.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // 꺼진 젬은 흐리게
            var a = isChecked ? accent : Ui.Mix(accent, Ui.Field, 0.65f);
            var r = new RectangleF(1, 1, Width - 2.5f, Height - 2.5f);
            using (var path = Ui.Round(r, r.Height / 2))
            using (var b = new SolidBrush(hover ? Ui.Mix(Ui.Field, accent, 0.12f) : Ui.Field))
            using (var p = new Pen(Color.FromArgb(hover ? 230 : 150, a), 1.4f))
            {
                g.FillPath(b, path);
                g.DrawPath(p, path);
            }
            int left = 10;
            if (checkable)
            {
                // 체크 표시: 켜짐 = 채운 원 + ✓, 꺼짐 = 빈 원
                var cr = new RectangleF(7, (Height - 14) / 2f, 14, 14);
                if (isChecked)
                {
                    using (var cb = new SolidBrush(accent)) g.FillEllipse(cb, cr);
                    using (var cp = new Pen(Ui.Field, 1.8f))
                        g.DrawLines(cp, new[] { new PointF(cr.X + 3.5f, cr.Y + 7.2f), new PointF(cr.X + 6f, cr.Y + 9.8f), new PointF(cr.X + 10.6f, cr.Y + 4.4f) });
                }
                else using (var cp = new Pen(Theme.Dim, 1.3f)) g.DrawEllipse(cp, cr);
                left = 7 + CW;
            }
            var textRect = new Rectangle(left, 0, Width - left - (removable ? XW + 6 : 8), Height);
            var fg = isChecked ? Ui.Mix(accent, Color.White, 0.25f) : Color.FromArgb(110, 112, 118);
            TextRenderer.DrawText(g, Text, Theme.Bold, textRect, fg, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            if (!removable) return;
            // ✕
            var xr = new RectangleF(Width - XW - 5, (Height - 16) / 2f, 16, 16);
            if (overX) using (var hb = new SolidBrush(Color.FromArgb(90, Ui.Danger))) g.FillEllipse(hb, xr);
            using (var xp = new Pen(overX ? Color.White : Theme.Dim, 1.4f))
            {
                float c = 4f;
                g.DrawLine(xp, xr.X + c, xr.Y + c, xr.Right - c, xr.Bottom - c);
                g.DrawLine(xp, xr.Right - c, xr.Y + c, xr.X + c, xr.Bottom - c);
            }
        }
    }

    // 퀘스트 보상 선택 칩: 젬 색 바탕의 둥근 칸. 왼쪽 원이 고르면 ✓ 로 바뀌고 금색 테두리
    public class ChoiceChip : Control
    {
        readonly string gem;   // r/g/b/w, 장비면 null
        bool selected, hover;
        const int CheckSlot = 22;

        public ChoiceChip(string text, string gem)
        {
            Ui.Smooth(this);
            this.gem = gem;
            Text = text;
            Font = Theme.Bold;
            Cursor = Cursors.Hand;
            Height = 30;
            // 고르면 굵게 바뀌어도 폭이 흔들리지 않도록 굵은 글꼴 기준
            Width = TextRenderer.MeasureText(text, Theme.Bold).Width + CheckSlot + 14;
            Margin = new Padding(0, 0, 6, 6);
        }

        public bool Selected { get => selected; set { if (selected == value) return; selected = value; Invalidate(); } }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Ui.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // 고른 칸은 젬 색을 살짝 밝게(흰색을 많이 섞으면 색이 바래 보임), 고르지 않은 칸은 차분하게
            var baseFill = Theme.GemBack(gem, false);
            var fill = selected ? Ui.Mix(baseFill, Color.White, 0.16f) : Ui.Mix(baseFill, Ui.Card, 0.35f);
            if (hover) fill = Ui.Mix(fill, Color.White, 0.10f);
            var r = new RectangleF(1, 1, Width - 2.5f, Height - 2.5f);
            using (var path = Ui.Round(r, 7))
            using (var b = new SolidBrush(fill))
            using (var p = new Pen(selected ? Theme.Accent : Ui.Mix(fill, Color.White, 0.18f), selected ? 2f : 1f))
            {
                g.FillPath(b, path);
                g.DrawPath(p, path);
            }
            // 왼쪽 원: 고름 = 금색 채운 원 + ✓, 안 고름 = 빈 원
            var cr = new RectangleF(9, (Height - 13) / 2f, 13, 13);
            if (selected)
            {
                using (var cb = new SolidBrush(Theme.Accent)) g.FillEllipse(cb, cr);
                using (var cp = new Pen(Color.FromArgb(40, 30, 10), 1.8f))
                    g.DrawLines(cp, new[] { new PointF(cr.X + 3.2f, cr.Y + 6.8f), new PointF(cr.X + 5.6f, cr.Y + 9.2f), new PointF(cr.X + 9.8f, cr.Y + 4.2f) });
            }
            else using (var cp = new Pen(Color.FromArgb(150, 255, 255, 255), 1.2f)) g.DrawEllipse(cp, cr);
            var fg = selected ? Color.White : Color.FromArgb(225, 226, 228);
            TextRenderer.DrawText(g, Text, selected ? Theme.Bold : Theme.Body, new Rectangle(CheckSlot + 6, 0, Width - CheckSlot - 10, Height), fg,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    // 묶음에 필요한 소켓 색을 작은 원으로 (예: ● ● ●  파랑·초록·파랑)
    public class SocketDots : Control
    {
        readonly string[] sockets;
        public SocketDots(string[] sockets, int slots)
        {
            Ui.Smooth(this);
            this.sockets = sockets;
            Size = new Size(Math.Max(slots, 1) * 13 + 4, 26);
            Margin = new Padding(0, 1, 8, 1);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Ui.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 0; i < sockets.Length; i++)
            {
                var r = new RectangleF(2 + i * 13, Height / 2f - 5, 10, 10);
                using (var b = new SolidBrush(Ui.Socket(sockets[i]))) g.FillEllipse(b, r);
                using (var p = new Pen(Color.FromArgb(60, Color.Black))) g.DrawEllipse(p, r);
            }
        }
    }

    // 둥근 카드 (단계 하나). 안의 Body 에 줄을 쌓고, 높이는 내용에 맞춘다
    public class Card : Panel
    {
        public readonly FlowLayoutPanel Body = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
            BackColor = Ui.Card, Location = new Point(14, 12), Margin = new Padding(0),
        };
        public bool Highlight;   // 오버레이에 보여 줄 단계면 금색 테두리

        public Card()
        {
            Ui.Smooth(this);
            BackColor = Ui.Card;
            Padding = new Padding(14, 12, 14, 12);
            Margin = new Padding(0, 0, 0, 10);
            Controls.Add(Body);
            // 카드 높이 = 내용 높이. 예전에는 Body 크기 변경 알림으로만 맞췄는데, 창 크기를 바꾸는 중에 Body 가 잠깐 0 으로
            // 재진 뒤 다시 커질 때 알림이 오지 않아 카드가 테두리만 남았다 (젬 구매 첫 카드). 배치가 끝날 때마다 다시 잰다.
            Body.SizeChanged += (s, e) => SyncHeight();
            Body.Layout += (s, e) => { if (IsHandleCreated) BeginInvoke((Action)SyncHeight); };
        }

        // 내용(Body)이 실제로 차지하는 높이로 카드 높이를 맞춘다
        public void SyncHeight()
        {
            if (IsDisposed) return;
            int inner = Body.Width > 0 ? Body.GetPreferredSize(new Size(Body.MaximumSize.Width > 0 ? Body.MaximumSize.Width : Body.Width, 0)).Height : Body.Height;
            int h = Math.Max(Body.Height, inner) + Padding.Vertical;
            if (Height != h) Height = h;
        }

        // 카드 폭에 맞춰 안의 줄이 줄바꿈되도록
        public void FitWidth(int width)
        {
            Width = width;
            Body.MaximumSize = new Size(width - Padding.Horizontal, 0);
            foreach (Control c in Body.Controls)
            {
                c.MaximumSize = new Size(width - Padding.Horizontal, 0);
                // 줄 안에서 따로 줄바꿈하는 영역 (Tag = 왼쪽에 둔 글자 폭). 넘친 젬이 그 영역 안에서 맞춰 내려가게
                foreach (Control inner in c.Controls)
                    if (inner.Tag is int offset) inner.MaximumSize = new Size(width - Padding.Horizontal - offset, 0);
            }
            SyncHeight();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Ui.Round(r, 10))
            using (var b = new SolidBrush(Ui.Card))
            using (var p = new Pen(Highlight ? Color.FromArgb(170, Theme.Accent) : Ui.CardBorder, Highlight ? 1.5f : 1f))
            {
                g.FillPath(b, path);
                g.DrawPath(p, path);
            }
        }
    }

    // 둥근 입력칸 (테두리 없는 TextBox 를 감싼다)
    public class FieldBox : Panel
    {
        public readonly TextBox Box = new TextBox { BorderStyle = BorderStyle.None, BackColor = Ui.Field, ForeColor = Theme.Text };
        bool focused;

        readonly bool hasClear;

        // 폭을 바꾸면 안의 글자칸도 같이 (지역 팁 탭처럼 창 크기에 맞춰 늘어나는 칸). ✕ 가 있는 칸은 폭을 고정해서 쓴다
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (!hasClear) Box.Width = Math.Max(20, Width - 18);
            Invalidate();
        }

        public FieldBox(int width, Font font = null, bool clearable = false)
        {
            hasClear = clearable;
            Ui.Smooth(this);
            BackColor = Ui.Card;
            Box.Font = font ?? Theme.Body;
            Size = new Size(width, 28);
            Margin = new Padding(0, 0, 6, 0);
            Box.Location = new Point(9, (28 - Box.PreferredHeight) / 2 + 1);
            Box.Width = width - 18 - (clearable ? 20 : 0);
            Controls.Add(Box);
            Box.GotFocus += (s, e) => { focused = true; Invalidate(); };
            Box.LostFocus += (s, e) => { focused = false; Invalidate(); };
            if (clearable)
            {
                // 오른쪽 ✕: 글자가 있을 때만 보이고, 누르면 지운다. Esc 도 같은 동작
                var clear = new ClearButton { Location = new Point(width - 25, 6), Visible = false };
                clear.Click += (s, e) => { Box.Text = ""; Box.Focus(); };
                Controls.Add(clear);
                Box.TextChanged += (s, e) => clear.Visible = Box.Text.Length > 0;
                Box.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Escape && Box.Text.Length > 0) { Box.Text = ""; e.SuppressKeyPress = true; }
                };
            }
        }

        // 입력칸 안의 작은 ✕ (마우스를 올리면 밝아짐)
        class ClearButton : Control
        {
            bool hover;
            public ClearButton()
            {
                Ui.Smooth(this);
                Size = new Size(16, 16);
                Cursor = Cursors.Hand;
            }
            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Ui.Field);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var b = new SolidBrush(hover ? Color.FromArgb(110, 114, 122) : Color.FromArgb(78, 82, 90))) g.FillEllipse(b, 0, 0, 15, 15);
                using (var p = new Pen(Ui.Field, 1.6f))
                {
                    g.DrawLine(p, 4.5f, 4.5f, 10.5f, 10.5f);
                    g.DrawLine(p, 10.5f, 4.5f, 4.5f, 10.5f);
                }
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Ui.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Ui.Round(r, 6))
            using (var b = new SolidBrush(Ui.Field))
            using (var p = new Pen(focused ? Theme.Accent : Color.FromArgb(66, 70, 77)))
            {
                g.FillPath(b, path);
                g.DrawPath(p, path);
            }
        }
    }
}
