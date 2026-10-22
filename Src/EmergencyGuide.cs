using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace DriverSetup
{
    /// <summary>
    /// 应急驱动指引：查不到机型（非联想 / 新机型未录入 / 序列号查无）时，
    /// 告诉用户去哪拿通用驱动，以及哪些东西不能通用替换。
    ///
    /// 设计取舍：不内置任何驱动包。通用驱动动辄几十上百 MB，
    /// 塞进程序既臃肿又会过期，直接跳官方入口更稳。
    /// </summary>
    public static class EmergencyGuide
    {
        private static readonly Color Paper = Theme.Bg;
        private static readonly Color Paper2 = Theme.Card;
        private static readonly Color Ink = Theme.Text;
        private static readonly Color Ink2 = Theme.Text2;
        private static readonly Color Ink3 = Theme.Text3;
        private static readonly Color Line = Theme.Line;
        private static readonly Color Accent = Theme.Accent;

        private class Item
        {
            public string Step;      // "①" 之类
            public string Title;
            public string Desc;
            public string Url;
            public string Button;
        }

        private static Item[] Items()
        {
            return new Item[]
            {
                new Item
                {
                    Step = "①",
                    Title = "芯片组 / 核显 / 网卡",
                    Desc = "Intel Driver & Support Assistant 会自动识别本机芯片并推荐对应驱动。\r\n" +
                           "装完这三类，绝大多数「未知设备」都能消掉。",
                    Url = "https://www.intel.cn/content/www/cn/zh/support/intel-driver-support-assistant.html",
                    Button = "打开 Intel DSA"
                },
                new Item
                {
                    Step = "②",
                    Title = "独立显卡",
                    Desc = "产品类型选「GeForce RTX 40 Series (Notebooks)」，再按实际型号选。\r\n" +
                           "NVIDIA 全球统一发版，笔记本 GPU 也在里面，通用性最好。",
                    Url = "https://www.nvidia.cn/Download/index.aspx",
                    Button = "打开 NVIDIA 下载页"
                },
                new Item
                {
                    Step = "③",
                    Title = "剩下的回原厂官网",
                    Desc = "音频、触摸板、Fn 键、电源管理、BIOS 只能从 OEM 拿。\r\n" +
                           "通用版装得上，但会丢 OEM 定制功能（音效、色域、省电策略）。",
                    Url = _vendorPage,
                    Button = "打开" + _vendorName + "驱动页"
                }
            };
        }

        // 第三条卡片按当前厂商动态生成（联想机型给联想驱动页，其他厂商给对应官网）
        private static string _vendorName = "联想";
        private static string _vendorPage = "https://newsupport.lenovo.com.cn/driveDownloads_index.html";

        /// <summary>指定第三条卡片指向的厂商官网；不传则默认联想</summary>
        public static void SetVendor(string displayName, string officialPage)
        {
            if (!string.IsNullOrEmpty(displayName)) _vendorName = displayName;
            if (!string.IsNullOrEmpty(officialPage)) _vendorPage = officialPage;
        }

        /// <summary>构建指引面板（可直接塞进任意容器）</summary>
        public static FlowLayoutPanel Build()
        {
            var host = new FlowLayoutPanel();
            host.FlowDirection = FlowDirection.TopDown;
            host.WrapContents = false;
            host.AutoScroll = false;
            host.AutoSize = true;
            host.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            host.BackColor = Color.Transparent;
            host.Padding = new Padding(0);

            // 顶部说明
            var head = new Label();
            head.Text = "查不到机型？用通用驱动先把机器跑起来";
            head.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            head.ForeColor = Ink;
            head.AutoSize = true;
            head.Margin = new Padding(2, 4, 0, 2);
            host.Controls.Add(head);

            var sub = new Label();
            sub.Text = "驱动分两层：芯片层（Intel / NVIDIA / Realtek）认芯片，OEM 层认机型。\r\n" +
                       "通用驱动只覆盖芯片层——任何用这颗芯片的机器都能装，但 OEM 的 Fn 键、\r\n" +
                       "音效面板、色域切换会丢失。应急够用，长期还是回原厂下定制版。";
            sub.Font = new Font("Microsoft YaHei UI", 8.5F);
            sub.ForeColor = Ink2;
            sub.AutoSize = true;
            sub.Margin = new Padding(2, 0, 0, 10);
            host.Controls.Add(sub);

            foreach (var it in Items())
                host.Controls.Add(BuildCard(it));

            // 底部红线
            var warn = new Label();
            warn.Text = "⚠ 这几样绝对不能通用替换：\r\n" +
                        "    BIOS / EC / ME 固件（刷错开不了机）· 键盘 Fn 键 · 触摸板 · 电源管理（DPTF）";
            warn.Font = new Font("Microsoft YaHei UI", 8.5F);
            warn.ForeColor = Theme.Red;
            warn.AutoSize = true;
            warn.Margin = new Padding(2, 12, 0, 4);
            host.Controls.Add(warn);

            return host;
        }

        private static Control BuildCard(Item it)
        {
            var card = new RoundPanel();
            card.Radius = 12;
            card.FillColor = Paper2;
            card.BorderColor = Color.FromArgb(40, 150, 130, 170);
            card.Size = new Size(600, 108);
            card.Margin = new Padding(2, 0, 0, 10);

            var step = new Label();
            step.Text = it.Step;
            step.Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold);
            step.ForeColor = Accent;
            step.AutoSize = true;
            step.Location = new Point(16, 14);
            card.Controls.Add(step);

            var title = new Label();
            title.Text = it.Title;
            title.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            title.ForeColor = Ink;
            title.AutoSize = true;
            title.Location = new Point(46, 18);
            card.Controls.Add(title);

            var desc = new Label();
            desc.Text = it.Desc;
            desc.Font = new Font("Microsoft YaHei UI", 8F);
            desc.ForeColor = Ink2;
            desc.AutoSize = false;
            desc.Size = new Size(420, 40);
            desc.Location = new Point(46, 42);
            card.Controls.Add(desc);

            var btn = new Button();
            btn.Text = it.Button;
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderColor = Line;
            btn.BackColor = Paper;
            btn.ForeColor = Ink;
            btn.Font = new Font("Microsoft YaHei UI", 8F);
            btn.Size = new Size(126, 28);
            btn.Location = new Point(464, 46);
            btn.Cursor = Cursors.Hand;
            string url = it.Url;
            btn.Click += delegate { OpenUrl(url); };
            card.Controls.Add(btn);

            return card;
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Swallow(ex, "EmergencyGuide.OpenUrl");
                MessageBox.Show("打开浏览器失败，请手动访问：\r\n" + url, "打开失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        /// <summary>作为独立窗口弹出（侧栏按钮用）</summary>
        public static void ShowDialog(IWin32Window owner)
        {
            using (var f = new Form())
            {
                f.Text = "应急驱动指引";
                f.StartPosition = FormStartPosition.CenterParent;
                f.Size = new Size(684, 620);
                f.MinimumSize = new Size(664, 460);
                f.BackColor = Paper;
                f.Font = new Font("Microsoft YaHei UI", 9F);
                f.MaximizeBox = false;
                f.ShowIcon = false;
                if (Program.AppIcon != null) f.Icon = Program.AppIcon;

                var body = new Panel();
                body.Dock = DockStyle.Fill;
                body.Padding = new Padding(20);
                body.BackColor = Paper;
                var guide = Build();
                guide.Dock = DockStyle.Fill;
                // Build() 里是 AutoSize=GrowAndShrink：面板会始终等于内容尺寸，
                // AutoScroll 永远不会触发，内容超高时底部那行红线警告直接被裁掉。
                // 这里必须关掉自适应，让 Dock + AutoScroll 真正生效。
                guide.AutoSize = false;
                guide.AutoScroll = true;
                body.Controls.Add(guide);
                f.Controls.Add(body);

                var close = new Button();
                close.Text = "知道了";
                close.FlatStyle = FlatStyle.Flat;
                close.FlatAppearance.BorderColor = Line;
                close.BackColor = Paper2;
                close.ForeColor = Ink;
                close.Font = new Font("Microsoft YaHei UI", 8.5F);
                close.Size = new Size(96, 30);
                close.DialogResult = DialogResult.OK;
                close.Location = new Point(20, 12);   // 兜底位置
                close.Click += delegate { f.Close(); };

                var bar = new Panel();
                bar.Dock = DockStyle.Bottom;
                bar.Height = 52;
                bar.BackColor = Paper;
                bar.Controls.Add(close);

                Action placeClose = delegate
                {
                    if (bar.Width < 220) return;
                    close.Location = new Point(bar.Width - close.Width - 20, 12);
                };
                bar.Resize += delegate { placeClose(); };
                bar.HandleCreated += delegate { placeClose(); };
                f.Controls.Add(bar);
                f.AcceptButton = close;
                f.CancelButton = close;

                f.ShowDialog(owner);
            }
        }
    }
}
