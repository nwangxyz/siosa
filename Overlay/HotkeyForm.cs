using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Siosa
{
    // 단축키 변경 창 (메인 화면 '단축키 변경'). 키 칸을 누르고 새 조합을 누르면 바뀐다.
    //   Ctrl 또는 Alt 가 들어간 조합만 (게임 채팅 입력과 겹치지 않게). Esc = 바꾸기 취소.
    //   저장하면 settings.hotkeys 에 쓰고, 실행 중인 오버레이는 설정창을 닫을 때 다시 등록한다.
    public class HotkeyForm : Form
    {
        readonly Settings settings;
        readonly Dictionary<string, Keys> keys = new Dictionary<string, Keys>();
        readonly Dictionary<string, FlatButton> boxes = new Dictionary<string, FlatButton>();
        readonly Label lbStatus = new Label { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Dim };
        readonly FlatButton btnSave;
        string capturing;   // 지금 키를 기다리는 기능

        public HotkeyForm(Settings settings)
        {
            this.settings = settings;
            Text = "단축키 변경";
            Icon = Ui.AppIcon(16);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            Padding = new Padding(14);
            KeyPreview = true;

            foreach (var a in Hotkeys.All) keys[a.Id] = Hotkeys.Get(settings, a.Id);

            var head = new Label
            {
                Text = "바꿀 키 칸을 누른 뒤 새 조합을 누르세요. Ctrl 또는 Alt 가 들어가야 합니다. (Esc: 바꾸기 취소)",
                AutoSize = true, ForeColor = Theme.Dim, Margin = new Padding(0, 0, 0, 10),
            };

            var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, BackColor = Theme.Back, Margin = new Padding(0) };
            foreach (var a in Hotkeys.All)
            {
                string id = a.Id;
                grid.Controls.Add(new Label { Text = a.Name, AutoSize = false, Size = new Size(190, 30), TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 2, 8, 2) });
                var box = new FlatButton("", FlatButton.Kind.Normal, 160) { Margin = new Padding(0, 3, 6, 3) };
                box.Click += (s, e) => StartCapture(id);
                boxes[id] = box;
                grid.Controls.Add(box);
                var reset = new FlatButton("기본값", FlatButton.Kind.Ghost, 60) { Margin = new Padding(0, 3, 0, 3) };
                reset.Click += (s, e) => { capturing = null; keys[id] = a.Default; Refresh2(); };
                grid.Controls.Add(reset);
            }

            var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 40, ColumnCount = 4, BackColor = Theme.Back };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 3; i++) bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var all = new FlatButton("모두 기본값", FlatButton.Kind.Ghost) { Margin = new Padding(6, 6, 0, 0) };
            all.Click += (s, e) => { capturing = null; foreach (var a in Hotkeys.All) keys[a.Id] = a.Default; Refresh2(); };
            btnSave = new FlatButton("저장", FlatButton.Kind.Primary, 80) { Margin = new Padding(6, 6, 0, 0) };
            btnSave.Click += (s, e) => Save();
            var cancel = new FlatButton("취소", FlatButton.Kind.Normal, 80) { Margin = new Padding(6, 6, 0, 0), DialogResult = DialogResult.Cancel };
            bottom.Controls.Add(lbStatus, 0, 0);
            bottom.Controls.Add(all, 1, 0);
            bottom.Controls.Add(btnSave, 2, 0);
            bottom.Controls.Add(cancel, 3, 0);

            var col = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Back };
            col.Controls.Add(head);
            col.Controls.Add(grid);
            Controls.Add(col);
            Controls.Add(bottom);
            ClientSize = new Size(560, 30 + 36 * Hotkeys.All.Length + 40 + 28);
            Refresh2();
        }

        void StartCapture(string id)
        {
            capturing = id;
            Refresh2();
        }

        // 키 칸 글자·중복 표시·저장 가능 여부
        void Refresh2()
        {
            var dup = keys.GroupBy(kv => kv.Value).Where(g => g.Count() > 1).SelectMany(g => g.Select(kv => kv.Key)).ToHashSet();
            foreach (var a in Hotkeys.All)
            {
                var b = boxes[a.Id];
                b.Text = capturing == a.Id ? "키를 누르세요…" : Hotkeys.Text(keys[a.Id]);
                b.Style = capturing == a.Id ? FlatButton.Kind.Primary : dup.Contains(a.Id) ? FlatButton.Kind.Danger : FlatButton.Kind.Normal;
            }
            if (dup.Count > 0)
            {
                lbStatus.ForeColor = Color.FromArgb(240, 120, 110);
                lbStatus.Text = "✖ 같은 키가 두 기능에 있습니다.";
            }
            else if (capturing == null)
            {
                lbStatus.ForeColor = Theme.Dim;
                lbStatus.Text = "";
            }
            btnSave.Enabled = dup.Count == 0;
        }

        // 키 입력 받기: ProcessCmdKey 는 수정키가 붙은 조합과 방향키·Tab 까지 받는다
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (capturing == null) return base.ProcessCmdKey(ref msg, keyData);
            var key = keyData & Keys.KeyCode;
            if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu) return true;   // 수정키만 누른 상태 → 다음 키 기다림
            if (keyData == Keys.Escape) { capturing = null; Refresh2(); return true; }
            if (!Hotkeys.Valid(keyData))
            {
                lbStatus.ForeColor = Color.FromArgb(240, 120, 110);
                lbStatus.Text = $"✖ {Hotkeys.Text(keyData)} — Ctrl 또는 Alt 와 같이 눌러 주세요.";
                return true;
            }
            keys[capturing] = keyData;
            capturing = null;
            Refresh2();
            return true;
        }

        void Save()
        {
            // 다른 프로그램이 이미 쓰는 조합인지 미리 등록해 본다 (실행 중인 Siosa 오버레이 단축키는 설정창을 연 동안 풀려 있다)
            var taken = new List<string>();
            foreach (var a in Hotkeys.All)
            {
                var k = keys[a.Id];
                if (Native.RegisterHotKey(Handle, 0x7000 + a.HotkeyId, Hotkeys.Modifiers(k) | Native.MOD_NOREPEAT, (uint)(k & Keys.KeyCode)))
                    Native.UnregisterHotKey(Handle, 0x7000 + a.HotkeyId);
                else taken.Add($"{a.Name} {Hotkeys.Text(k)}");
            }
            if (taken.Count > 0 &&
                MessageBox.Show(this, "다른 프로그램이 이미 쓰고 있는 단축키가 있습니다:\n" + string.Join("\n", taken) + "\n\n그래도 저장할까요? (이 키는 동작하지 않을 수 있습니다)",
                    "Siosa", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            settings.hotkeys = Hotkeys.All.Where(a => keys[a.Id] != a.Default).ToDictionary(a => a.Id, a => (int)keys[a.Id]);
            DialogResult = DialogResult.OK;
            Close();
        }

        // 개발용(--tabshot, --hotkeytest): 한 칸을 바꾸는 중인 화면 / 키 입력 흉내
        public void DevCapture(string id) => StartCapture(id);
        public void DevPress(Keys k) { var m = new Message(); ProcessCmdKey(ref m, k); }
        public string DevState() => string.Join(" ", Hotkeys.All.Select(a => $"{a.Id}={boxes[a.Id].Text}")) + $" | 저장 {(btnSave.Enabled ? "가능" : "불가")} | {lbStatus.Text}";
        public void DevSave() => Save();
    }
}
