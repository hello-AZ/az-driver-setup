using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DriverSetup
{
    /// <summary>配置核对页：只读展示本机七项硬件</summary>
    public class ConfigPage : RoundPanel
    {
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

        private const int GapD = 14;

        /// <summary>DPI 缩放系数（96 DPI = 1.0）。动态创建的卡片不受 WinForms 自动缩放影响，需手动乘。</summary>
        private float _scale = -1f;
        private float S
        {
            get
            {
                if (_scale < 0f)
                {
                    using (var g = Graphics.FromHwnd(IntPtr.Zero)) _scale = g.DpiX / 96f;
                }
                return _scale;
            }
        }
        private int Gap { get { return (int)Math.Round(GapD * S); } }

        private Panel _grid;
        private Label _summary;
        private Button _btnRefresh;
        private Label _statusLine;
        private HardwareSnapshot _snap;
        private bool _busy;

        public ConfigPage()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            BuildUi();
        }

        private void BuildUi()
        {
            // ---- 标题区 ----
            var head = new Panel();
            head.Dock = DockStyle.Top;
            head.Height = 42;
            head.BackColor = Paper;

            var sub = new Label();
            sub.Text = "只读采集本机硬件，与购买配置逐项对照。不修改任何设置。";
            sub.Font = new Font("Microsoft YaHei UI", 8.5F);
            sub.ForeColor = Ink3;
            sub.AutoSize = true;
            sub.Location = new Point(0, 11);

            _btnRefresh = new Button();
            _btnRefresh.Text = "重新检测";
            _btnRefresh.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            _btnRefresh.FlatStyle = FlatStyle.Flat;
            _btnRefresh.FlatAppearance.BorderColor = Line;
            _btnRefresh.BackColor = Paper2;
            _btnRefresh.ForeColor = Ink2;
            _btnRefresh.Size = new Size(104, 34);
            _btnRefresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnRefresh.Click += delegate { RefreshData(); };

                        head.Controls.Add(sub);
            head.Controls.Add(_btnRefresh);
            head.Resize += delegate
            {
                _btnRefresh.Location = new Point(head.Width - _btnRefresh.Width - 24, 4);   // 24 = 滚动条 + 呼吸位
            };

            // ---- 汇总条 ----
            var bar = new Panel();
            bar.Dock = DockStyle.Top;
            bar.Height = 42;
            bar.BackColor = Paper2;
            bar.BorderStyle = BorderStyle.FixedSingle;
            bar.Padding = new Padding(16, 0, 16, 0);

            _summary = new Label();
            _summary.Text = "正在采集…";
            _summary.Font = new Font("Microsoft YaHei UI", 9F);
            _summary.ForeColor = Ink2;
            _summary.AutoSize = false;
            _summary.Dock = DockStyle.Fill;
            _summary.TextAlign = ContentAlignment.MiddleLeft;
            bar.Controls.Add(_summary);

            // ---- 状态行 ----
            _statusLine = new Label();
            _statusLine.Dock = DockStyle.Bottom;
            _statusLine.Height = 30;
            _statusLine.Font = new Font("Microsoft YaHei UI", 8F);
            _statusLine.ForeColor = Ink3;
            _statusLine.TextAlign = ContentAlignment.MiddleLeft;

            // ---- 卡片网格 ----
            _grid = new RoundPanel { DrawFill = false, DrawBorder = false };
            _grid.Dock = DockStyle.Fill;
            
            _grid.AutoScroll = true;

            Controls.Add(_grid);
            Controls.Add(_statusLine);
            Controls.Add(bar);
            Controls.Add(head);
        }

        /// <summary>重新采集并刷新</summary>
        public async void RefreshData()
        {
            if (_busy) return;
            _busy = true;
            _btnRefresh.Enabled = false;
            _summary.Text = "正在采集本机硬件…";
            Cursor = Cursors.WaitCursor;

            try
            {
                _snap = await Task.Run((Func<HardwareSnapshot>)(() => HardwareInfo.Collect()));
                Render();
            }
            catch (Exception ex)
            {
                _summary.Text = "采集失败：" + ex.Message;
                _summary.ForeColor = Red;
            }
            finally
            {
                _busy = false;
                _btnRefresh.Enabled = true;
                Cursor = Cursors.Default;
            }
        }

        // ---------------- 渲染 ----------------
        private void Render()
        {
            if (_snap == null || _grid == null) return;

            _grid.SuspendLayout();
            _grid.Controls.Clear();
            _grid.AutoScrollPosition = new Point(0, 0);

            var table = new TableLayoutPanel();
            table.Dock = DockStyle.Top;
            table.AutoSize = true;
            table.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            table.ColumnCount = 2;
            table.Padding = new Padding(0);
            table.Margin = new Padding(0);
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            var cells = new Panel[]
            {
                MakeCpuCard(), MakeGpuCard(),
                MakeMemCard(), MakeDiskCard(),
                MakeMonitorCard(), MakeBatteryCard()
            };

            int row = 0;
            for (int i = 0; i < cells.Length; i += 2)
            {
                table.RowCount = row + 1;
                table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                for (int c = 0; c < 2; c++)
                {
                    var card = cells[i + c];
                    card.Dock = DockStyle.Fill;
                    card.Margin = new Padding(0, 0, (c == 0 ? Gap / 2 : 0), Gap);
                    table.Controls.Add(card, c, row);
                }
                row++;
            }

            // 网卡横跨两列
            table.RowCount = row + 1;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var nic = MakeNicCard();
            nic.Dock = DockStyle.Fill;
            nic.Margin = new Padding(0, 0, 0, 0);
            table.Controls.Add(nic, 0, row);
            table.SetColumnSpan(nic, 2);

            _grid.Controls.Add(table);

            _summary.ForeColor = Ink2;
            _summary.Text = string.Format(
                "7 项硬件 · 内存 {0} GB · 硬盘 {1} 块 · 显示器 {2} 台 · 采集耗时 {3} ms",
                _snap.TotalMemory / 1073741824,
                _snap.Disks.Count,
                _snap.Monitors.Count,
                _snap.ElapsedMs);

            string src = "数据来源：WMI · 注册表 EDID · powercfg 电池报告　|　全部只读";
            if (!InstallEngine.IsAdmin)
                src += "　|　硬盘 SMART 需管理员权限";
            if (_snap.Errors.Count > 0)
                src += "　|　" + _snap.Errors.Count + " 项读取异常";
            _statusLine.Text = src;

            _grid.ResumeLayout();
        }

        // ---------------- 卡片工厂 ----------------
        private Panel NewCard(string title, string en, string badgeText, Color badgeColor, int rows)
        {
            int h = (int)Math.Round((16 + 30 + rows * 27 + 14) * S);

            var card = new RoundPanel();
            card.Height = h;
            card.Radius = (int)(14 * S);
            card.FillColor = Paper2;

            var t = new Label();
            t.Text = title;
            t.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
            t.ForeColor = Ink;
            t.AutoSize = true;
            t.Location = new Point((int)(16 * S), (int)(11 * S));

            if (!string.IsNullOrEmpty(en))
            {
                var e = new Label();
                e.Text = en;
                e.Font = new Font("Microsoft YaHei UI", 7.5F);
                e.ForeColor = Ink3;
                e.AutoSize = true;
                e.Location = new Point((int)(16 * S) + t.PreferredWidth + (int)(8 * S), (int)(16 * S));
                card.Controls.Add(e);
            }

            if (!string.IsNullOrEmpty(badgeText))
            {
                var b = new Label();
                b.Text = badgeText;
                b.Font = new Font("Microsoft YaHei UI", 8F);
                b.ForeColor = badgeColor;
                b.AutoSize = true;
                b.Location = new Point(10, (int)(14 * S));
                b.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                b.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                b.TextAlign = ContentAlignment.MiddleRight;
                b.Size = new Size((int)(96 * S), (int)(18 * S));
                card.Controls.Add(b);
            }

            card.Controls.Add(t);
            return card;
        }

        private void AddRow(Panel card, ref int idx, string key, string value, Color? valueColor)
        {
            var k = new Label();
            k.Text = key;
            k.Font = new Font("Microsoft YaHei UI", 9F);
            k.ForeColor = Ink3;
            k.AutoSize = false;
            k.Size = new Size((int)(84 * S), (int)(24 * S));
            k.Location = new Point((int)(16 * S), (int)((46 + idx * 27) * S));
            k.TextAlign = ContentAlignment.MiddleLeft;

            var v = new Label();
            v.Text = value;
            v.Font = new Font("Microsoft YaHei UI", 9.5F);
            v.ForeColor = valueColor ?? Ink;
            v.AutoSize = false;
            v.Size = new Size(40, (int)(24 * S));
            v.Location = new Point((int)(100 * S), (int)((46 + idx * 27) * S));
            v.TextAlign = ContentAlignment.MiddleLeft;
            v.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            v.AutoEllipsis = true;

            card.Controls.Add(k);
            card.Controls.Add(v);
            idx++;
        }

        // ---------------- 七张卡 ----------------
        private Panel MakeCpuCard()
        {
            var c = NewCard("处理器", "CPU", "", Ink, 2);
            int i = 0;
            AddRow(c, ref i, "型号", _snap.Cpu.Name, null);
            AddRow(c, ref i, "核心 / 线程",
                _snap.Cpu.Cores + " 核 / " + _snap.Cpu.Threads + " 线程", null);
            return c;
        }

        private Panel MakeGpuCard()
        {
            var g = _snap.Gpus.Count > 0 ? _snap.Gpus[0] : null;
            var c = NewCard("显卡", "GPU", "", Ink, 5);
            int i = 0;
            AddRow(c, ref i, "型号", g != null ? g.Name : "未检测到", null);
            AddRow(c, ref i, "显存", g != null ? g.VramText : "—", null);

            GpuDetail.Info spec = null;
            try { spec = GpuDetail.Read(); } catch { }
            if (spec != null && spec.Ok)
            {
                string busTxt = spec.BusWidth > 0
                    ? spec.BusWidth + " bit　" + spec.BandwidthGBs.ToString("F0") + " GB/s"
                    : "—";
                AddRow(c, ref i, "位宽 / 带宽", busTxt, null);
                AddRow(c, ref i, "功耗墙", spec.PowerDefaultW > 0
                    ? spec.PowerDefaultW.ToString("F0") + " W" : "—", null);
            }

            AddRow(c, ref i, "驱动版本", g != null ? g.DriverVersion : "—", null);
            return c;
        }

        private Panel MakeMemCard()
        {
            var c = NewCard("内存", "MEMORY", "", Ink, 3);
            int i = 0;

            long total = _snap.TotalMemory / 1073741824;
            string cnt = _snap.Memory.Count > 0
                ? _snap.Memory.Count + " × " + (_snap.Memory[0].Capacity / 1073741824) + " GB"
                : "";
            AddRow(c, ref i, "总容量", total + " GB　" + cnt, null);

            if (_snap.Memory.Count > 0)
                AddRow(c, ref i, "运行频率", _snap.Memory[0].ConfiguredMHz + " MHz", null);

            string brands = "";
            foreach (var m in _snap.Memory)
            {
                string b = string.IsNullOrEmpty(m.Manufacturer) ? "未知" : m.Manufacturer;
                if (brands.IndexOf(b) < 0) brands += (brands.Length > 0 ? " + " : "") + b;
            }
            AddRow(c, ref i, "颗粒厂商", brands, brands.IndexOf("+") > 0 ? Orange : (Color?)null);
            return c;
        }

        private Panel MakeDiskCard()
        {
            int n = Math.Max(1, _snap.Disks.Count);
            var c = NewCard("硬盘", "STORAGE", "", Ink, n * 2);
            int i = 0;

            if (_snap.Disks.Count == 0)
            {
                AddRow(c, ref i, "状态", "未检测到", Red);
                return c;
            }

            foreach (var d in _snap.Disks)
            {
                AddRow(c, ref i, "型号", d.Model, null);
                string line = d.SizeText;
                if (d.HealthOk) line += "　·　健康";
                else { line += "　·　注意"; }
                if (d.Temperature > 0) line += "　·　" + d.Temperature + "°C";
                if (d.PowerOnHours > 0) line += "　·　" + d.PowerOnHours + " 小时";
                AddRow(c, ref i, "容量", line, d.HealthOk ? (Color?)null : Red);
            }
            return c;
        }

        private Panel MakeMonitorCard()
        {
            int n = Math.Max(1, _snap.Monitors.Count);
            var c = NewCard("屏幕", "DISPLAY", "", Ink, n * 3);
            int i = 0;

            if (_snap.Monitors.Count == 0)
            {
                AddRow(c, ref i, "状态", "未读到 EDID", Red);
                return c;
            }

            foreach (var m in _snap.Monitors)
            {
                string name = string.IsNullOrEmpty(m.Name) ? m.PnpId : m.Name;
                AddRow(c, ref i, "型号", name + (m.Active ? "" : "　·　未激活"), null);

                string line = m.PrefWidth + " × " + m.PrefHeight;
                if (m.PrefRefresh > 0) line += "　" + m.PrefRefresh + " Hz";
                if (m.DiagInch > 0) line += "　" + m.DiagInch.ToString("0.0") + " 英寸";
                AddRow(c, ref i, "规格", line, null);

                string gamut = m.SrgbPct > 0
                    ? "sRGB " + m.SrgbPct.ToString("0.0") + " %　DCI-P3 " + m.DciP3Pct.ToString("0.0") + " %"
                    : "未读到色度坐标";
                AddRow(c, ref i, "色域", gamut, m.SrgbPct >= 99 ? (Color?)null : Orange);
            }
            return c;
        }

        private Panel MakeBatteryCard()
        {
            var b = _snap.Battery;
            var c = NewCard("电池", "BATTERY", "", Ink, 5);
            int i = 0;

            if (!b.Present || b.DesignedMWh <= 0)
            {
                AddRow(c, ref i, "状态", "无电池或读取失败", Red);
                return c;
            }

            AddRow(c, ref i, "设计容量", (b.DesignedMWh / 1000.0).ToString("0.0") + " Wh", null);
            AddRow(c, ref i, "实际容量", (b.FullMWh / 1000.0).ToString("0.0") + " Wh", null);

            double health = b.HealthPct;
            string hl = health.ToString("0.0") + " %　损耗 " + (100 - health).ToString("0.0") + " %";
            AddRow(c, ref i, "健康度", hl, health >= 90 ? (Color?)null : (health >= 80 ? Orange : Red));

            AddRow(c, ref i, "当前电量", b.CurrentPercent >= 0 ? b.CurrentPercent + " %" : "未知", null);
            AddRow(c, ref i, "循环次数", b.Cycles >= 0 ? b.Cycles + " 次" : "读取失败", null);
            return c;
        }

        private Panel MakeNicCard()
        {
            var c = NewCard("网络适配器", "NETWORK", "", Ink, Math.Max(1, _snap.Nics.Count));
            int i = 0;

            if (_snap.Nics.Count == 0)
            {
                AddRow(c, ref i, "状态", "未检测到物理网卡", Red);
                return c;
            }

            foreach (var n in _snap.Nics)
            {
                string label = n.Name.IndexOf("Wi-Fi", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               n.Name.IndexOf("Wireless", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               n.Name.IndexOf("WLAN", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               n.Name.IndexOf("8852", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "无线" : "有线";
                AddRow(c, ref i, label, n.Name + (n.Connected ? "　·　已连接" : ""), null);
            }
            return c;
        }
    }
}
