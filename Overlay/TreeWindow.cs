using System;
using System.Drawing;
using System.Windows.Forms;

namespace Siosa
{
    // 패시브 트리 창: 오버레이와 따로 떠 있는 창. 평소에는 클릭이 게임으로 통과하고,
    // 오버레이 이동 모드(이동 단축키) 동안에만 끌어서 옮길 수 있다. 크기는 +/- 단축키로 (settings.treeSize).
    public class TreeWindow : Form
    {
        const int Pad = 8;
        readonly Settings settings;
        Bitmap img;
        string title = "", sub = "";
        bool editMode;
        public Action SettingsChanged;

        public TreeWindow(Settings settings)
        {
            this.settings = settings;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Theme.Back;
            Opacity = 0.92;
            DoubleBuffered = true;
            StartPosition = FormStartPosition.Manual;
            var wa = Screen.PrimaryScreen.WorkingArea;
            var home = new Point(wa.Left + 360, wa.Top + wa.Height / 3);
            Location = settings.treeX >= 0 ? Ui.OnScreen(new Point(settings.treeX, settings.treeY), home) : home;
            Size = new Size(200, 100);
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
            Native.ExcludeFromCapture(Handle);   // 보상 칸을 캡처할 때 섞이지 않도록
        }

        // 내용 바꾸기 (그림은 오버레이가 만들어 넘긴다. 이 창이 버리지 않음)
        public void SetContent(string title, string sub, Bitmap img)
        {
            this.title = title; this.sub = sub; this.img = img;
            int head = Theme.Bold.Height + 6;
            int w = Math.Max(img?.Width ?? 0, TextRenderer.MeasureText(title + sub, Theme.Bold).Width) + Pad * 2;
            Size = new Size(w, Pad + head + (img?.Height ?? 0) + Pad);
            Invalidate();
        }

        // 이동 모드: 클릭 통과를 끄고 끌어서 옮김. 끝나면 위치 저장
        public bool EditMode
        {
            get => editMode;
            set
            {
                if (editMode == value) return;
                editMode = value;
                if (IsHandleCreated)
                {
                    int ex = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE);
                    Native.SetWindowLong(Handle, Native.GWL_EXSTYLE, editMode ? ex & ~Native.WS_EX_TRANSPARENT : ex | Native.WS_EX_TRANSPARENT);
                }
                if (!editMode)
                {
                    settings.treeX = Left;
                    settings.treeY = Top;
                    SettingsChanged?.Invoke();
                }
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!editMode || e.Button != MouseButtons.Left) return;
            Native.ReleaseCapture();
            Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, IntPtr.Zero);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var pen = new Pen(editMode ? Theme.Accent : Color.FromArgb(70, 72, 78)))
                g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            const TextFormatFlags F = TextFormatFlags.NoPadding;
            TextRenderer.DrawText(g, title, Theme.Bold, new Point(Pad, Pad), Theme.Accent, F);
            int tw = TextRenderer.MeasureText(title, Theme.Bold, Size.Empty, F).Width;
            TextRenderer.DrawText(g, sub, Theme.Body, new Point(Pad + tw, Pad + 1), Theme.Dim, F);
            if (img != null) g.DrawImageUnscaled(img, Pad, Pad + Theme.Bold.Height + 6);
        }
    }
}
