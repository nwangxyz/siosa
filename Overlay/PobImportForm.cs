using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Siosa
{
    // PoB 코드 또는 링크(pobb.in / pastebin / poe.ninja) 입력 창.
    // 불러오기를 누르면 (링크면 받아서) 해석하고 퀘스트 보상 자동 선택 결과를 만든다.
    public class PobImportForm : Form
    {
        readonly GameData data;
        readonly TextBox tbCode = new TextBox
        {
            // 기본 최대 길이(32767자)면 긴 PoB 코드(모발리틱스 등 5만 자)가 붙여넣을 때 잘린다
            Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, MaxLength = 0,
            BackColor = Theme.Panel, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9f),
        };
        readonly Label lbStatus = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Dim };
        readonly FlatButton btnOk;
        readonly Label head;
        // 2단계: 쓸 스킬 세트 고르기 (세트가 여러 개일 때)
        readonly CheckedListBox lbSets = new CheckedListBox
        {
            Dock = DockStyle.Fill, CheckOnClick = true, Visible = false,
            BackColor = Theme.Panel, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle,
        };
        PobBuild build;

        public Pob.Selection Result { get; private set; }

        public PobImportForm(GameData data)
        {
            this.data = data;
            Text = "PoB 불러오기";
            Icon = Ui.AppIcon(16);
            Size = new Size(560, 300);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            Padding = new Padding(10);

            head = new Label
            {
                Text = "PoB 코드나 링크(pobb.in, pastebin, poe.ninja)를 붙여넣으세요.\n클래스와 젬만 읽어서 퀘스트 젬 보상을 자동으로 고릅니다. (지금 고른 젬 보상은 바뀝니다)",
                Dock = DockStyle.Top, Height = 42,
            };

            var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 40, ColumnCount = 3 };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            btnOk = MakeButton("불러오기", true);
            btnOk.Click += async (s, e) => { if (build == null) await Import(); else ApplySets(); };
            var btnCancel = MakeButton("취소", false);
            btnCancel.Click += (s, e) => Close();
            bottom.Controls.Add(lbStatus, 0, 0);
            bottom.Controls.Add(btnOk, 1, 0);
            bottom.Controls.Add(btnCancel, 2, 0);

            Controls.Add(tbCode);
            Controls.Add(lbSets);
            Controls.Add(head);
            Controls.Add(bottom);
            CancelButton = btnCancel;
        }

        static FlatButton MakeButton(string text, bool primary) =>
            new FlatButton(text, primary ? FlatButton.Kind.Primary : FlatButton.Kind.Normal, 90) { Margin = new Padding(6, 4, 0, 0) };

        void ApplySets()
        {
            var sets = lbSets.CheckedIndices.Cast<int>().ToList();
            if (sets.Count == 0)
            {
                lbStatus.ForeColor = Color.FromArgb(240, 120, 110);
                lbStatus.Text = "✖ 세트를 하나 이상 고르세요.";
                return;
            }
            Result = Pob.Select(data, build, sets);
            DialogResult = DialogResult.OK;
            Close();
        }

        async Task Import()
        {
            var input = tbCode.Text.Trim();
            if (input.Length == 0) return;
            bool link = Pob.IsLink(input);
            btnOk.Enabled = false;
            lbStatus.ForeColor = Theme.Dim;
            lbStatus.Text = link ? "링크에서 받는 중…" : "읽는 중…";
            try
            {
                // 링크 받기는 최대 10초. 설정창이 멈추지 않도록 백그라운드에서
                var b = await Task.Run(() => Pob.Parse(Pob.Resolve(input)));
                if (b.Sets.Count <= 1)
                {
                    Result = Pob.Select(data, b);
                    DialogResult = DialogResult.OK;
                    Close();
                    return;
                }
                // 스킬 세트가 여러 개면 쓸 세트를 고른다 (레벨링으로 보이는 세트를 미리 체크)
                build = b;
                head.Text = $"{b.ClassName} ({b.Ascendancy}) · 쓸 스킬 세트를 고르세요.\n체크한 세트의 젬으로 퀘스트 보상과 구매 목록을 만듭니다. (맵·엔드게임 세트는 보통 빼기)";
                for (int i = 0; i < b.Sets.Count; i++)
                    lbSets.Items.Add($"{b.Sets[i].Title}   (젬 {b.Sets[i].GemCount}개)", b.Sets[i].Leveling);
                tbCode.Visible = false;
                lbSets.Visible = true;
                btnOk.Text = "적용";
                btnOk.Enabled = true;
                lbStatus.Text = "";
            }
            catch (Exception ex)
            {
                lbStatus.ForeColor = Color.FromArgb(240, 120, 110);
                lbStatus.Text = link && (ex is System.Net.WebException || ex is System.IO.IOException)
                    ? "✖ 링크를 열 수 없습니다. 코드를 직접 붙여넣어 주세요."
                    : "✖ " + ex.Message;
                btnOk.Enabled = true;
            }
        }
    }
}
