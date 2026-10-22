using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace DriverSetup
{
    /// <summary>
    /// 关于 / 卸载。
    /// 安装版的卸载程序是 Inno Setup 生成的 unins000.exe，藏在开始菜单文件夹里，
    /// 用户找不着——所以在这里给一个直通入口。
    /// </summary>
    public static class AboutForm
    {
        private static readonly Color Paper = Theme.Bg;
        private static readonly Color Paper2 = Theme.Card;
        private static readonly Color Ink = Theme.Text;
        private static readonly Color Ink2 = Theme.Text2;
        private static readonly Color Ink3 = Theme.Text3;
        private static readonly Color Line = Theme.Line;
        private static readonly Color Accent = Theme.Accent;

        private static string BaseDir
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        /// <summary>安装版才有卸载程序；便携版是绿色目录，没有</summary>
        private static string UninstallerPath
        {
            get
            {
                string p = Path.Combine(BaseDir, "unins000.exe");
                return File.Exists(p) ? p : null;
            }
        }

        public static string VersionText
        {
            get
            {
                try
                {
                    var v = Assembly.GetExecutingAssembly().GetName().Version;
                    return v.Major + "." + v.Minor + "." + v.Build + "." + v.Revision;
                }
                catch { return "未知"; }
            }
        }

        public static void Show(IWin32Window owner)
        {
            using (var f = new Form())
            {
                f.Text = "关于 AZ 驱动懒人安装";
                f.StartPosition = FormStartPosition.CenterParent;
                f.Size = new Size(560, 460);
                f.MinimumSize = new Size(520, 420);
                f.BackColor = Paper;
                f.Font = new Font("Microsoft YaHei UI", 9F);
                f.MaximizeBox = false;
                f.MinimizeBox = false;
                f.ShowIcon = false;
                if (Program.AppIcon != null) f.Icon = Program.AppIcon;

                var body = new Panel();
                body.Dock = DockStyle.Fill;
                body.Padding = new Padding(24, 20, 24, 8);
                body.BackColor = Paper;
                body.AutoScroll = true;

                int y = 6;

                var title = new Label();
                title.Text = "AZ 驱动懒人安装";
                title.Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold);
                title.ForeColor = Ink;
                title.AutoSize = true;
                title.Location = new Point(24, y);
                body.Controls.Add(title);
                y += 36;

                var ver = new Label();
                ver.Text = "版本 " + VersionText + "   ·   非联想官方工具，按 MIT 协议开源";
                ver.Font = new Font("Microsoft YaHei UI", 8.5F);
                ver.ForeColor = Ink3;
                ver.AutoSize = true;
                ver.Location = new Point(24, y);
                body.Controls.Add(ver);
                y += 30;

                y = AddBlock(body, y, "数据目录", BaseDir, Ink2);

                string unins = UninstallerPath;
                if (unins != null)
                {
                    y = AddBlock(body, y, "安装方式", "安装版（写入 Program Files，含卸载程序）", Ink2);

                    var tip = new Label();
                    tip.Text = "卸载会同时删除 Downloads 与 Backup 目录，建议先把需要的备份拷走。";
                    tip.Font = new Font("Microsoft YaHei UI", 8F);
                    tip.ForeColor = Theme.Orange;
                    tip.AutoSize = false;
                    tip.Size = new Size(470, 34);
                    tip.Location = new Point(24, y + 6);
                    body.Controls.Add(tip);
                    y += 46;
                }
                else
                {
                    y = AddBlock(body, y, "安装方式",
                        "便携版（绿色目录）——卸载方法：直接删除整个文件夹即可", Ink2);
                }

                var warn = new Label();
                warn.Text = "BIOS / EC / ME 固件永不自动执行；驱动清理只允许删除本工具安装并登记过的驱动。\r\n" +
                            "驱动安装包全部来自联想官网，本程序不打包、不分发任何驱动文件。";
                warn.Font = new Font("Microsoft YaHei UI", 8F);
                warn.ForeColor = Ink3;
                warn.AutoSize = false;
                warn.Size = new Size(470, 42);
                warn.Location = new Point(24, y + 10);
                body.Controls.Add(warn);

                f.Controls.Add(body);

                // ---- 底部按钮区 ----
                var bar = new Panel();
                bar.Dock = DockStyle.Bottom;
                bar.Height = 56;
                bar.BackColor = Paper;

                var close = new Button();
                close.Text = "关闭";
                close.FlatStyle = FlatStyle.Flat;
                close.FlatAppearance.BorderColor = Line;
                close.BackColor = Paper2;
                close.ForeColor = Ink;
                close.Font = new Font("Microsoft YaHei UI", 8.5F);
                close.Size = new Size(96, 32);
                close.DialogResult = DialogResult.OK;

                bar.Controls.Add(close);

                if (unins != null)
                {
                    var uninstall = new Button();
                    uninstall.Text = "卸载本程序";
                    uninstall.FlatStyle = FlatStyle.Flat;
                    uninstall.FlatAppearance.BorderColor = Theme.Red;
                    uninstall.BackColor = Paper2;
                    uninstall.ForeColor = Theme.Red;
                    uninstall.Font = new Font("Microsoft YaHei UI", 8.5F);
                    uninstall.Size = new Size(120, 32);
                    uninstall.Click += delegate
                    {
                        var r = MessageBox.Show(f,
                            "确定要卸载「AZ 驱动懒人安装」吗？\r\n\r\n" +
                            "卸载会关闭程序，并删除安装目录下的\r\n" +
                            "Downloads（驱动包）和 Backup（驱动备份）。",
                            "确认卸载", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                            MessageBoxDefaultButton.Button2);
                        if (r != DialogResult.Yes) return;

                        try
                        {
                            // 卸载程序会检测主进程是否还在运行；直接 Start 再 Exit 有竞态，
                            // 撞上就静默放弃——点了卸载什么都不会发生。改成延迟 3 秒再拉起卸载器。
                            Process.Start(new ProcessStartInfo("cmd.exe",
                                "/c ping 127.0.0.1 -n 4 >nul & \"" + unins + "\" /SILENT /NORESTART")
                            {
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                WorkingDirectory = BaseDir
                            });
                            // 卸载程序需要本进程退出才能删文件
                            Application.Exit();
                        }
                        catch (Exception ex)
                        {
                            Log.Swallow(ex, "AboutForm.Uninstall");
                            MessageBox.Show(f, "启动卸载程序失败：\r\n" + ex.Message + "\r\n\r\n" +
                                "可以手动运行：" + unins,
                                "卸载失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    };
                    bar.Controls.Add(uninstall);
                }

                bar.Resize += delegate
                {
                    if (bar.Width < 300) return;
                    close.Location = new Point(bar.Width - close.Width - 24, 12);
                    if (unins != null && bar.Controls.Count > 1)
                        bar.Controls[1].Location = new Point(bar.Width - close.Width - 24 - 132, 12);
                    else if (bar.Controls.Count > 0)
                        bar.Controls[0].Location = new Point(24, 12);
                };

                f.Controls.Add(bar);
                f.AcceptButton = close;
                f.CancelButton = close;

                f.Shown += delegate
                {
                    // 兜底：窗口显示后按真实宽度重排一次
                    close.Location = new Point(bar.Width - close.Width - 24, 12);
                    foreach (Control c in bar.Controls)
                        if (c is Button && c != close)
                            c.Location = new Point(bar.Width - close.Width - 24 - 132, 12);
                };

                f.ShowDialog(owner);
            }
        }

        private static int AddBlock(Control parent, int y, string title, string value, Color valueColor)
        {
            var t = new Label();
            t.Text = title;
            t.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold);
            t.ForeColor = Ink3;
            t.AutoSize = true;
            t.Location = new Point(24, y);
            parent.Controls.Add(t);

            var v = new Label();
            v.Text = value;
            v.Font = new Font("Microsoft YaHei UI", 8.5F);
            v.ForeColor = valueColor;
            v.AutoSize = false;
            v.Size = new Size(470, 36);
            v.Location = new Point(24, y + 20);
            parent.Controls.Add(v);

            return y + 62;
        }
    }
}
