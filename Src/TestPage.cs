using System;
using System.Drawing;
using System.Windows.Forms;

namespace DriverSetup
{
    /// <summary>硬件测试页：键盘 / 鼠标 / 屏幕（坏点 + 刷新率）</summary>
    public class TestPage : RoundPanel
    {
        private static readonly Color Paper = Theme.Bg;
        private static readonly Color Paper2 = Theme.Card;
        private static readonly Color Ink = Theme.Text;
        private static readonly Color Ink2 = Theme.Text2;
        private static readonly Color Ink3 = Theme.Text3;
        private static readonly Color Line = Theme.Line;
        private static readonly Color Green = Theme.Green;
        private static readonly Color Blue = Theme.Accent;

        private KeyboardView _kb;
        private Label _progress;
        private Button[] _tabs;
        private Control[] _views;

        public TestPage()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            BuildUi();
        }

        private void BuildUi()
        {
            // ---------- 子页切换 ----------
            var head = new Panel();
            head.Dock = DockStyle.Top;
            head.Height = 58;
            head.BackColor = Paper;

            string[] names = { "键盘", "鼠标", "屏幕" };
            _tabs = new Button[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                var b = new Button();
                b.Text = names[i];
                b.Size = new Size(96, 32);
                b.Location = new Point(24 + i * 104, 13);
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderColor = Line;
                b.BackColor = Paper2;
                b.ForeColor = Ink2;
                b.Font = new Font("Microsoft YaHei UI", 9F);
                b.Cursor = Cursors.Hand;
                int idx = i;
                b.Click += delegate { ShowView(idx); };
                _tabs[i] = b;
                head.Controls.Add(b);
            }

            var headLine = new Panel();
            headLine.Dock = DockStyle.Bottom;
            headLine.Height = 1;
            headLine.BackColor = Line;
            head.Controls.Add(headLine);

            // ---------- 三个子页 ----------
            var kbPane = BuildKeyboardPane();
            var mousePane = new MouseTestPage();
            var screenPane = BuildScreenPane();

            _views = new Control[] { kbPane, mousePane, screenPane };

            var host = new RoundPanel { DrawFill = false, DrawBorder = false };
            host.Dock = DockStyle.Fill;
            
            foreach (var v in _views)
            {
                v.Dock = DockStyle.Fill;
                v.Visible = false;
                host.Controls.Add(v);
            }

            Controls.Add(host);
            Controls.Add(head);

            ShowView(0);
        }

        // ---------------- 键盘 ----------------
        private Panel BuildKeyboardPane()
        {
            var pane = new Panel();
            pane.BackColor = Paper;

            var bar = new Panel();
            bar.Dock = DockStyle.Top;
            bar.Height = 38;
            bar.BackColor = Paper2;
            bar.BorderStyle = BorderStyle.FixedSingle;
            bar.Padding = new Padding(12, 0, 12, 0);

            _progress = new Label();
            _progress.Text = "已测试 0 / 0";
            _progress.Dock = DockStyle.Fill;
            _progress.TextAlign = ContentAlignment.MiddleLeft;
            _progress.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            _progress.ForeColor = Ink2;
            bar.Controls.Add(_progress);

            var cbLayout = new ComboBox();
            cbLayout.DropDownStyle = ComboBoxStyle.DropDownList;
            cbLayout.FlatStyle = FlatStyle.Flat;
            cbLayout.BackColor = Paper2;
            cbLayout.ForeColor = Ink2;
            cbLayout.Font = new Font("Microsoft YaHei UI", 8.5F);
            cbLayout.Size = new Size(184, 26);
            cbLayout.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            foreach (var n in KeyboardLayouts.Names) cbLayout.Items.Add(n);

            int saved = LoadLayoutPref();
            cbLayout.SelectedIndex = saved;

            var btnReset = new Button();
            btnReset.Text = "重置";
            btnReset.Size = new Size(84, 28);
            btnReset.FlatStyle = FlatStyle.Flat;
            btnReset.FlatAppearance.BorderColor = Line;
            btnReset.BackColor = Paper2;
            btnReset.ForeColor = Ink;
            btnReset.Font = new Font("Microsoft YaHei UI", 8.5F);
            btnReset.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnReset.Location = new Point(bar.Width - 100, 5);
            btnReset.Click += delegate { if (_kb != null) _kb.ResetAll(); };
            bar.Controls.Add(btnReset);
            bar.Controls.Add(cbLayout);
            bar.Resize += delegate
            {
                btnReset.Location = new Point(bar.Width - btnReset.Width - 10, 5);
                cbLayout.Location = new Point(bar.Width - btnReset.Width - cbLayout.Width - 20, 6);
            };

            cbLayout.SelectedIndexChanged += delegate
            {
                if (_kb == null) return;
                _kb.SetLayout(cbLayout.SelectedIndex);
                SaveLayoutPref(cbLayout.SelectedIndex);
            };

            _kb = new KeyboardView();
            _kb.Dock = DockStyle.Fill;
            _kb.ProgressChanged += delegate { UpdateProgress(); };

            pane.Controls.Add(_kb);
            pane.Controls.Add(bar);

            UpdateProgress();
            return pane;
        }

