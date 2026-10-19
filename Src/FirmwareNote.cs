using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace DriverSetup
{
    /// <summary>固件说明窗口：只给信息与官网链接，不提供下载/执行</summary>
    public class FirmwareNoteForm : Form
    {
        private static readonly Color Paper = Theme.Bg;
        private static readonly Color Paper2 = Theme.Card;
        private static readonly Color Ink = Theme.Text;
        private static readonly Color Ink2 = Theme.Text2;
        private static readonly Color Ink3 = Theme.Text3;
        private static readonly Color Line = Theme.Line;
        private static readonly Color Red = Theme.Red;
        private static readonly Color Orange = Theme.Orange;

        public FirmwareNoteForm(CompareResult r)
        {
            if (Program.AppIcon != null) Icon = Program.AppIcon;
            Text = "固件说明 · " + r.Driver.DriverName;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(540, 470);
            BackColor = Paper;
            Font = new Font("Microsoft YaHei UI", 9F);

            var title = new Label();
            string pn = r.Driver.PartName;
            title.Text = pn.Contains("固件") ? pn : pn + " 固件";
            title.Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold);
            title.ForeColor = Ink;
            title.AutoSize = true;
            title.Location = new Point(22, 18);

            var ver = new Label();
            ver.Text = "本机  " + (string.IsNullOrEmpty(r.LocalVersion) ? "未知" : r.LocalVersion) +
                       "      →      官网  " + r.Driver.Version;
            ver.Font = new Font("Consolas", 10F);
            ver.ForeColor = r.StatusRank == 1 ? Red : Ink2;
            ver.AutoSize = true;
            ver.Location = new Point(22, 52);

            var warn = new Panel();
            warn.Location = new Point(22, 84);
            warn.Size = new Size(496, 44);
            warn.BackColor = Color.FromArgb(253, 246, 236);
            var wt = new Label();
            wt.Text = "⚠  刷写过程断电 = 主板变砖，只能返厂。本工具不下载、不执行。";
            wt.Dock = DockStyle.Fill;
            wt.TextAlign = ContentAlignment.MiddleLeft;
            wt.Padding = new Padding(10, 0, 0, 0);
            wt.Font = new Font("Microsoft YaHei UI", 8.5F);
            wt.ForeColor = Orange;
            warn.Controls.Add(wt);

            var body = new TextBox();
            body.Multiline = true;
            body.ReadOnly = true;
            body.ScrollBars = ScrollBars.Vertical;
            body.BorderStyle = BorderStyle.None;
            body.BackColor = Paper;
            body.ForeColor = Ink2;
            body.Font = new Font("Microsoft YaHei UI", 9F);
            body.Location = new Point(22, 140);
            body.Size = new Size(496, 258);
            body.Text =
                "这是什么\r\n" +
                "  BIOS 是主板的底层固件，负责开机自检、硬件初始化、风扇曲线与功耗墙；\r\n" +
                "  ME 是主板上的独立小处理器固件，管电源管理与安全启动链。\r\n" +
                "\r\n" +
                "更新通常带来\r\n" +
                "  · CPU 微码修正、内存兼容性改进\r\n" +
                "  · 风扇策略与温度曲线调整\r\n" +
                "  · 安全漏洞修补\r\n" +
                "\r\n" +
                "什么时候值得刷\r\n" +
                "  遇到明确的兼容性问题（如换了内存不认、睡眠异常、风扇狂转）才刷。\r\n" +
                "  「官网有新版」本身不是刷机理由——稳定比新更重要。\r\n" +
                "\r\n" +
                "真要刷的话\r\n" +
                "  1. 从联想官网下载，核对型号与版本号\r\n" +
                "  2. 插上电源，电池电量高于 50%\r\n" +
                "  3. 全程不关机、不待机、不断电\r\n" +
                "  4. 刷完会自动重启，第一次开机会比平时慢";

            var btnSite = new Button();
            btnSite.Text = "打开联想驱动下载页";
            btnSite.Size = new Size(168, 34);
            btnSite.Location = new Point(22, 418);
            btnSite.FlatStyle = FlatStyle.Flat;
            btnSite.FlatAppearance.BorderColor = Line;
            btnSite.BackColor = Paper2;
            btnSite.ForeColor = Ink2;
            btnSite.Click += delegate
            {
                try { Process.Start("https://newsupport.lenovo.com.cn/driveDownloads_index.html"); }
                catch (Exception) { }
            };

            var btnClose = new Button();
            btnClose.Text = "关闭";
            btnClose.Size = new Size(96, 34);
            btnClose.Location = new Point(422, 418);
            btnClose.DialogResult = DialogResult.OK;
            btnClose.FlatStyle = FlatStyle.Flat;
            btnClose.FlatAppearance.BorderColor = Line;
            btnClose.BackColor = Paper2;
            btnClose.ForeColor = Ink;
            btnClose.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);

            Controls.Add(title);
            Controls.Add(ver);
            Controls.Add(warn);
            Controls.Add(body);
            Controls.Add(btnSite);
            Controls.Add(btnClose);
            AcceptButton = btnClose;
            CancelButton = btnClose;
        }
    }

    /// <summary>选择要检测的显示器</summary>
    public class ScreenPickForm : Form
    {
        private static readonly Color Paper = Theme.Bg;
        private static readonly Color Paper2 = Theme.Card;
        private static readonly Color Ink = Theme.Text;
        private static readonly Color Ink3 = Theme.Text3;
        private static readonly Color Line = Theme.Line;

        public Screen Selected;

        public ScreenPickForm()
        {
            Text = "选择显示器";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(440, 300);
            BackColor = Paper;
            Font = new Font("Microsoft YaHei UI", 9F);

            var title = new Label();
            title.Text = "在哪块屏幕上检测坏点？";
            title.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
            title.ForeColor = Ink;
            title.AutoSize = true;
            title.Location = new Point(20, 16);

            var hint = new Label();
            hint.Text = "选中后该屏会全屏显示纯色，按任意键换色、Esc 退出。";
            hint.Font = new Font("Microsoft YaHei UI", 8F);
            hint.ForeColor = Ink3;
            hint.AutoSize = true;
            hint.Location = new Point(22, 44);

            var list = new ListBox();
            list.Location = new Point(22, 70);
            list.Size = new Size(396, 170);
            list.Font = new Font("Microsoft YaHei UI", 9.5F);
            list.BorderStyle = BorderStyle.FixedSingle;
            list.BackColor = Paper2;

            var screens = Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                var s = screens[i];
                string text = string.Format("{0}　{1} × {2}{3}",
                    s.Primary ? "主显示器" : "显示器 " + (i + 1),
                    s.Bounds.Width, s.Bounds.Height,
                    s.Primary ? "　（主）" : "");
                list.Items.Add(text);
            }
            if (list.Items.Count > 0) list.SelectedIndex = 0;

            var ok = new Button();
            ok.Text = "开始检测";
            ok.Size = new Size(112, 34);
            ok.Location = new Point(222, 252);
            ok.FlatStyle = FlatStyle.Flat;
            ok.FlatAppearance.BorderColor = Line;
            ok.BackColor = Paper2;
            ok.ForeColor = Ink;
            ok.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            ok.DialogResult = DialogResult.OK;

            var cancel = new Button();
            cancel.Text = "取消";
            cancel.Size = new Size(84, 34);
            cancel.Location = new Point(344, 252);
            cancel.FlatStyle = FlatStyle.Flat;
            cancel.FlatAppearance.BorderColor = Line;
            cancel.BackColor = Paper2;
            cancel.ForeColor = Ink3;
            cancel.DialogResult = DialogResult.Cancel;

            Controls.Add(title);
            Controls.Add(hint);
            Controls.Add(list);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;

            ok.Click += delegate
            {
                int idx = list.SelectedIndex;
                if (idx >= 0 && idx < screens.Length) Selected = screens[idx];
            };
        }
    }
}
