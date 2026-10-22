using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Siosa
{
    // "이전 진행 불러오기": 로그에서 찾은 캐릭터 중 이 프리셋 캐릭터를 고르는 창.
    //   파티원 레벨업도 로그에 남아 목록에 섞이므로 추측하지 않고 사용자가 고른다.
    public class CharPickForm : Form
    {
        public LogWatcher.LogChar Picked { get; private set; }

        public CharPickForm(List<LogWatcher.LogChar> chars)
        {
            Text = "이전 진행 불러오기";
            Icon = Ui.AppIcon(16);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            Padding = new Padding(14);

            var col = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Back };
            col.Controls.Add(new Label
            {
                Text = "이 프리셋으로 이어서 키울 캐릭터를 고르세요. 그 캐릭터의 시작부터 로그를 다시 읽어\n방문 지역과 패시브 포인트를 채웁니다. (최근에 레벨업한 순서 · 파티원도 섞여 있을 수 있음)",
                AutoSize = true, ForeColor = Theme.Dim, Margin = new Padding(0, 0, 0, 10),
            });
            foreach (var c in chars)
            {
                var ch = c;
                string when = ch.Time.Length >= 16 ? ch.Time.Substring(5, 11) : ch.Time;   // "10/20 15:49"
                var b = new FlatButton($"{ch.Name}  ({ch.Class})  Lv {ch.Level}   ·   {when}{(ch.Created ? "" : "   · 만든 기록 없음")}", FlatButton.Kind.Normal, 520)
                {
                    Margin = new Padding(0, 0, 0, 6),
                };
                b.Click += (s, e) => { Picked = ch; DialogResult = DialogResult.OK; };
                col.Controls.Add(b);
            }
            var note = new Label
            {
                Text = "'만든 기록 없음': 로그에 캐릭터를 만든 부분이 없어 처음 보인 접속부터 읽습니다 (그 전 진행은 빠짐).",
                AutoSize = true, ForeColor = Theme.Dim, Margin = new Padding(0, 6, 0, 0),
            };
            if (chars.Exists(x => !x.Created)) col.Controls.Add(note);

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, BackColor = Theme.Back };
            var cancel = new FlatButton("취소", FlatButton.Kind.Normal, 80) { Margin = new Padding(6, 6, 0, 0), DialogResult = DialogResult.Cancel };
            bottom.Controls.Add(cancel);
            CancelButton = cancel;

            Controls.Add(col);
            Controls.Add(bottom);
            ClientSize = new Size(560, Math.Min(620, 28 + 52 + chars.Count * 40 + 30 + 40 + 10));
        }
    }
}