        private void UpdateProgress()
        {
            if (_progress == null || _kb == null) return;
            int done = _kb.TestedCount, total = _kb.TotalCount;
            _progress.Text = string.Format("已测试 {0} / {1}", done, total);
            _progress.ForeColor = done >= total && total > 0 ? Green : Ink2;
            if (done >= total && total > 0)
                _progress.Text = string.Format("已测试 {0} / {1}　全部按键通过", done, total);
        }

        // ---------------- 屏幕 ----------------
        private Panel BuildScreenPane()
        {
            var pane = new Panel();
            pane.BackColor = Paper;
            pane.Padding = new Padding(28, 26, 28, 26);

            var title = new Label();
            title.Text = "屏幕检测";
            title.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
            title.ForeColor = Ink;
            title.AutoSize = true;
            title.Location = new Point(0, 0);
            pane.Controls.Add(title);

            var desc = new Label();
            desc.Text = "坏点检测：全屏循环纯色，找亮点、暗点、漏光。\n" +
                        "刷新率测试：高速横移方块，用帧间隔推算真实刷新率，同时观察拖影。";
            desc.Font = new Font("Microsoft YaHei UI", 9F);
            desc.ForeColor = Ink2;
            desc.AutoSize = true;
            desc.Location = new Point(0, 42);
            pane.Controls.Add(desc);

            var btnDead = MkAction("坏点检测", Blue);
            btnDead.Location = new Point(0, 118);
            btnDead.Click += delegate { RunDeadPixel(); };
            pane.Controls.Add(btnDead);

            var btnRefresh = MkAction("刷新率测试", Ink2);
            btnRefresh.Location = new Point(178, 118);
            btnRefresh.Click += delegate { RunRefresh(); };
            pane.Controls.Add(btnRefresh);

            var mon = new Label();
            mon.AutoSize = true;
            mon.Location = new Point(0, 182);
            mon.Font = new Font("Consolas", 9F);
            mon.ForeColor = Ink3;
            var sb = new System.Text.StringBuilder();
            foreach (var sc in Screen.AllScreens)
            {
                sb.AppendLine((sc.Primary ? "主显示器  " : "显示器    ") + sc.Bounds.Width + " × " +
                              sc.Bounds.Height + "   " + sc.DeviceName);
            }
            mon.Text = sb.ToString().TrimEnd();
            pane.Controls.Add(mon);

            return pane;
        }

        private Button MkAction(string text, Color back)
        {
            var b = new Button();
            b.Text = text;
            b.Size = new Size(162, 40);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = back;
            b.ForeColor = Color.White;
            b.Font = new Font("Microsoft YaHei UI", 9.5F);
            b.Cursor = Cursors.Hand;
            return b;
        }

        private void RunDeadPixel()
        {
            Screen target = Screen.PrimaryScreen;

            if (Screen.AllScreens.Length > 1)
            {
                using (var pick = new ScreenPickForm())
                {
                    if (pick.ShowDialog(FindForm()) != DialogResult.OK) return;
                    if (pick.Selected != null) target = pick.Selected;
                }
            }

            using (var f = new DeadPixelForm(target))
            {
                f.ShowDialog(FindForm());
            }
        }

        private void RunRefresh()
        {
            Screen target = Screen.PrimaryScreen;

            if (Screen.AllScreens.Length > 1)
            {
                using (var pick = new ScreenPickForm())
                {
                    if (pick.ShowDialog(FindForm()) != DialogResult.OK) return;
                    if (pick.Selected != null) target = pick.Selected;
                }
            }

            using (var f = new RefreshTestForm(target)) f.ShowDialog(FindForm());
        }

        // ---------------- 对外接口 ----------------
        private void ShowView(int index)
        {
            if (_views == null || index < 0 || index >= _views.Length) return;

            for (int i = 0; i < _views.Length; i++)
            {
                if (_views[i] == null) continue;
                _views[i].Visible = (i == index);
                if (_tabs != null && _tabs[i] != null)
                {
                    bool on = (i == index);
                    _tabs[i].BackColor = on ? Paper : Paper2;
                    _tabs[i].ForeColor = on ? Ink : Ink2;
                    _tabs[i].Font = new Font("Microsoft YaHei UI", 9F, on ? FontStyle.Bold : FontStyle.Regular);
                }
            }
            if (index < _views.Length && _views[index] != null) _views[index].BringToFront();
        }

        /// <summary>接收 Raw Input 转来的物理按键</summary>
        public void HandleRawKey(int vkey, int scan, bool down)
        {
            if (_kb == null) return;
            var k = (Keys)vkey;
            if (k == Keys.None) return;
            _kb.HandleKey(k, down, scan);
        }

        private static string LayoutPrefPath
        {
            get { return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "keyboard-layout.txt"); }
        }

        private static int LoadLayoutPref()
        {
            try
            {
                string t = System.IO.File.ReadAllText(LayoutPrefPath).Trim();
                int v;
                if (int.TryParse(t, out v) && v >= 0 && v < KeyboardLayouts.Names.Length) return v;
            }
            catch { }
            return 1;   // 默认 102 联想式
        }

        private static void SaveLayoutPref(int id)
        {
            try { System.IO.File.WriteAllText(LayoutPrefPath, id.ToString()); }
            catch { }
        }

        public void ResetKeyboard()
        {
            if (_kb != null) _kb.ResetAll();
        }
    }
}
