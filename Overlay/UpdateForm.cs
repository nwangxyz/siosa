using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Siosa
{
    // 업데이트 진행 창: 받는 중 n% → 압축 푸는 중 → 바꾸는 중. 끝나면 설정창이 닫히고 새 버전이 자동으로 뜬다
    public class UpdateForm : Form
    {
        readonly Label lbText = new Label { AutoSize = false, Dock = DockStyle.Top, Height = 26, ForeColor = Theme.Text, TextAlign = ContentAlignment.MiddleLeft };
        readonly Bar bar = new Bar { Dock = DockStyle.Top, Height = 14 };

        public UpdateForm(string version)
        {
            Text = "Siosa 업데이트";
            Icon = Ui.AppIcon(16);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ControlBox = false;   // 바꾸는 도중에 닫지 않도록
            ShowInTaskbar = false;
            BackColor = Theme.Back;
            Font = Theme.Body;
            Padding = new Padding(18, 14, 18, 18);
            ClientSize = new Size(420, 110);
            var title = new Label { Text = $"v{version} 으로 업데이트하는 중", Font = Theme.Bold, ForeColor = Theme.Accent, AutoSize = false, Dock = DockStyle.Top, Height = 28 };
            Controls.Add(bar);
            Controls.Add(lbText);
            Controls.Add(title);
            SetProgress("준비 중…", 0);
        }

        // percent < 0: 진행률을 모름 (글자만 바꾼다)
        public void SetProgress(string text, int percent)
        {
            lbText.Text = text;
            if (percent >= 0) bar.Value = Math.Min(100, percent);
        }

        class Bar : Control
        {
            int value;
            public int Value { get => value; set { this.value = value; Invalidate(); } }
            public Bar() { Ui.Smooth(this); }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Theme.Back);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
                using (var path = Ui.Round(r, 6))
                using (var b = new SolidBrush(Ui.Field))
                    g.FillPath(b, path);
                if (value <= 0) return;
                var f = new RectangleF(r.X, r.Y, Math.Max(r.Height, r.Width * value / 100f), r.Height);
                using (var path = Ui.Round(f, 6))
                using (var b = new SolidBrush(Theme.Accent))
                    g.FillPath(b, path);
            }
        }
    }
}
