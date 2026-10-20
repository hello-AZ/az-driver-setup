using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DriverSetup
{
    public class MainForm : Form
    {
        // ---- 纸本配色 ----
        private static readonly Color Paper = Theme.Bg;
        private static readonly Color Paper2 = Theme.Card;
        private static readonly Color Ink = Theme.Text;
        private static readonly Color Ink2 = Theme.Text2;
        private static readonly Color Ink3 = Theme.Text3;
        private static readonly Color Line = Theme.Line;
        private static readonly Color Green = Theme.Green;
        private static readonly Color Orange = Theme.Orange;
        private static readonly Color Red = Theme.Red;
        private static readonly Color Blue = Theme.Accent;
        private static readonly Color Grey = Color.FromArgb(211, 200, 182);

        private Label _pageTitle;
        private Label _pageSub;
        private Label _machineInfo;
        private FlowLayoutPanel _listPanel;
        private Panel _sideStats;
        private TextBox _logBox;
        private ToolStripProgressBar _progress;
        private ToolStripStatusLabel _status;
        private Button _btnRefresh;
        private List<CompareResult> _results = new List<CompareResult>();
        private LenovoMachine _machine;
        private List<LenovoDriver> _drivers = new List<LenovoDriver>();
        private IDriverCatalog _catalog;
        private readonly Dictionary<CompareResult, ProgressBar> _bars = new Dictionary<CompareResult, ProgressBar>();
        private readonly Dictionary<CompareResult, Label> _pcts = new Dictionary<CompareResult, Label>();
        private bool _busy;
        private Panel _navPanel;
        private Panel _contentPanel;
        private int _currentPage;
        private Panel[] _pages;
        private readonly List<Panel> _navItems = new List<Panel>();
        private ConfigPage _configPage;
        private TestPage _testPage;
        private PressurePage _pressurePage;
        private SystemPage _systemPage;
        private SplashForm _splash;
        private RawKeyboard _raw;
        private Panel _fwList;
        private Panel _bkList;

        private void Report(int pct, string step)
        {
            if (_splash != null) _splash.SetProgress(pct, step);
        }

        public MainForm(SplashForm splash = null)
        {
            _splash = splash;
            if (Program.AppIcon != null) Icon = Program.AppIcon;
            // 不用 AutoScale（net48 在 150% 下方向不可控），构造完自己按 DPI 等比缩放一次
            AutoScaleMode = AutoScaleMode.None;

            Text = "AZ 驱动懒人安装";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1400, 850);
            MinimumSize = new Size(900, 600);
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Paper;
            KeyPreview = true;   // 硬件测试页需要窗体先收到物理按键
            BuildUi();
            // Load 阶段设的尺寸会被 WinForms 内部重置，挪到 Shown（窗口显示完，没人再动）
            Load += delegate { RefreshAll(); };

            // 退出前注销全局键盘监听，否则注册会指向失效的 hwnd 并不断累积
            FormClosed += delegate
            {
                try { if (_raw != null) _raw.Unregister(); } catch (Exception ex) { Log.Swallow(ex, "MainForm.FormClosed"); }
            };
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetDpiForSystem();

        // ---------------- 物理按键（Raw Input，能拿到扫描码和设备） ----------------
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (_raw == null)
            {
                _raw = new RawKeyboard(Handle);
                _raw.KeyEvent += OnRawKey;
            }
        }

        private void OnRawKey(int vkey, int scan, bool down, string path)
        {
            if (_testPage == null || _currentPage != 4) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action(delegate { OnRawKey(vkey, scan, down, path); }));
                return;
            }
            _testPage.HandleRawKey(vkey, scan, down);
        }

        protected override void WndProc(ref Message m)
        {
            if (_raw != null && _raw.ProcessMessage(ref m)) return;
            base.WndProc(ref m);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Raw Input 已在处理按键时，这里只负责吞掉会引发焦点/菜单行为的键
            if (_raw != null && _raw.Registered && _testPage != null && _currentPage == 4)
            {
                Keys k = keyData & Keys.KeyCode;
                if (k == Keys.Tab || k == Keys.Up || k == Keys.Down || k == Keys.Left ||
                    k == Keys.Right || k == Keys.Enter || k == Keys.Escape ||
                    k == Keys.Space || k == Keys.Apps || k == Keys.Back)
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (_raw != null && _raw.Registered) return;   // Raw Input 已经接管
            if (_testPage != null && _currentPage == 4)
                _testPage.HandleRawKey((int)e.KeyCode, 0, true);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (_raw != null && _raw.Registered) return;
            if (_testPage != null && _currentPage == 4)
                _testPage.HandleRawKey((int)e.KeyCode, 0, false);
        }

        // ---------------- UI ----------------
        private void BuildUi()
        {
            var status = new StatusStrip();
            status.BackColor = Paper2;
            status.SizingGrip = false;
            _progress = new ToolStripProgressBar();
            _progress.Visible = false;
            _progress.Width = 180;
            _status = new ToolStripStatusLabel(InstallEngine.IsAdmin ? "就绪" : "只读模式（未提权）");
            status.Items.Add(_progress);
            status.Items.Add(_status);

            // 顶部
            var top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = 92;
            top.BackColor = Paper;
            top.Padding = new Padding(22, 14, 22, 10);

            var brand = new Label();
            brand.Text = "AZ · LAZY DRIVER";
            brand.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold);
            brand.ForeColor = Blue;
            brand.AutoSize = true;
            brand.Location = new Point(22, 10);

            var title = new Label();
            title.Text = "驱动懒人安装";
            _pageTitle = title;
            title.Font = new Font("Microsoft YaHei UI", 13.5F, FontStyle.Bold);
            title.ForeColor = Ink;
            title.AutoSize = true;
            title.Location = new Point(20, 30);

            var sub = new Label();
            sub.Text = "Lenovo 官网对照 · 按序列号实时查询";
            _pageSub = sub;
            sub.Font = new Font("Microsoft YaHei UI", 8F);
            sub.ForeColor = Ink3;
            sub.AutoSize = true;
            sub.Location = new Point(24, 62);

            _machineInfo = new Label();
            _machineInfo.AutoSize = false;
            _machineInfo.Size = new Size(300, 52);
            _machineInfo.TextAlign = ContentAlignment.MiddleRight;
            _machineInfo.Font = new Font("Microsoft YaHei UI", 8.2F);
            _machineInfo.ForeColor = Ink3;
            _machineInfo.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _machineInfo.Location = new Point(ClientSize.Width - 430, 20);

            _btnRefresh = new Button();
            _btnRefresh.Text = "重新检测";
            _btnRefresh.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            _btnRefresh.FlatStyle = FlatStyle.Flat;
            _btnRefresh.FlatAppearance.BorderColor = Line;
            _btnRefresh.BackColor = Paper2;
            _btnRefresh.ForeColor = Ink;
            _btnRefresh.Size = new Size(104, 34);
            _btnRefresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnRefresh.Location = new Point(ClientSize.Width - 124, 28);
            _btnRefresh.Click += delegate { RefreshAll(); };

            top.Paint += delegate(object o, PaintEventArgs e)
            {
                using (var pen = new Pen(Line))
                    e.Graphics.DrawLine(pen, 0, top.Height - 1, top.Width, top.Height - 1);
            };

            top.Controls.Add(brand);
            top.Controls.Add(title);
            top.Controls.Add(sub);
            top.Controls.Add(_machineInfo);
            top.Controls.Add(_btnRefresh);

            // 右栏
            var side = new Panel();
            side.Dock = DockStyle.Right;
            side.Width = 292;
            side.BackColor = Paper;
            side.Padding = new Padding(0, 6, 20, 6);

            _sideStats = new Panel();
            _sideStats.Dock = DockStyle.Top;
            _sideStats.Height = 262;
            _sideStats.BackColor = Paper2;
            _sideStats.BorderStyle = BorderStyle.FixedSingle;
            _sideStats.Padding = new Padding(14, 12, 14, 12);

            var logPanel = new Panel();
            logPanel.Dock = DockStyle.Fill;
            logPanel.BackColor = Paper2;
            logPanel.BorderStyle = BorderStyle.FixedSingle;
            logPanel.Padding = new Padding(10, 8, 10, 8);

            var logTitle = new Label();
            logTitle.Text = "日志";
            logTitle.Dock = DockStyle.Top;
            logTitle.Height = 22;
            logTitle.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold);
            logTitle.ForeColor = Ink3;

            _logBox = new TextBox();
            _logBox.Multiline = true;
            _logBox.ReadOnly = true;
            _logBox.ScrollBars = ScrollBars.Vertical;
            _logBox.BorderStyle = BorderStyle.None;
            _logBox.BackColor = Paper2;
            _logBox.ForeColor = Ink3;
            _logBox.Font = new Font("Consolas", 8F);
            _logBox.Dock = DockStyle.Fill;

            logPanel.Controls.Add(_logBox);
            logPanel.Controls.Add(logTitle);
            side.Controls.Add(logPanel);
            side.Controls.Add(_sideStats);

            // 左栏卡片流
            _listPanel = new FlowLayoutPanel();
            _listPanel.Dock = DockStyle.Fill;
            _listPanel.AutoScroll = true;
            _listPanel.FlowDirection = FlowDirection.TopDown;
            _listPanel.WrapContents = false;
            _listPanel.BackColor = Paper;
            _listPanel.Padding = new Padding(20, 6, 8, 10);

            // 驱动页 = 左卡片流 + 右统计日志
            Report(18, "初始化界面…");
            var driverPage = new Panel();
            driverPage.Dock = DockStyle.Fill;
            driverPage.BackColor = Paper;
            driverPage.Controls.Add(_listPanel);
            driverPage.Controls.Add(side);

            // 配置核对页
            Report(34, "载入硬件配置…");
            _configPage = new ConfigPage();
            _configPage.Dock = DockStyle.Fill;

            // 固件提示页 / 备份回滚页 / 硬件测试页
            var fwPage = BuildFirmwarePage();
            var bkPage = BuildBackupPage();
            Report(58, "载入键盘布局…");
            _testPage = new TestPage();
            _testPage.Dock = DockStyle.Fill;


            var contentHost = new RoundPanel();
            contentHost.DrawFill = false;
            contentHost.DrawBorder = false;
            contentHost.BackColor = Color.Transparent;
            _contentPanel = contentHost;
            _contentPanel.Dock = DockStyle.Fill;
            _contentPanel.BackColor = Paper;
            _contentPanel.Controls.Add(_configPage);
            _contentPanel.Controls.Add(driverPage);
            _contentPanel.Controls.Add(fwPage);
            _contentPanel.Controls.Add(bkPage);
            _contentPanel.Controls.Add(_testPage);

            Report(72, "准备压力测试…");
            _pressurePage = new PressurePage();
            _pressurePage.Dock = DockStyle.Fill;
            _contentPanel.Controls.Add(_pressurePage);

            Report(86, "准备系统排查…");
            _systemPage = new SystemPage();
            _systemPage.Dock = DockStyle.Fill;
            _contentPanel.Controls.Add(_systemPage);

            _pages = new Panel[] { _configPage, driverPage, fwPage, bkPage, _testPage, _pressurePage, _systemPage };
            Report(94, "绘制导航…");
            _navPanel = BuildNav();

            // 添加顺序：Fill 先，边后
            Controls.Add(_contentPanel);
            Controls.Add(_navPanel);
            Controls.Add(top);
            Controls.Add(status);

            int startPage = 0;
            string env = Environment.GetEnvironmentVariable("DRV_START_PAGE");
            if (!string.IsNullOrEmpty(env)) int.TryParse(env, out startPage);
            ShowPage(startPage);
        }

        // ---------------- 左导航 ----------------
        private Panel BuildNav()
        {
            var nav = new Panel();
            nav.Dock = DockStyle.Left;
            nav.Width = 184;
            nav.BackColor = Paper2;
            nav.Padding = new Padding(0, 0, 0, 10);

            string[] icons = { "\u25C8", "\u2B07", "\u2699", "\u27F2", "\u2328", "\u26A1", "\u26A0" };
            string[] names = { "配置核对", "驱动安装", "固件提示", "备份回滚", "硬件测试", "压力测试", "系统排查" };

            // 以较短的数组为准：以后少写一个图标也只是少一项，不会 IndexOutOfRange
            int navCount = Math.Min(icons.Length, names.Length);
            for (int i = navCount - 1; i >= 0; i--)
            {
                int idx = i;
                var item = new Panel();
                item.Dock = DockStyle.Top;
                item.Height = 46;
                item.BackColor = Paper2;
                item.Cursor = Cursors.Hand;
                item.Margin = new Padding(0);

                var bar = new Panel();
                bar.Dock = DockStyle.Left;
                bar.Width = 3;
                bar.BackColor = Paper2;
                bar.Name = "bar";

                // 图标与文字分成两个 Label，各自固定位置，避免不同字形宽度导致错位
                var icon = new Label();
                icon.Text = icons[i];
                icon.Font = new Font("Microsoft YaHei UI", 10F);
                icon.ForeColor = Ink3;
                icon.Location = new Point(16, 12);
                icon.Size = new Size(22, 22);
                icon.TextAlign = ContentAlignment.MiddleCenter;
                icon.Cursor = Cursors.Hand;

                var lbl = new Label();
                lbl.Text = names[i];
                lbl.Font = new Font("Microsoft YaHei UI", 9.5F);
                lbl.ForeColor = Ink2;
                lbl.Location = new Point(46, 12);
                lbl.Size = new Size(nav.Width - 52, 22);
                lbl.TextAlign = ContentAlignment.MiddleLeft;
                lbl.Cursor = Cursors.Hand;

                EventHandler click = delegate { ShowPage(idx); };
                item.Click += click;
                icon.Click += click;
                lbl.Click += click;

                item.Controls.Add(icon);
                item.Controls.Add(lbl);
                item.Controls.Add(bar);
                nav.Controls.Add(item);
                _navItems.Insert(0, item);
            }

            var hint = new Label();
            hint.Text = "只读采集 + 官方清单\r\n不联网时不查驱动";
            hint.Dock = DockStyle.Bottom;
            hint.Height = 40;
            hint.Padding = new Padding(14, 0, 0, 0);
            hint.Font = new Font("Microsoft YaHei UI", 7.5F);
            hint.ForeColor = Ink3;
            nav.Controls.Add(hint);

            return nav;
        }

        private void ShowPage(int index)
        {
            if (_pages == null || index < 0 || index >= _pages.Length) return;

            _currentPage = index;
            {
                string[] _names = { "配置核对", "驱动安装", "固件提示", "备份回滚", "硬件测试", "压力测试", "系统排查" };
                if (index >= 0 && index < _names.Length) Log.Write("[页面] 切换到 " + _names[index]);
            }
            for (int i = 0; i < _pages.Length; i++)
                _pages[i].Visible = (i == index);

            for (int i = 0; i < _navItems.Count; i++)
            {
                bool on = (i == index);
                _navItems[i].BackColor = on ? Paper : Paper2;
                foreach (Control c in _navItems[i].Controls)
                {
                    if (c.Name == "bar") c.BackColor = on ? Blue : Paper2;
                    else if (c is Label && c.Left < 40) c.ForeColor = on ? Blue : Ink3;
                    else if (c is Label) c.ForeColor = on ? Ink : Ink2;
                }
            }

            string[] titles = { "配置核对", "驱动安装", "固件提示", "备份回滚", "硬件测试", "压力测试", "系统排查" };
            string[] subs =
            {
                "只读采集本机硬件，与购买配置逐项对照",
                "按序列号查官网清单，与本机版本逐条比对",
                "BIOS / ME 固件只提示，不下载不执行",
                "备份与回滚本机驱动",
                "键盘按键、鼠标按键与回报率、屏幕坏点与刷新率",
                "CPU / GPU 烤机与温度曲线",
                "蓝屏记录（bugcheck 码）与第三方驱动清理"
            };
            if (_pageTitle != null) _pageTitle.Text = titles[index];
            if (_pageSub != null) _pageSub.Text = subs[index];

            if (index == 0 && _configPage != null)
                _configPage.RefreshData();

            // 键盘测试是全局监听的，切进这一页就清空，避免带上别处的按键
            if (index == 4 && _testPage != null)
                _testPage.ResetKeyboard();

            // 系统排查页的数据（pnputil / 事件日志）在真正切进来时才读
            if (index == 6 && _systemPage != null)
                _systemPage.RefreshCurrent();

            // 压力测试页的 LHM 采样器也在切进来时才初始化
            if (index == 5 && _pressurePage != null)
                _pressurePage.EnsureSampler();
        }

        // ---------------- 固件页 ----------------
        private Panel BuildFirmwarePage()
        {
            var page = new Panel();
            page.Dock = DockStyle.Fill;
            page.BackColor = Paper;

            var head = new Panel();
            head.Dock = DockStyle.Top;
            head.Height = 58;
            head.BackColor = Paper;

            var title = new Label();
            title.Text = "固件提示";
            title.Font = new Font("Microsoft YaHei UI", 13.5F, FontStyle.Bold);
            title.ForeColor = Ink;
            title.AutoSize = true;
            title.Location = new Point(0, 4);

            var sub = new Label();
            sub.Text = "BIOS / ME 固件不提供下载与执行，只给版本对比和官网说明。刷写失败会使机器无法开机。";
            sub.Font = new Font("Microsoft YaHei UI", 8F);
            sub.ForeColor = Ink3;
            sub.AutoSize = true;
            sub.Location = new Point(0, 30);

            head.Controls.Add(title);
            head.Controls.Add(sub);

            var warn = new Panel();
            warn.Dock = DockStyle.Top;
            warn.Height = 40;
            warn.BackColor = Color.FromArgb(253, 246, 236);
            warn.Padding = new Padding(14, 0, 14, 0);

            var wt = new Label();
            wt.Text = "\u26A0   本页只读 · 请在联想官网核对机型与版本后自行操作";
            wt.Dock = DockStyle.Fill;
            wt.TextAlign = ContentAlignment.MiddleLeft;
            wt.Font = new Font("Microsoft YaHei UI", 8.5F);
            wt.ForeColor = Orange;
            warn.Controls.Add(wt);

            _fwList = new Panel();
            _fwList.Dock = DockStyle.Fill;
            _fwList.BackColor = Paper;
            _fwList.AutoScroll = true;
            _fwList.Padding = new Padding(0, 10, 0, 0);

            page.Controls.Add(_fwList);
            page.Controls.Add(warn);
            // head 里有重复的页面标题，顶栏已经显示了，不再加入
            return page;
        }

        private void RenderFirmware()
        {
            if (_fwList == null) return;
            _fwList.SuspendLayout();
            _fwList.Controls.Clear();

            var fws = _results.Where(x => x.StatusRank == 5).ToList();
            if (fws.Count == 0)
            {
                var none = new Label();
                none.Text = "未检测到固件条目（或尚未完成检测）";
                none.ForeColor = Ink3;
                none.Font = new Font("Microsoft YaHei UI", 8.5F);
                none.AutoSize = true;
                none.Location = new Point(4, 8);
                _fwList.Controls.Add(none);
            }
            else
            {
                int y = 0;
                foreach (var r in fws) y += AddFirmwareCard(r, y);
            }
            _fwList.ResumeLayout();
        }

        private int AddFirmwareCard(CompareResult r, int y)
        {
            int w = _fwList.ClientSize.Width - 8;
            if (w < 300) w = 300;

            var card = new Panel();
            card.Location = new Point(0, y);
            card.Width = w;
            card.Height = 118;
            card.BackColor = Paper2;
            card.BorderStyle = BorderStyle.FixedSingle;

            var n = new Label();
            n.Text = r.Driver.PartName + "　" + r.Driver.DriverName;
            n.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            n.ForeColor = Ink;
            n.AutoSize = true;
            n.Location = new Point(14, 12);

            var v = new Label();
            v.Text = "本机 " + (string.IsNullOrEmpty(r.LocalVersion) ? "未读取" : r.LocalVersion) +
                     "     官网 " + r.Driver.Version;
            v.Font = new Font("Consolas", 9F);
            v.ForeColor = Ink2;
            v.AutoSize = true;
            v.Location = new Point(14, 38);

            var d = new Label();
            d.Text = string.IsNullOrEmpty(r.LocalVersion)
                ? "本机 ME 版本读不到；固件请通过联想官网下载，本工具不下载、不执行。"
                : "这是 Intel 版本体系（16.x），与联想包名 " + r.Driver.Version + " 不同源，无法自动判断新旧。";
            d.Font = new Font("Microsoft YaHei UI", 8F);
            d.ForeColor = Ink3;
            d.AutoSize = false;
            d.Size = new Size(w - 28, 36);
            d.Location = new Point(14, 62);

            var btn = new Button();
            btn.Text = "查看说明";
            btn.Size = new Size(104, 30);
            btn.Location = new Point(w - 122, 12);
            btn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderColor = Line;
            btn.BackColor = Paper2;
            btn.ForeColor = Ink2;
            btn.Font = new Font("Microsoft YaHei UI", 8.5F);
            CompareResult captured = r;
            btn.Click += delegate
            {
                using (var f = new FirmwareNoteForm(captured)) f.ShowDialog(this);
            };

            card.Controls.Add(n);
            card.Controls.Add(v);
            card.Controls.Add(d);
            card.Controls.Add(btn);
            _fwList.Controls.Add(card);
            return card.Height + 10;
        }

        // ---------------- 备份页 ----------------
        private Panel BuildBackupPage()
        {
            var page = new Panel();
            page.Dock = DockStyle.Fill;
            page.BackColor = Paper;

            var head = new Panel();
            head.Dock = DockStyle.Top;
            head.Height = 58;
            head.BackColor = Paper;

            var title = new Label();
            title.Text = "备份回滚";
            title.Font = new Font("Microsoft YaHei UI", 13.5F, FontStyle.Bold);
            title.ForeColor = Ink;
            title.AutoSize = true;
            title.Location = new Point(0, 4);

            var sub = new Label();
            sub.Text = "备份用 pnputil 导出本机全部第三方驱动（体积较大）；回滚把备份里的 INF 重新装回。";
            sub.Font = new Font("Microsoft YaHei UI", 8F);
            sub.ForeColor = Ink3;
            sub.AutoSize = true;
            sub.Location = new Point(0, 30);

            head.Controls.Add(title);
            head.Controls.Add(sub);

            var btns = new Panel();
            btns.Dock = DockStyle.Top;
            btns.Height = 56;
            btns.BackColor = Paper;

            var b1 = new Button();
            b1.Text = "备份当前驱动…";
            b1.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            b1.FlatStyle = FlatStyle.Flat;
            b1.FlatAppearance.BorderColor = Line;
            b1.BackColor = Paper2;
            b1.ForeColor = Ink;
            b1.Size = new Size(164, 36);
            b1.Location = new Point(0, 4);
            b1.Click += async delegate { await DoBackup(); };

            var b2 = new Button();
            b2.Text = "回滚最近备份";
            b2.Font = new Font("Microsoft YaHei UI", 9F);
            b2.FlatStyle = FlatStyle.Flat;
            b2.FlatAppearance.BorderColor = Line;
            b2.BackColor = Paper2;
            b2.ForeColor = Ink2;
            b2.Size = new Size(164, 36);
            b2.Location = new Point(176, 4);
            b2.Click += delegate
            {
                string dir = InstallEngine.LatestBackup();
                if (string.IsNullOrEmpty(dir))
                {
                    MessageBox.Show(this, "还没有备份记录。请先点「备份驱动」。",
                        "无备份", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                using (var dlg = new RestoreDialog(dir)) dlg.ShowDialog(FindForm());
            };

            btns.Controls.Add(b1);
            btns.Controls.Add(b2);

            _bkList = new Panel();
            _bkList.Dock = DockStyle.Fill;
            _bkList.BackColor = Paper;
            _bkList.AutoScroll = true;
            _bkList.Padding = new Padding(0, 12, 0, 0);

            page.Controls.Add(_bkList);
            page.Controls.Add(btns);
            // head 里有重复的页面标题，顶栏已经显示了，不再加入
            return page;
        }

        private void RenderBackup()
        {
            if (_bkList == null) return;
            _bkList.SuspendLayout();
            _bkList.Controls.Clear();

            string latest = InstallEngine.LatestBackup();

            var box = new Panel();
            box.BackColor = Paper2;
            box.BorderStyle = BorderStyle.FixedSingle;
            box.Location = new Point(0, 4);
            box.Width = Math.Max(320, _bkList.ClientSize.Width - 8);
            box.Height = 84;

            var l1 = new Label();
            l1.Text = "备份目录";
            l1.Font = new Font("Microsoft YaHei UI", 8F);
            l1.ForeColor = Ink3;
            l1.AutoSize = true;
            l1.Location = new Point(14, 12);

            var l2 = new Label();
            l2.Text = InstallEngine.BackupDir;
            l2.Font = new Font("Consolas", 8.5F);
            l2.ForeColor = Ink;
            l2.AutoSize = false;
            l2.Size = new Size(box.Width - 28, 18);
            l2.Location = new Point(14, 30);

            var l3 = new Label();
            l3.Text = string.IsNullOrEmpty(latest)
                ? "还没有备份记录"
                : "最近一次：" + Path.GetFileName(latest) + "　·　" + InstallEngine.CountSubDirSize(latest);
            l3.Font = new Font("Microsoft YaHei UI", 8.5F);
            l3.ForeColor = string.IsNullOrEmpty(latest) ? Ink3 : Green;
            l3.AutoSize = true;
            l3.Location = new Point(14, 54);

            box.Controls.Add(l1);
            box.Controls.Add(l2);
            box.Controls.Add(l3);
            _bkList.Controls.Add(box);
            _bkList.ResumeLayout();
        }

        // ---------------- 数据 ----------------
        private void RefreshAll()
        {
            if (_busy) return;
            _busy = true;
            _btnRefresh.Enabled = false;
            Cursor = Cursors.WaitCursor;
            _listPanel.Controls.Clear();
            AppendLog("── 开始检测 " + DateTime.Now.ToString("HH:mm:ss"));

            AppendLog("[trace] 后台任务启动");

            // 全部耗时操作丢后台；UI 只在 BeginInvoke 里碰
            System.Threading.Tasks.Task.Run((Action)delegate
            {
                AppendLogFromWorker("[trace] 进入后台线程");
                var log = new System.Text.StringBuilder();
                List<CompareResult> results = null;
                string error = null;
                bool unsupported = false;
                string vendorName = null;

                try
                {
                    string sn = LenovoApi.ReadSerialNumber();
                    string vendor = LenovoApi.ReadVendor();
                    vendorName = vendor;
                    log.AppendLine("序列号 " + sn + " · 厂商 " + vendor);

                    var catalog = CatalogFactory.Create(vendor);

                    if (catalog == null && string.IsNullOrEmpty(vendor))
                    {
                        log.AppendLine("厂商未识别，按序列号试探 Lenovo…");
                        var probe = new LenovoCatalog();
                        var m0 = probe.GetMachine(sn);
                        if (m0 != null)
                        {
                            catalog = probe;
                            log.AppendLine("序列号在 Lenovo 库命中：" + m0.ProductModel);
                        }
                    }

                    if (catalog == null)
                    {
                        unsupported = true;
                    }
                    else
                    {
                        var machine = catalog.GetMachine(sn);
                        if (machine != null)
                            log.AppendLine("机型 " + machine.ProductModel + " / MTM " + machine.MTM);
                        else
                            log.AppendLine("机型档案查询失败，仅能依据本机设备判断");

                        var drivers = catalog.GetDrivers(sn);
                        log.AppendLine("官网清单 " + drivers.Count + " 条");

                        var devices = LenovoApi.QueryDevices();
                        log.AppendLine("本机设备 " + devices.Count + " 项");

                        results = LenovoApi.Compare(drivers, devices);
                        log.AppendLine("比对完成 " + results.Count + " 条");
                    AppendLogFromWorker("[trace] 数据就绪，准备回 UI");
                    }
                }
                catch (Exception ex)
                {
                    error = ex.ToString();
                    AppendLogFromWorker("[trace] 后台异常: " + ex.GetType().Name + " " + ex.Message);
                }

                try
                {
                    if (IsDisposed || !IsHandleCreated)
                    {
                        AppendLogFromWorker("[trace] 窗体已销毁，放弃回填");
                        return;
                    }
                    AppendLogFromWorker("[trace] 开始 BeginInvoke");
                    BeginInvoke((Action)delegate
                    {
                        try
                        {
                            foreach (var line in log.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries))
                                AppendLog(line);

                            if (error != null)
                            {
                                AppendLog("[错误] " + error);
                                _status.Text = "检测失败：" + error.Split(new[] { Environment.NewLine }, StringSplitOptions.None)[0];
                            }
                            else if (unsupported)
                            {
                                AppendLog("暂不支持该厂商");
                                ShowUnsupported(vendorName);
                            }
                            else if (results != null)
                            {
                                Render(results);
                            }
                        }
                        catch (Exception ex2)
                        {
                            AppendLog("[渲染错误] " + ex2.ToString());
                            _status.Text = "渲染失败：" + ex2.Message;
                        }
                        finally
                        {
                            _busy = false;
                            _btnRefresh.Enabled = true;
                            Cursor = Cursors.Default;
                        }
                    });
                }
                catch (Exception ex) { Log.Swallow(ex, "MainForm:829"); }
            });
        }

        private void Render(List<CompareResult> results)
        {
            // 固件页 / 备份页都要读这份数据，必须存下来
            _results = results;

            _listPanel.SuspendLayout();
            _listPanel.Controls.Clear();

            AddSection("需要处理", results.Count(x => x.StatusRank == 1));
            foreach (var r in results.Where(x => x.StatusRank == 1)) AddRow(r);

            AddSection("未安装 / 待确认", results.Count(x => x.StatusRank == 2));
            foreach (var r in results.Where(x => x.StatusRank == 2)) AddRow(r);

            AddSection("本机比官网新", results.Count(x => x.StatusRank == 3));
            foreach (var r in results.Where(x => x.StatusRank == 3)) AddRow(r);

            AddSection("已是最新", results.Count(x => x.StatusRank == 0));
            foreach (var r in results.Where(x => x.StatusRank == 0)) AddRow(r);

            AddCollapsibleSection("非本机配置（同型号其它配置的驱动）",
                                  results.Where(x => x.StatusRank == 4).ToList());

            AddSection("固件与可选应用", results.Count(x => x.StatusRank == 5 || x.StatusRank == 6));
            foreach (var r in results.Where(x => x.StatusRank == 5 || x.StatusRank == 6)) AddRow(r);

            _listPanel.ResumeLayout();

            int upd = results.Count(x => x.StatusRank == 1);
            int ok = results.Count(x => x.StatusRank == 0);
            int newer = results.Count(x => x.StatusRank == 3);
            int miss = results.Count(x => x.StatusRank == 2 || x.StatusRank == 4);
            int fw = results.Count(x => x.StatusRank == 5 || x.StatusRank == 6);

            BuildStats(upd, ok, newer, miss, fw);

            if (_machine != null)
            {
                _machineInfo.Text = "机型 " + _machine.ProductModel + "\r\nMTM " + _machine.MTM +
                                    " · SN " + Mask(_machine.MachineNo) + "\r\n" + _machine.ProductDate + " 出厂 · " + _machine.SaleArea;
            }
            else
            {
                _machineInfo.Text = "未读取到 Lenovo 机型信息";
            }

            _status.Text = (InstallEngine.IsAdmin ? "" : "只读模式（未提权） · ") +
                           string.Format("可更新 {0} · 已最新 {1} · 非本机配置 {2}", upd, ok, miss);

            RenderFirmware();
            RenderBackup();

            AppendLog("[render] 列表控件数=" + _listPanel.Controls.Count +
                      " 固件=" + _results.Count(x => x.StatusRank == 5));
        }

        private static string Mask(string sn)
        {
            if (string.IsNullOrEmpty(sn) || sn.Length < 4) return sn;
            if (sn.Length <= 6) return sn.Substring(0, 2) + "**";
            return sn.Substring(0, 4) + new string('*', sn.Length - 6) + sn.Substring(sn.Length - 2);
        }

        private void ShowUnsupported(string vendor)
        {
            _listPanel.Controls.Clear();
            var l = new Label();
            l.Text = "暂不支持该厂商：" + (string.IsNullOrEmpty(vendor) ? "未识别" : vendor) +
                     "\r\n\r\n当前支持的厂商：" + CatalogFactory.SupportedVendors() +
                     "\r\n\r\n新增厂商只需在 Catalog.cs 里增加一个 IDriverCatalog 实现。";
            l.Font = new Font("Microsoft YaHei UI", 10F);
            l.ForeColor = Ink2;
            l.AutoSize = false;
            l.Size = new Size(620, 150);
            l.Margin = new Padding(22, 24, 0, 0);
            _listPanel.Controls.Add(l);
            _status.Text = "不支持的厂商";
            _machineInfo.Text = vendor;
            BuildStats(0, 0, 0, 0, 0);
        }

        private void AddSection(string title, int count)
        {
            var l = new Label();
            l.Text = title.ToUpperInvariant() + "   " + count + " 项";
            l.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold);
            l.ForeColor = Ink3;
            l.AutoSize = false;
            l.Height = 26;
            l.Width = Math.Max(200, _listPanel.ClientSize.Width - 40);
            l.Margin = new Padding(2, 10, 0, 2);
            l.TextAlign = ContentAlignment.BottomLeft;
            _listPanel.Controls.Add(l);
        }

        /// <summary>可折叠分组：默认只显示一行标题，点击展开/收起</summary>
        private void AddCollapsibleSection(string title, List<CompareResult> items)
        {
            if (items.Count == 0) return;

            var head = new Label();
            head.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold);
            head.ForeColor = Ink3;
            head.AutoSize = false;
            head.Height = 26;
            head.Width = Math.Max(200, _listPanel.ClientSize.Width - 40);
            head.Margin = new Padding(2, 10, 0, 2);
            head.TextAlign = ContentAlignment.BottomLeft;
            head.Cursor = Cursors.Hand;
            head.Text = title.ToUpperInvariant() + "   " + items.Count + " 项  ▸ 点击展开";

            bool expanded = false;
            head.Click += delegate
            {
                if (_busy) return;
                if (!expanded)
                {
                    int idx = _listPanel.Controls.GetChildIndex(head) + 1;
                    foreach (var r in items)
                    {
                        var card = BuildRow(r);
                        _listPanel.Controls.Add(card);
                        _listPanel.Controls.SetChildIndex(card, idx++);
                    }
                    expanded = true;
                    head.Text = title.ToUpperInvariant() + "   " + items.Count + " 项  ▾ 点击收起";
                }
                else
                {
                    var mark = new List<Control>();
                    int start = _listPanel.Controls.GetChildIndex(head) + 1;
                    for (int i = start; i < _listPanel.Controls.Count; i++)
                    {
                        var c = _listPanel.Controls[i];
                        var rr = c.Tag as CompareResult;
                        if (rr != null && items.Contains(rr)) mark.Add(c);
                        else break;
                    }
                    foreach (var c in mark) _listPanel.Controls.Remove(c);
                    expanded = false;
                    head.Text = title.ToUpperInvariant() + "   " + items.Count + " 项  ▸ 点击展开";
                }
            };

            _listPanel.Controls.Add(head);
        }

        private Color StatusColor(CompareResult r)
        {
            switch (r.StatusRank)
            {
                case 1: return Red;
                case 0: return Green;
                case 2: return Orange;
                case 3: return Blue;
                case 4: return Grey;
                default: return Color.FromArgb(140, 58, 58);
            }
        }

        private void AddRow(CompareResult r)
        {
            _listPanel.Controls.Add(BuildRow(r));
        }

        private Panel BuildRow(CompareResult r)
        {
            int w = Math.Max(560, _listPanel.ClientSize.Width - 46);
            var card = new Panel();
            card.Width = w;
            card.Height = 54;
            card.Margin = new Padding(0, 0, 0, 5);
            card.BackColor = r.StatusRank == 1 ? Color.FromArgb(251, 241, 238) : Paper2;
            card.BorderStyle = BorderStyle.FixedSingle;

            var bar = new Panel();
            bar.Dock = DockStyle.Left;
            bar.Width = 3;
            bar.BackColor = StatusColor(r);

            var part = new Label();
            part.Text = r.Driver.PartName;
            part.Font = new Font("Microsoft YaHei UI", 7.5F);
            part.ForeColor = Ink3;
            part.AutoSize = true;
            part.Location = new Point(14, 8);

            var name = new Label();
            name.Text = r.Driver.DriverName;
            name.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            name.ForeColor = Ink;
            name.AutoSize = false;
            name.Size = new Size(238, 20);
            name.Location = new Point(12, 26);
            name.AutoEllipsis = true;

            var off = new Label();
            off.Text = "官网 " + r.Driver.Version;
            off.Font = new Font("Consolas", 8F);
            off.ForeColor = Ink2;
            off.AutoSize = false;
            off.Size = new Size(168, 17);
            off.Location = new Point(262, 8);
            off.AutoEllipsis = true;

            var loc = new Label();
            loc.Text = "本机 " + (string.IsNullOrEmpty(r.LocalVersion) ? "—" : r.LocalVersion);
            loc.Font = new Font("Consolas", 8F);
            loc.ForeColor = Ink2;
            loc.AutoSize = false;
            loc.Size = new Size(168, 17);
            loc.Location = new Point(262, 26);
            loc.AutoEllipsis = true;

            var st = new Label();
            st.Text = r.Status;
            st.Font = new Font("Microsoft YaHei UI", 7.5F, FontStyle.Bold);
            st.TextAlign = ContentAlignment.MiddleCenter;
            st.Size = new Size(66, 20);
            st.Location = new Point(438, 17);
            st.ForeColor = StatusColor(r);
            st.BorderStyle = BorderStyle.FixedSingle;
            st.BackColor = Color.FromArgb(250, 246, 238);

            var dev = new Label();
            dev.Text = r.MatchedDevice;
            dev.Font = new Font("Microsoft YaHei UI", 7.5F);
            dev.ForeColor = Ink3;
            dev.AutoSize = false;
            dev.Size = new Size(w - 640, 18);
            dev.Location = new Point(514, 19);
            dev.AutoEllipsis = true;

            var btn = new Button();
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderColor = Line;
            btn.BackColor = Paper2;
            btn.ForeColor = Ink2;
            btn.Font = new Font("Microsoft YaHei UI", 8F);
            btn.Size = new Size(104, 30);
            btn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btn.Location = new Point(w - 112, 14);

            if (r.Driver.PartName == "BIOS" || r.Driver.PartName == "固件")
            {
                btn.Text = "查看说明";
                btn.Click += delegate { ShowFirmwareNote(r); };
            }
            else
            {
                btn.Text = r.StatusRank == 3 ? "下载官网" : (r.StatusRank == 0 ? "重装" : "下载并安装");
                bool dlOnly = r.StatusRank == 3;
                btn.Click += async delegate { await InstallOne(r, btn, dlOnly); };
            }

            card.Controls.Add(bar);
            card.Controls.Add(part);
            card.Controls.Add(name);
            card.Controls.Add(off);
            card.Controls.Add(loc);
            card.Controls.Add(st);
            card.Controls.Add(dev);
            card.Controls.Add(btn);

            var pb = new ProgressBar();
            pb.Location = new Point(4, 47);
            pb.Size = new Size(w - 8, 5);
            pb.Style = ProgressBarStyle.Continuous;
            pb.Visible = false;
            pb.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _bars[r] = pb;

            var pct = new Label();
            pct.Location = new Point(w - 176, 19);
            pct.Size = new Size(56, 16);
            pct.TextAlign = ContentAlignment.MiddleRight;
            pct.Font = new Font("Consolas", 7.5F);
            pct.ForeColor = Ink3;
            pct.Visible = false;
            pct.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _pcts[r] = pct;

            card.Controls.Add(pb);
            card.Controls.Add(pct);

            card.Tag = r;
            return card;
        }

        private void BuildStats(int upd, int ok, int newer, int miss, int fw)
        {
            _sideStats.Controls.Clear();

            var h = new Label();
            h.Text = "检测结果";
            h.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold);
            h.ForeColor = Ink3;
            h.Location = new Point(14, 10);
            h.AutoSize = true;
            _sideStats.Controls.Add(h);

            int y = 34;
            AddStat("可更新", upd, Red, ref y);
            AddStat("已最新", ok, Green, ref y);
            AddStat("比官网新", newer, Blue, ref y);
            AddStat("非本机配置", miss, Orange, ref y);
            AddStat("固件 / 应用", fw, Color.FromArgb(140, 58, 58), ref y);

            var b1 = new Button();
            b1.Text = "备份驱动";
            b1.FlatStyle = FlatStyle.Flat;
            b1.FlatAppearance.BorderColor = Line;
            b1.BackColor = Paper2;
            b1.ForeColor = Ink2;
            b1.Font = new Font("Microsoft YaHei UI", 8F);
            b1.Size = new Size(120, 28);
            b1.Location = new Point(14, y + 8);
            b1.Click += async delegate { await DoBackup(); };

            var b2 = new Button();
            b2.Text = "回滚";
            b2.FlatStyle = FlatStyle.Flat;
            b2.FlatAppearance.BorderColor = Line;
            b2.BackColor = Paper2;
            b2.ForeColor = Ink2;
            b2.Font = new Font("Microsoft YaHei UI", 8F);
            b2.Size = new Size(120, 28);
            b2.Location = new Point(140, y + 8);
            b2.Click += delegate
            {
                string dir = InstallEngine.LatestBackup();
                if (string.IsNullOrEmpty(dir))
                {
                    MessageBox.Show(this, "还没有备份记录。请先点「备份驱动」。",
                        "无备份", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                using (var dlg = new RestoreDialog(dir)) dlg.ShowDialog(FindForm());
            };

            _sideStats.Controls.Add(b1);
            _sideStats.Controls.Add(b2);

            var note = new Label();
            note.Text = "数据来自 Lenovo 官网，按序列号实时查询。\r\n固件永不自动执行。";
            note.Font = new Font("Microsoft YaHei UI", 7.5F);
            note.ForeColor = Ink3;
            note.Location = new Point(14, y + 44);
            note.Size = new Size(250, 34);
            _sideStats.Controls.Add(note);
        }

        private void AddStat(string text, int value, Color dot, ref int y)
        {
            var d = new Panel();
            d.Size = new Size(7, 7);
            d.Location = new Point(14, y + 6);
            d.BackColor = dot;

            var l = new Label();
            l.Text = text;
            l.Font = new Font("Microsoft YaHei UI", 8.5F);
            l.ForeColor = Ink2;
            l.Location = new Point(28, y);
            l.AutoSize = true;

            var v = new Label();
            v.Text = value.ToString();
            v.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            v.ForeColor = Ink;
            v.Location = new Point(236, y - 3);
            v.AutoSize = true;

            _sideStats.Controls.Add(d);
            _sideStats.Controls.Add(l);
            _sideStats.Controls.Add(v);
            y += 26;
        }

        // ---------------- 安装 ----------------
        private async Task InstallOne(CompareResult r, Button btn, bool downloadOnly)
        {
            if (!InstallEngine.IsAdmin)
            {
                var ask = MessageBox.Show(this,
                    "安装驱动需要管理员权限。\r\n是否以管理员身份重新启动本程序？",
                    "需要管理员权限", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (ask == DialogResult.Yes)
                {
                    if (InstallEngine.RelaunchAsAdmin())
                    {
                        Close();
                        Application.Exit();
                    }
                    else
                    {
                        MessageBox.Show(this, "提权失败。请关闭本程序，右键选择「以管理员身份运行」。",
                                        "提权失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
                return;
            }

            string fileName = r.Driver.FileName;
            if (string.IsNullOrEmpty(fileName))
            {
                MessageBox.Show(this, "官网没有给出这个驱动的文件名，无法下载。", "无法下载",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string dest = Path.Combine(InstallEngine.DownloadDir, fileName);
            bool needDownload = !File.Exists(dest);

            string msg = downloadOnly
                ? "将下载到：\r\n" + dest + "\r\n\r\n仅下载、不安装（本机版本比官网新，装旧版没有意义）。继续？"
                : "将下载并静默安装：\r\n\r\n" + r.Driver.DriverName + "\r\n版本 " + r.Driver.Version +
                  "\r\n\r\n下载位置：" + dest + "\r\n\r\n安装期间请勿断电。继续？";

            if (MessageBox.Show(this, msg, downloadOnly ? "确认下载" : "确认安装",
                                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            btn.Enabled = false;
            ProgressBar pb = null;
            Label pct = null;
            _bars.TryGetValue(r, out pb);
            _pcts.TryGetValue(r, out pct);
            if (pb != null) { pb.Visible = true; pb.Value = 0; }
            if (pct != null) { pct.Visible = true; pct.Text = needDownload ? "0%" : "安装中"; }
            _progress.Visible = true;
            _progress.Value = 0;

            try
            {
                if (needDownload)
                {
                    AppendLog("下载 " + fileName);
                    var prog = new Progress<int>(p =>
                    {
                        if (pb != null) pb.Value = Math.Min(100, p);
                        if (pct != null) pct.Text = p + "%";
                        _progress.Value = Math.Min(100, p);
                    });
                    await InstallEngine.DownloadAsync(r.Driver.FilePath, dest, prog);
                    long len = new FileInfo(dest).Length;
                    AppendLog("下载完成 " + (len / 1024) + " KB");
                }
                else
                {
                    AppendLog("使用已下载的 " + fileName);
                }

                if (downloadOnly)
                {
                    if (pct != null) pct.Text = "已下载";
                    try { Process.Start("explorer.exe", "/select,\"" + dest + "\""); }
                    catch (Exception) { }
                    return;
                }

                if (pct != null) pct.Text = "安装中";
                AppendLog("安装 " + r.Driver.DriverName + " …");

                // 安装前快照：用来算这次到底新增了哪些 oem*.inf
                var oemBefore = await Task.Run(() => InstallHistory.SnapshotOem());

                int code = await InstallEngine.RunInstallerAsync(dest, InstallEngine.InnoSilentArgs);
                AppendLog(InstallEngine.DescribeExitCode(code));

                // ---- 安装后校验：重新读设备版本，判断是否真的变了 ----
                if (pct != null) pct.Text = "校验中";
                await Task.Delay(1500);

                // ---- 记安装台账：新增的 oem*.inf 归属这次安装 ----
                try
                {
                    var oemAfter = await Task.Run(() => InstallHistory.SnapshotOem());
                    var added = oemAfter.Where(x => !oemBefore.Contains(x)).ToList();
                    InstallHistory.Record(new InstallHistory.Entry
                    {
                        Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        Driver = r.Driver.PartName + " " + r.Driver.DriverName,
                        Version = r.Driver.Version,
                        Package = fileName,
                        OemInfs = added,
                    });
                    AppendLog(added.Count > 0
                        ? "登记安装记录：" + string.Join(", ", added)
                        : "登记安装记录：本次没有新增驱动包（可能同版本已在位）");
                }
                catch (Exception hex) { AppendLog("[台账] 记录失败：" + hex.Message); }
                var devices = await Task.Run(() => LenovoApi.QueryDevices());
                var hits = await Task.Run(() => LenovoApi.MatchFor(r.Driver, devices));
                string nowVer = "";
                if (hits.Count > 0)
                    nowVer = hits.OrderByDescending(h => h.Version).First().Version;

                bool changed = !string.Equals(nowVer, r.LocalVersion, StringComparison.OrdinalIgnoreCase);
                if (changed)
                {
                    AppendLog("版本变化 " + (string.IsNullOrEmpty(r.LocalVersion) ? "—" : r.LocalVersion) +
                              " → " + nowVer);
                    if (code != 1641) ShowRestartDialog();
                }
                else
                {
                    AppendLog("版本未变化（安装器已执行）");
                    string codeNote = code == 259
                        ? "退出码 259 = 「没有需要安装的项目」，通常说明同版本已在位。"
                        : "退出码 " + code + "。";
                    string tip = "安装器已执行完毕，但本机驱动版本没有变化。\r\n" + codeNote + "\r\n\r\n" +
                                 "官网版本：" + r.Driver.Version + "\r\n" +
                                 "本机版本：" + (string.IsNullOrEmpty(nowVer) ? "未匹配到设备" : nowVer) + "\r\n\r\n" +
                                 "常见原因：\r\n" +
                                 "  · 本机已是该版本，安装器主动跳过（正常保护）\r\n" +
                                 "  · 这个包装的不是该设备的驱动（例如应用类）\r\n" +
                                 "  · 需要重启后才生效\r\n\r\n" +
                                 "安装器日志：" + InstallEngine.LastLogPath(dest);
                    MessageBox.Show(this, tip, "没有变化", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                AppendLog("[错误] " + ex.Message);
                MessageBox.Show(this, "下载或安装失败：\r\n" + ex.Message + "\r\n\r\n若是网络问题，可手动从官网下载。",
                                "失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btn.Enabled = true;
                if (pb != null) pb.Visible = false;
                if (pct != null) pct.Visible = false;
                _progress.Visible = false;
            }
        }

        private void ShowRestartDialog()
        {
            using (var f = new Form())
            {
                f.Text = "安装完成";
                f.ClientSize = new Size(400, 168);
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MaximizeBox = false;
                f.MinimizeBox = false;
                f.BackColor = Paper;
                f.Font = new Font("Microsoft YaHei UI", 9F);

                var msg = new Label();
                msg.Text = "已安装完成。\r\n\r\n部分驱动需要重启后才会生效。";
                msg.Location = new Point(24, 24);
                msg.Size = new Size(350, 54);
                msg.ForeColor = Ink;
                msg.Font = new Font("Microsoft YaHei UI", 10F);

                var now = new Button();
                now.Text = "现在重启";
                now.Location = new Point(120, 110);
                now.Size = new Size(122, 36);
                now.FlatStyle = FlatStyle.Flat;
                now.FlatAppearance.BorderColor = Ink;
                now.BackColor = Ink;
                now.ForeColor = Paper;
                now.DialogResult = DialogResult.Yes;

                var later = new Button();
                later.Text = "稍后重启";
                later.Location = new Point(244, 110);
                later.Size = new Size(122, 36);
                later.FlatStyle = FlatStyle.Flat;
                later.FlatAppearance.BorderColor = Line;
                later.BackColor = Paper2;
                later.ForeColor = Ink2;
                later.DialogResult = DialogResult.No;

                f.Controls.Add(msg);
                f.Controls.Add(now);
                f.Controls.Add(later);
                f.AcceptButton = later;
                f.CancelButton = later;

                if (f.ShowDialog(this) == DialogResult.Yes)
                {
                    try
                    {
                        Process.Start("shutdown", "-r -t 8 -c \"驱动安装完成，系统将在 8 秒后重启\"");
                        AppendLog("已计划 8 秒后重启");
                    }
                    catch (Exception ex) { AppendLog("重启失败：" + ex.Message); }
                }
                else
                {
                    AppendLog("选择稍后重启");
                }
            }
        }

        private void ShowFirmwareNote(CompareResult r)
        {
            string extra = r.Driver.PartName == "BIOS"
                ? "\r\n\r\n本机 BIOS：" + (string.IsNullOrEmpty(r.LocalVersion) ? "未读取" : r.LocalVersion) +
                  "\r\n官网 BIOS：" + r.Driver.Version +
                  "\r\n\r\n刷写 BIOS 必须接通电源，过程中不得断电或强制关机，否则可能损坏主板。\r\n本程序不会自动执行它。"
                : "\r\n\r\n固件更新需单独手动进行，本程序不会自动执行。";

            if (r.Driver.PartName == "BIOS" && !string.IsNullOrEmpty(r.Driver.FilePath))
            {
                var ask = MessageBox.Show(this,
                    r.Driver.DriverName + "\r\n版本 " + r.Driver.Version + extra +
                    "\r\n\r\n是否在浏览器中打开 Lenovo 官方下载页？",
                    "固件说明", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (ask == DialogResult.Yes)
                {
                    try { Process.Start("https://newsupport.lenovo.com.cn/driveDownloads_index.html"); }
                    catch (Exception) { }
                }
                return;
            }

            MessageBox.Show(this, r.Driver.DriverName + "\r\n版本 " + r.Driver.Version + extra,
                            "固件说明", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // ---------------- 备份 / 回滚 ----------------
        private Task DoBackup()
        {
            if (!InstallEngine.IsAdmin)
            {
                MessageBox.Show(this, "备份驱动需要管理员权限，请以管理员身份重新启动。", "需要管理员权限");
                return Task.FromResult(0);
            }

            // 备份前先确认：这一步会导出几 GB 的文件
            int drvCount = InstallEngine.CountInstalledDrivers();
            string sizeHint = drvCount > 0 ? "数 GB（上次实测 86 个驱动约 4.9 GB）" : "数 GB";
            string askBackup =
                "即将备份本机全部第三方驱动（不只是联想这 17 个包）。\r\n\r\n" +
                "驱动数量：" + (drvCount > 0 ? drvCount + " 个" : "读取中失败，按实际为准") + "\r\n" +
                "预计占用：" + sizeHint + "\r\n" +
                "预计耗时：1~2 分钟\r\n" +
                "保存到：" + InstallEngine.BackupDir + "\\<时间戳>\r\n\r\n" +
                "备份仅用于回滚，占空间大，确认继续？";

            if (MessageBox.Show(this, askBackup, "确认备份驱动",
                                MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                AppendLog("已取消备份");
                return Task.FromResult(0);
            }

            using (var dlg = new BackupDialog())
            {
                dlg.ShowDialog(this);

                if (!string.IsNullOrEmpty(dlg.ResultDir))
                {
                    AppendLog("驱动备份 → " + dlg.ResultDir);
                    var ask = MessageBox.Show(this,
                        "驱动已备份到：\r\n" + dlg.ResultDir + "\r\n\r\n是否打开文件夹？",
                        "备份完成", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                    if (ask == DialogResult.Yes)
                    {
                        try { Process.Start("explorer.exe", dlg.ResultDir); } catch (Exception) { }
                    }
                }
                else if (dlg.Error != null)
                {
                    MessageBox.Show(this, "备份失败：\r\n" + dlg.Error.Message,
                                    "失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            return Task.FromResult(0);
        }

        private async Task DoRestore()
        {
            if (!InstallEngine.IsAdmin)
            {
                MessageBox.Show(this, "回滚需要管理员权限，请以管理员身份重新启动。", "需要管理员权限");
                return;
            }
            string dir = InstallEngine.LatestBackup();
            if (string.IsNullOrEmpty(dir))
            {
                MessageBox.Show(this, "没有找到备份目录。请先点「备份驱动」。", "无备份");
                return;
            }
            var ask = MessageBox.Show(this, "将从最近一次备份回滚：\r\n" + dir +
                                        "\r\n\r\n过程可能较慢，继续？", "确认回滚",
                                      MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (ask != DialogResult.OK) return;

            Cursor = Cursors.WaitCursor;
            _progress.Visible = true;
            try
            {
                var prog = new Progress<string>(s => AppendLog(s));
                int ok = await InstallEngine.RestoreDriversAsync(dir, prog);
                MessageBox.Show(this, "回滚完成，" + ok + " 个驱动已重新安装。", "完成",
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "回滚失败：" + ex.Message, "失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { Cursor = Cursors.Default; _progress.Visible = false; }
        }

        private static readonly object _logLock = new object();

        /// <summary>后台线程写日志：只落盘，不碰控件</summary>
        private void AppendLogFromWorker(string line)
        {
            lock (_logLock)
            {
                try
                {
                    System.IO.File.AppendAllText(
                        System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.log"),
                        DateTime.Now.ToString("HH:mm:ss.fff") + "  " + line + " [T" +
                        System.Threading.Thread.CurrentThread.ManagedThreadId + "]" + Environment.NewLine);
                }
                catch (Exception ex) { Log.Swallow(ex, "MainForm:1548"); }
            }
        }

        private void AppendLog(string line)
        {
            try
            {
                _logBox.AppendText(line + Environment.NewLine);
                _logBox.SelectionStart = _logBox.TextLength;
                _logBox.ScrollToCaret();
            }
            catch (Exception ex) { Log.Swallow(ex, "MainForm:1560"); }

            // 同时落盘：界面看不到的错误也有据可查
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + line + Environment.NewLine);
            }
            catch (Exception ex) { Log.Swallow(ex, "MainForm:1569"); }
        }
    }
}
