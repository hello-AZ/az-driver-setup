using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace DriverSetup
{
    /// <summary>删除驱动前的三关认证</summary>
    public class DeleteConfirmForm : Form
    {
        private static readonly Color Paper = Theme.Bg;
        private static readonly Color Paper2 = Theme.Card;
        private static readonly Color Ink = Theme.Text;
        private static readonly Color Ink2 = Theme.Text2;
        private static readonly Color Ink3 = Theme.Text3;
        private static readonly Color Line = Theme.Line;
        private static readonly Color Red = Theme.Red;

        private readonly int _count;
        private CheckBox _chkRisk;
        private TextBox _txtNum;
        private TextBox _txtPhrase;
        private Button _btnOk;

        public DeleteConfirmForm(List<string> items)
        {
            _count = items == null ? 0 : items.Count;

            if (Program.AppIcon != null) Icon = Program.AppIcon;
            Text = "确认删除驱动";
            ClientSize = new Size(560, 520);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Paper;
            Font = new Font("Microsoft YaHei UI", 9F);

            var title = new Label();
            title.Text = "即将删除 " + _count + " 个驱动包";
            title.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
            title.ForeColor = Red;
            title.AutoSize = true;
            title.Location = new Point(24, 18);
            Controls.Add(title);

            var listBox = new TextBox();
            listBox.Multiline = true;
            listBox.ReadOnly = true;
            listBox.ScrollBars = ScrollBars.Vertical;
            listBox.Location = new Point(24, 52);
            listBox.Size = new Size(512, 116);
            listBox.BackColor = Paper2;
            listBox.ForeColor = Ink2;
            listBox.BorderStyle = BorderStyle.FixedSingle;
            listBox.Font = new Font("Consolas", 8.5F);
            listBox.Text = string.Join("\r\n", items);
            Controls.Add(listBox);

            var warn = new Label();
            warn.Text = "后果：\r\n" +
                        "· 正在使用这些驱动的设备会暂时失效（分辨率骤降、网卡消失等）\r\n" +
                        "· 重启后 Windows 可能自动装回旧版\r\n" +
                        "· 删掉存储控制器 / 芯片组驱动可能导致无法开机";
            warn.Font = new Font("Microsoft YaHei UI", 8.5F);
            warn.ForeColor = Ink2;
            warn.AutoSize = false;
            warn.Size = new Size(512, 74);
            warn.Location = new Point(24, 180);
            Controls.Add(warn);

            var sep = new Panel();
            sep.Location = new Point(24, 258);
            sep.Size = new Size(512, 1);
            sep.BackColor = Line;
            Controls.Add(sep);

            // ---------- 第一关 ----------
            _chkRisk = new CheckBox();
            _chkRisk.Text = "第一关：我已了解上述风险，并已备份或不需要备份";
            _chkRisk.Font = new Font("Microsoft YaHei UI", 9F);
            _chkRisk.ForeColor = Ink;
            _chkRisk.AutoSize = true;
            _chkRisk.Location = new Point(24, 274);
            _chkRisk.CheckedChanged += delegate { Validate(); };
            Controls.Add(_chkRisk);

            // ---------- 第二关 ----------
            var l2 = new Label();
            l2.Text = "第二关：输入要删除的数量（" + _count + "）";
            l2.Font = new Font("Microsoft YaHei UI", 9F);
            l2.ForeColor = Ink;
            l2.AutoSize = true;
            l2.Location = new Point(24, 312);
            Controls.Add(l2);

            _txtNum = new TextBox();
            _txtNum.Size = new Size(80, 26);
            _txtNum.Location = new Point(24, 336);
            _txtNum.Font = new Font("Consolas", 10F);
            _txtNum.BackColor = Paper2;
            _txtNum.BorderStyle = BorderStyle.FixedSingle;
            _txtNum.TextChanged += delegate { Validate(); };
            Controls.Add(_txtNum);

            // ---------- 第三关 ----------
            var l3 = new Label();
            l3.Text = "第三关：输入「确认删除」四个字";
            l3.Font = new Font("Microsoft YaHei UI", 9F);
            l3.ForeColor = Ink;
            l3.AutoSize = true;
            l3.Location = new Point(24, 374);
            Controls.Add(l3);

            _txtPhrase = new TextBox();
            _txtPhrase.Size = new Size(200, 26);
            _txtPhrase.Location = new Point(24, 398);
            _txtPhrase.Font = new Font("Microsoft YaHei UI", 10F);
            _txtPhrase.BackColor = Paper2;
            _txtPhrase.BorderStyle = BorderStyle.FixedSingle;
            _txtPhrase.TextChanged += delegate { Validate(); };
            Controls.Add(_txtPhrase);

            // ---------- 按钮 ----------
            var btnCancel = new Button();
            btnCancel.Text = "取消";
            btnCancel.Size = new Size(110, 38);
            btnCancel.FlatStyle = FlatStyle.Flat;
            btnCancel.FlatAppearance.BorderColor = Line;
            btnCancel.BackColor = Paper2;
            btnCancel.ForeColor = Ink2;
            btnCancel.Location = new Point(24, 452);
            btnCancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(btnCancel);

            _btnOk = new Button();
            _btnOk.Text = "删除";
            _btnOk.Size = new Size(130, 38);
            _btnOk.FlatStyle = FlatStyle.Flat;
            _btnOk.FlatAppearance.BorderSize = 0;
            _btnOk.BackColor = Color.FromArgb(210, 210, 210);
            _btnOk.ForeColor = Color.FromArgb(140, 140, 140);
            _btnOk.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            _btnOk.Enabled = false;
            _btnOk.Location = new Point(406, 452);
            _btnOk.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            Controls.Add(_btnOk);

            Validate();
        }

        private void Validate()
        {
            bool ok = _chkRisk.Checked;

            if (ok)
            {
                string n = _txtNum.Text.Trim();
                ok = n == _count.ToString();
            }
            if (ok)
            {
                ok = _txtPhrase.Text.Trim() == "确认删除";
            }

            if (_btnOk != null)
            {
                _btnOk.Enabled = ok;
                _btnOk.BackColor = ok ? Red : Color.FromArgb(210, 210, 210);
                _btnOk.ForeColor = ok ? Color.White : Color.FromArgb(140, 140, 140);
            }
        }
    }
}
