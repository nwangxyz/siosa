using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Siosa
{
    // 젬 고르기 창 (링크 탭의 '+ 젬'). 한국어·영문 이름 일부로 찾고, 더블클릭 또는 Enter 로 고른다.
    public class GemPickerForm : Form
    {
        readonly GameData data;
        readonly string classId;
        readonly TextBox tbSearch = new TextBox { Dock = DockStyle.Top };
        readonly ListBox lbResults = new ListBox
        {
            Dock = DockStyle.Fill, BackColor = Theme.Panel, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle,
            DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 22, IntegralHeight = false,
        };

        public string Result { get; private set; }   // 고른 젬 ID

        public GemPickerForm(GameData data, string classId)
        {
            this.data = data;
            this.classId = classId;
            Text = "젬 추가";
            Icon = Ui.AppIcon(16);
            Size = new Size(640, 420);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            Padding = new Padding(10);

            var head = new Label { Text = "젬 이름 일부를 입력하세요 (한국어·영문). 더블클릭 또는 Enter 로 추가", Dock = DockStyle.Top, Height = 24 };
            Controls.Add(lbResults);
            Controls.Add(new Panel { Dock = DockStyle.Top, Height = 6 });
            Controls.Add(tbSearch);
            Controls.Add(head);

            tbSearch.TextChanged += (s, e) => Search();
            tbSearch.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Down && lbResults.Items.Count > 0) { lbResults.Focus(); lbResults.SelectedIndex = 0; e.Handled = true; }
                if (e.KeyCode == Keys.Enter) { Pick(); e.SuppressKeyPress = true; }
            };
            lbResults.DoubleClick += (s, e) => Pick();
            lbResults.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { Pick(); e.SuppressKeyPress = true; } };
            lbResults.DrawItem += DrawItem;
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
            Shown += (s, e) => tbSearch.Focus();
        }

        // 개발용(--tabshot): 검색어를 넣은 상태
        public void DevSearch(string q) => tbSearch.Text = q;

        void Search()
        {
            lbResults.BeginUpdate();
            lbResults.Items.Clear();
            var q = tbSearch.Text.Trim();
            if (q.Length > 0)
                foreach (var kv in data.Gems
                    .Where(kv => !kv.Key.Contains("Royale") &&
                                 (kv.Value.ko.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || kv.Value.en.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0))
                    .OrderBy(kv => kv.Value.ko.StartsWith(q) || kv.Value.en.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(kv => kv.Value.ko)
                    .Take(40))
                    lbResults.Items.Add(kv.Key);
            lbResults.EndUpdate();
        }

        void DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var id = (string)lbResults.Items[e.Index];
            var g = data.Gems[id];
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var back = new SolidBrush(sel ? Color.FromArgb(70, 74, 82) : Theme.Panel)) e.Graphics.FillRectangle(back, e.Bounds);
            TextRenderer.DrawText(e.Graphics, g.ko, Theme.Bold, new Point(e.Bounds.X + 6, e.Bounds.Y + 3), Theme.GemText(g.color));
            var v = classId != null ? data.WhereToBuy(id, classId) : null;
            string where = v != null ? $"{v.act}장 · {v.npc}" : "상점에서 판매하지 않음";
            TextRenderer.DrawText(e.Graphics, $"{g.en}   ·   {where}", Theme.Body, new Point(e.Bounds.X + 210, e.Bounds.Y + 3), Theme.Dim);
        }

        void Pick()
        {
            var id = lbResults.SelectedItem as string ?? (lbResults.Items.Count > 0 ? (string)lbResults.Items[0] : null);
            if (id == null) return;
            Result = id;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
