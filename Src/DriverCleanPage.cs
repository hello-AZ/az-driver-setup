using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace DriverSetup
{
    /// <summary>驱动清理：列出所有第三方驱动包（oem*.inf），可批量删除</summary>
    public class DriverCleanPage : Panel
    {
        private static readonly Color Paper = Theme.Bg;
        private static readonly Color Paper2 = Theme.Card;
        private static readonly Color Ink = Theme.Text;
        private static readonly Color Ink2 = Theme.Text2;
        private static readonly Color Ink3 = Theme.Text3;
        private static readonly Color Line = Theme.Line;
        private static readonly Color Red = Theme.Red;
        private static readonly Color Green2 = Theme.Green;

        public class DriverEntry
        {
            public string PublishedName = "";   // oem85.inf
            public string OriginalName = "";    // acpivpc.inf
            public string Provider = "";
            public string ClassName = "";
            public string Version = "";

            public bool IsMicrosoft
            {
                get
                {
                    return Provider.IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
        }

        private DataGridView _dgv;
        private Label _summary;
        private TextBox _search;
        private CheckBox _onlyThird;
        private Button _btnDelete;
        private List<DriverEntry> _all = new List<DriverEntry>();
        private CheckBox _onlyDeletable;

        public DriverCleanPage()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            BuildUi();
            // 数据在切到本页时才读（pnputil 扫 90+ 驱动要 2-3 秒）
        }

        private void BuildUi()
        {
            // ---------- 顶栏 ----------
            var head = new Panel();
            head.Dock = DockStyle.Top;
            head.Height = 46;
            head.BackColor = Paper;

            var title = new Label();
            title.Text = "驱动清理";
            title.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
            title.ForeColor = Ink;
            title.AutoSize = true;
            title.Location = new Point(24, 10);
            head.Controls.Add(title);

            _summary = new Label();
            _summary.Text = "";
            _summary.Font = new Font("Microsoft YaHei UI", 8.5F);
            _summary.ForeColor = Ink3;
            _summary.AutoSize = true;
            _summary.Location = new Point(140, 16);
            head.Controls.Add(_summary);

            // ---------- 工具条 ----------
            var bar = new RoundPanel();
            bar.Radius = 10;
            bar.Dock = DockStyle.Top;
            bar.Height = 42;
            bar.BackColor = Paper;

            _search = new TextBox();
            _search.Size = new Size(220, 26);
            _search.Location = new Point(24, 8);
            _search.Font = new Font("Microsoft YaHei UI", 9F);
            _search.ForeColor = Ink;
            _search.BackColor = Paper2;
            _search.BorderStyle = BorderStyle.FixedSingle;
            _search.TextChanged += delegate { ApplyFilter(); };
            bar.Controls.Add(_search);

            var ph = new Label();
            ph.Text = "搜索提供者 / 类名 / INF";
            ph.Font = new Font("Microsoft YaHei UI", 8F);
            ph.ForeColor = Ink3;
            ph.AutoSize = true;
            ph.Location = new Point(30, 13);
            ph.BackColor = Paper2;
            ph.Click += delegate { _search.Focus(); };
            bar.Controls.Add(ph);
            _search.TextChanged += delegate { ph.Visible = _search.Text.Length == 0; };
            _search.GotFocus += delegate { ph.Visible = false; };
            _search.LostFocus += delegate { ph.Visible = _search.Text.Length == 0; };

            _onlyThird = new CheckBox();
            _onlyThird.Text = "只看第三方";
            _onlyThird.Checked = true;
            _onlyThird.Font = new Font("Microsoft YaHei UI", 8.5F);
            _onlyThird.ForeColor = Ink2;
            _onlyThird.AutoSize = true;
            _onlyThird.Location = new Point(258, 12);
            _onlyThird.CheckedChanged += delegate { ApplyFilter(); };
            bar.Controls.Add(_onlyThird);

            _onlyDeletable = new CheckBox();
            _onlyDeletable.Text = "只看可删除";
            _onlyDeletable.Checked = true;
            _onlyDeletable.Font = new Font("Microsoft YaHei UI", 8.5F);
            _onlyDeletable.ForeColor = Ink2;
            _onlyDeletable.AutoSize = true;
            _onlyDeletable.Location = new Point(446, 12);
            _onlyDeletable.CheckedChanged += delegate { ApplyFilter(); };
            bar.Controls.Add(_onlyDeletable);

            var btnRefresh = new Button();
            btnRefresh.Text = "刷新列表";
            btnRefresh.Size = new Size(96, 28);
            btnRefresh.FlatStyle = FlatStyle.Flat;
            btnRefresh.FlatAppearance.BorderColor = Line;
            btnRefresh.BackColor = Paper2;
            btnRefresh.ForeColor = Ink2;
            btnRefresh.Font = new Font("Microsoft YaHei UI", 8.5F);
            btnRefresh.Location = new Point(556, 8);
            btnRefresh.Click += delegate { RefreshData(); };
            bar.Controls.Add(btnRefresh);

            _btnDelete = new Button();
            _btnDelete.Text = "删除选中项";
            _btnDelete.Size = new Size(120, 28);
            _btnDelete.FlatStyle = FlatStyle.Flat;
            _btnDelete.FlatAppearance.BorderSize = 0;
            _btnDelete.BackColor = Red;
            _btnDelete.ForeColor = Color.White;
            _btnDelete.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            // 不用 Anchor：容器初始宽度(200)会把锚点偏移算错，控件跑出可见区
            _btnDelete.Location = new Point(160, 8);
            _btnDelete.Click += delegate { DoDelete(); };
            bar.Controls.Add(_btnDelete);
            bar.Resize += delegate
            {
                if (bar.Width < 200) return;
                _btnDelete.Location = new Point(bar.Width - 144, 8);
            };

            // ---------- 列表 ----------
            _dgv = new DataGridView();
            _dgv.Dock = DockStyle.Fill;
            _dgv.BackgroundColor = Paper2;
            _dgv.BorderStyle = BorderStyle.None;
            _dgv.GridColor = Line;
            _dgv.RowHeadersVisible = false;
            _dgv.AllowUserToAddRows = false;
            _dgv.AllowUserToDeleteRows = false;
            _dgv.AllowUserToResizeRows = false;
            _dgv.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _dgv.MultiSelect = true;
            _dgv.Font = new Font("Microsoft YaHei UI", 8.5F);
            _dgv.ColumnHeadersDefaultCellStyle.BackColor = Paper;
            _dgv.ColumnHeadersDefaultCellStyle.ForeColor = Ink2;
            _dgv.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            _dgv.ColumnHeadersHeight = 30;
            _dgv.EnableHeadersVisualStyles = false;
            _dgv.RowTemplate.Height = 26;
            _dgv.DefaultCellStyle.BackColor = Paper2;
            _dgv.DefaultCellStyle.ForeColor = Ink2;
            _dgv.DefaultCellStyle.SelectionBackColor = Color.FromArgb(226, 236, 245);
            _dgv.DefaultCellStyle.SelectionForeColor = Ink;
            _dgv.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 246, 238);
            _dgv.CellValueChanged += delegate { UpdateSummary(); };
            _dgv.CurrentCellDirtyStateChanged += delegate
            {
                if (_dgv.IsCurrentCellDirty) _dgv.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };

            var c0 = new DataGridViewCheckBoxColumn();
            c0.HeaderText = "";
            c0.Width = 34;
            c0.Name = "sel";
            _dgv.Columns.Add(c0);

            AddCol("发布名称", "pub", 110, "Consolas");
            AddCol("原始 INF", "orig", 130, "Consolas");
            AddCol("提供者", "prov", 140, null);
            AddCol("类", "cls", 130, null);
            AddCol("版本", "ver", 190, "Consolas");
            AddCol("安装记录", "hist", 190, null);

            var tip = new Label();
            tip.Dock = DockStyle.Bottom;
            tip.Height = 34;
            tip.BackColor = Paper;
            tip.ForeColor = Ink3;
            tip.Font = new Font("Microsoft YaHei UI", 8F);
            tip.Padding = new Padding(24, 6, 24, 0);
            tip.Height = 56;
            tip.Text = "只有通过本工具「下载并安装」的驱动才会被登记，也只有登记过的才能删除。想清理历史驱动，请先去「驱动安装」重装一次让它登记。";

            Controls.Add(_dgv);
            Controls.Add(tip);
            Controls.Add(bar);
            Controls.Add(head);
        }

        private void AddCol(string header, string name, int width, string font)
        {
            var c = new DataGridViewTextBoxColumn();
            c.HeaderText = header;
            c.Name = name;
            c.Width = width;
            c.ReadOnly = true;
            c.Resizable = DataGridViewTriState.True;
            if (font != null) c.DefaultCellStyle.Font = new Font(font, 8.5F);
            _dgv.Columns.Add(c);
        }

        // ---------------- 数据 ----------------

        public void RefreshData()
        {
            Log.Write("[排查] 开始枚举驱动");
            if (_summary != null) { _summary.Text = "正在读取驱动列表…"; _summary.ForeColor = Ink3; }

            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                List<DriverEntry> list;
                string err = null;
                try { list = EnumDrivers(); }
                catch (Exception ex) { list = new List<DriverEntry>(); err = ex.Message; }

                try
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    BeginInvoke((Action)delegate
                    {
                        _all = list;
                        Log.Write("[排查] 驱动枚举完成，共 " + list.Count + " 个驱动包" + (err != null ? "，错误：" + err : ""));
                        if (err != null && _summary != null) _summary.Text = "读取失败：" + err;
                        ApplyFilter();
                    });
                }
                catch { }
            });
        }

        private void ApplyFilter()
        {
            if (_dgv == null) return;

            string q = (_search == null ? "" : _search.Text.Trim());
            bool onlyThird = _onlyThird != null && _onlyThird.Checked;

            bool onlyDel = _onlyDeletable != null && _onlyDeletable.Checked;

            var shown = _all.Where(d =>
                (!onlyThird || !d.IsMicrosoft) &&
                (!onlyDel || InstallHistory.FindByOem(d.PublishedName) != null) &&
                (q.Length == 0 ||
                 (d.Provider ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                 (d.ClassName ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                 (d.PublishedName ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                 (d.OriginalName ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderBy(d => d.Provider).ThenBy(d => d.PublishedName)
                .ToList();

            _dgv.Rows.Clear();
            foreach (var d in shown)
            {
                var hist = InstallHistory.FindByOem(d.PublishedName);
                string histTxt = hist != null
                    ? hist.Time + "  " + (hist.Driver ?? "")
                    : "无安装记录";

                int idx = _dgv.Rows.Add(false, d.PublishedName, d.OriginalName,
                                        d.Provider, d.ClassName, d.Version, histTxt);
                var row = _dgv.Rows[idx];

                if (hist == null)
                {
                    // 没有安装记录 → 禁止勾选
                    row.Cells["sel"].ReadOnly = true;
                    row.Cells["sel"].Value = false;
                    row.DefaultCellStyle.ForeColor = Ink3;
                    row.Cells["hist"].Style.ForeColor = Red;
                }
                else
                {
                    row.Cells["hist"].Style.ForeColor = Green2;
                }
            }
            if (_dgv.Rows.Count == 0)
            {
                _dgv.Rows.Add(false, "（没有可删除的驱动）", "", "", "", "", "");
            }
            UpdateSummary();
        }

        private void UpdateSummary()
        {
            if (_summary == null) return;
            int sel = 0;
            try
            {
                foreach (DataGridViewRow r in _dgv.Rows)
                    if (r.Cells["sel"].Value != null && (bool)r.Cells["sel"].Value) sel++;
            }
            catch { }

            int third = _all.Count(d => !d.IsMicrosoft);
            _summary.Text = "共 " + _all.Count + " 个驱动包（第三方 " + third + " 个）" +
                            (sel > 0 ? "　已选 " + sel : "");
        }

        // ---------------- 删除 ----------------

        private void DoDelete()
        {
            var picked = new List<string>();
            foreach (DataGridViewRow r in _dgv.Rows)
            {
                if (r.Cells["sel"].Value != null && (bool)r.Cells["sel"].Value)
                    picked.Add(Convert.ToString(r.Cells["pub"].Value));
            }

            if (picked.Count == 0)
            {
                MessageBox.Show(this, "没有选中任何驱动。先勾选要删除的项。", "驱动清理",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dlg = new DeleteConfirmForm(picked))
            {
                if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
            }

            int ok = 0, fail = 0;
            var errors = new StringBuilder();

            foreach (var name in picked)
            {
                try
                {
                    var psi = new ProcessStartInfo("pnputil.exe", "/delete-driver " + name + " /uninstall")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = ConsoleEncoding(),
                        StandardErrorEncoding = ConsoleEncoding(),
                    };
                    using (var p = Process.Start(psi))
                    {
                        string o = p.StandardOutput.ReadToEnd();
                        p.WaitForExit(15000);
                        if (p.ExitCode == 0) ok++;
                        else { fail++; errors.AppendLine(name + "：" + FirstLine(o)); }
                    }
                }
                catch (Exception ex)
                {
                    fail++;
                    errors.AppendLine(name + "：" + ex.Message);
                }
            }

            string result = "完成：成功 " + ok + " 个，失败 " + fail + " 个。";
            if (errors.Length > 0) result += "\n\n失败详情：\n" + errors.ToString();

            MessageBox.Show(this, result, "驱动清理", MessageBoxButtons.OK,
                fail > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);

            RefreshData();
        }

        private static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "(无输出)";
            var line = s.Split('\n').Select(x => x.Trim()).FirstOrDefault(x => x.Length > 0);
            return line ?? "(无输出)";
        }

        // ---------------- 解析 ----------------

        private static Encoding ConsoleEncoding()
        {
            try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
            catch { return Encoding.Default; }
        }

        private static List<DriverEntry> EnumDrivers()
        {
            var list = new List<DriverEntry>();

            var psi = new ProcessStartInfo("pnputil.exe", "/enum-drivers")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = ConsoleEncoding(),
            };

            string output;
            using (var p = Process.Start(psi))
            {
                output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(20000);
            }
            if (string.IsNullOrEmpty(output)) return list;

            DriverEntry cur = null;
            foreach (var raw in output.Split('\n'))
            {
                string s = raw.TrimEnd('\r').Trim();
                if (s.Length == 0) continue;

                int colon = s.IndexOf(':');
                if (colon <= 0) continue;

                string label = s.Substring(0, colon).Trim();
                string val = s.Substring(colon + 1).Trim();

                // 发布名称的值形如 oem12.inf —— 按值识别，不依赖语言
                if (Regex.IsMatch(val, @"^oem\d+\.inf$", RegexOptions.IgnoreCase))
                {
                    if (cur != null) list.Add(cur);
                    cur = new DriverEntry { PublishedName = val };
                    continue;
                }
                if (cur == null) continue;

                if (label.IndexOf("提供", StringComparison.Ordinal) >= 0 ||
                    label.IndexOf("Provider", StringComparison.OrdinalIgnoreCase) >= 0)
                    cur.Provider = val;
                else if (label.IndexOf("原始", StringComparison.Ordinal) >= 0 ||
                         label.IndexOf("Original", StringComparison.OrdinalIgnoreCase) >= 0)
                    cur.OriginalName = val;
                else if (label.IndexOf("类名", StringComparison.Ordinal) >= 0 ||
                         label.IndexOf("Class", StringComparison.OrdinalIgnoreCase) >= 0)
                    cur.ClassName = val;
                else if (label.IndexOf("版本", StringComparison.Ordinal) >= 0 ||
                         label.IndexOf("Version", StringComparison.OrdinalIgnoreCase) >= 0)
                    cur.Version = val;
            }
            if (cur != null) list.Add(cur);

            return list;
        }
    }
}
