using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DriverSetup
{
    /// <summary>GPU 烤机面板：D3D 视口 + 温度频率曲线 + 统计</summary>
    public class GpuBurnPanel : Panel
    {
        private static readonly Color Paper = Theme.Bg;
        private static readonly Color Paper2 = Theme.Card;
        private static readonly Color Ink = Theme.Text;
        private static readonly Color Ink2 = Theme.Text2;
        private static readonly Color Ink3 = Theme.Text3;
        private static readonly Color Line = Theme.Line;
        private static readonly Color Orange = Theme.Orange;

        private GpuBurner _burner;
        private PerfSampler _sampler;
        private Timer _timer;
        private Panel _view;
        private ChartView _chart;
        private Label _info;
        private Label _time;
        private Button _btnStart, _btnStop, _btnReset;
        private int _uiTick;
        private TableLayoutPanel _stats;
        private int _sec;

        public GpuBurnPanel(PerfSampler sampler)
        {
            _sampler = sampler;
            BackColor = Paper;
            Font = new Font("Microsoft YaHei UI", 9F);
            BuildUi();

            _burner = new GpuBurner();
            _timer = new Timer();
            _timer.Interval = 1000;
            _timer.Tick += delegate { Tick(); };

            Disposed += delegate
            {
                if (_timer != null) _timer.Stop();
                try { _burner.Stop(); } catch (Exception ex) { Log.Swallow(ex, "GpuBurnPanel:46"); }
            };
        }

        private void BuildUi()
        {
            // ---------- 顶部 ----------
            var head = new Panel();
            head.Dock = DockStyle.Top;
            head.Height = 60;
            head.BackColor = Paper;

            var title = new Label();
            title.Text = "GPU 烤机";
            title.Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold);
            title.ForeColor = Ink;
            title.AutoSize = true;
            title.Location = new Point(24, 12);
            head.Controls.Add(title);

            _time = new Label();
            _time.Text = "00:00";
            _time.Font = new Font("Consolas", 15F, FontStyle.Bold);
            _time.ForeColor = Ink2;
            _time.AutoSize = true;
            _time.Location = new Point(140, 10);
            head.Controls.Add(_time);

            _btnStart = MkButton("开始烤机", Orange, Color.White);
            _btnStart.Location = new Point(240, 12);
            _btnStart.Click += delegate { StartBurn(); };
            head.Controls.Add(_btnStart);

            _btnStop = MkButton("停止", Ink2, Color.White);
            _btnStop.Location = new Point(362, 12);
            _btnStop.Enabled = false;
            _btnStop.Click += delegate { StopBurn(); };
            head.Controls.Add(_btnStop);

            _btnReset = MkButton("重置", Paper2, Ink2);
            _btnReset.Location = new Point(472, 12);
            _btnReset.FlatAppearance.BorderColor = Line;
            _btnReset.FlatAppearance.BorderSize = 1;
            _btnReset.Click += delegate { ResetAll(); };
            head.Controls.Add(_btnReset);

            // ---------- D3D 视口 ----------
            _view = new Panel();
            _view.Dock = DockStyle.Top;
            _view.Height = 240;
            _view.BackColor = Color.Black;

            var viewHost = new Panel();
            viewHost.Dock = DockStyle.Top;
            viewHost.Height = 252;
            viewHost.BackColor = Paper;
            viewHost.Padding = new Padding(24, 0, 24, 12);
            viewHost.Controls.Add(_view);

            // ---------- 曲线 ----------
            _chart = new ChartView();
            _chart.Dock = DockStyle.Fill;
            _chart.Series = new[] { "GpuTemp", "GpuClock" };
            _chart.Hint = "点「开始烤机」后，温度与频率曲线会画在这里";

            var chartHost = new Panel();
            chartHost.Dock = DockStyle.Fill;
            chartHost.Padding = new Padding(24, 4, 24, 6);
            chartHost.BackColor = Paper;
            chartHost.Controls.Add(_chart);

            // ---------- 统计 ----------
            _stats = new TableLayoutPanel();
            _stats.Dock = DockStyle.Bottom;
            _stats.Height = 108;
            _stats.BackColor = Paper;
            _stats.Padding = new Padding(24, 2, 24, 4);
            _stats.ColumnCount = 5;
            _stats.RowCount = 3;
            _stats.ColumnStyles.Clear();
            _stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
            for (int i = 1; i < 5; i++) _stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18.5f));
            _stats.RowStyles.Clear();
            _stats.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            _stats.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            _stats.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

            string[] heads = { "指标", "当前", "平均", "最高", "最低" };
            for (int c = 0; c < heads.Length; c++)
            {
                var l = new Label();
                l.Text = heads[c];
                l.Dock = DockStyle.Fill;
                l.TextAlign = ContentAlignment.MiddleLeft;
                l.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
                l.ForeColor = Ink3;
                _stats.Controls.Add(l, c, 0);
            }
            string[] rows = { "GPU 温度", "GPU 频率" };
            Color[] colors = { ChartView.Orange, ChartView.Teal };
            for (int r = 0; r < rows.Length; r++)
            {
                var name = new Label();
                name.Text = rows[r];
                name.Dock = DockStyle.Fill;
                name.TextAlign = ContentAlignment.MiddleLeft;
                name.ForeColor = colors[r];
                name.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
                _stats.Controls.Add(name, 0, r + 1);

                for (int c = 1; c < 5; c++)
                {
                    var v = new Label();
                    v.Text = "—";
                    v.Dock = DockStyle.Fill;
                    v.TextAlign = ContentAlignment.MiddleLeft;
                    v.ForeColor = Ink2;
                    v.Font = new Font("Consolas", 9.5F);
                    v.Name = "gcell_" + r + "_" + c;
                    _stats.Controls.Add(v, c, r + 1);
                }
            }

            // ---------- 状态行 ----------
            _info = new Label();
            _info.Dock = DockStyle.Bottom;
            _info.Height = 30;
            _info.BackColor = Paper2;
            _info.BorderStyle = BorderStyle.FixedSingle;
            _info.TextAlign = ContentAlignment.MiddleLeft;
            _info.Padding = new Padding(12, 0, 12, 0);
            _info.Font = new Font("Consolas", 9F);
            _info.ForeColor = Ink2;
            _info.Text = "全屏着色器压满显卡；这是纯 GPU 负载，不影响 CPU。";

            Controls.Add(chartHost);
            Controls.Add(_stats);
            Controls.Add(_info);
            Controls.Add(viewHost);
            Controls.Add(head);
        }

        private Button MkButton(string text, Color back, Color fore)
        {
            var b = new Button();
            b.Text = text;
            b.Size = new Size(110, 34);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = back;
            b.ForeColor = fore;
            b.Font = new Font("Microsoft YaHei UI", 9F);
            b.Cursor = Cursors.Hand;
            return b;
        }

        private void StartBurn()
        {
            if (_view.Width < 10 || _view.Height < 10) return;
            _sec = 0;
            _chart.Clear();
            _btnStart.Enabled = false;
            _btnStop.Enabled = true;
            _burner.Start(_view.Handle, _view.ClientSize.Width, _view.ClientSize.Height);
            _timer.Start();
        }

        private void StopBurn()
        {
            _timer.Stop();
            try { _burner.Stop(); } catch (Exception ex) { Log.Swallow(ex, "GpuBurnPanel:216"); }
            _btnStart.Enabled = true;
            _btnStop.Enabled = false;
            if (_chart != null && _chart.Data.Count > 0)
                _info.Text = "已停止。曲线保留，可点「重置」清空。";
            else
                _info.Text = "已停止。";
        }

        private void Tick()
        {
            _sec++;
            _time.Text = TimeSpan.FromSeconds(_sec).ToString(@"mm\:ss");

            if (_sampler == null) return;
            PerfSample p;
            try { p = _sampler.Sample(_sec); }
            catch { return; }

            _chart.Data.Add(p);   // 采样保持每秒，数据不丢

            // 重绘降频：GPU 满载时 GDI+ 重绘会和 D3D 抢资源，
            // 每秒刷一次会让画面发顿。曲线和统计每 2 秒刷一次，状态行照旧每秒更新。
            _uiTick++;
            if (_uiTick % 2 == 0)
            {
                _chart.Invalidate();
                RefreshStats();
            }

            string s = "正在烤机";
            if (_burner.Fps > 0) s += " · " + _burner.Fps.ToString("F0") + " FPS";
            if (!string.IsNullOrEmpty(_burner.Error)) s += " · 错误: " + _burner.Error;
            if (!double.IsNaN(p.GpuLoad)) s += " · 负载 " + p.GpuLoad.ToString("F0") + "%";
            if (!double.IsNaN(p.GpuTemp)) s += " · " + p.GpuTemp.ToString("F0") + "°C";
            if (!double.IsNaN(p.GpuClock)) s += " · " + p.GpuClock.ToString("F0") + "MHz";
            if (!double.IsNaN(p.GpuPower)) s += " · " + p.GpuPower.ToString("F0") + "W";
            _info.Text = s;
        }

        private void RefreshStats()
        {
            if (_stats == null || _chart == null) return;
            Fill(0, _chart.Data.Select(x => x.GpuTemp).ToArray());
            Fill(1, _chart.Data.Select(x => x.GpuClock).ToArray());
        }

        private void Fill(int row, double[] vals)
        {
            var good = vals.Where(v => !double.IsNaN(v) && v > 0).ToArray();
            string[] cells = { "—", "—", "—", "—" };
            if (good.Length > 0)
            {
                cells[0] = good[good.Length - 1].ToString("F0");
                cells[1] = good.Average().ToString("F0");
                cells[2] = good.Max().ToString("F0");
                cells[3] = good.Min().ToString("F0");
            }
            for (int c = 1; c <= 4; c++)
            {
                var l = _stats.Controls["gcell_" + row + "_" + c] as Label;
                if (l != null) l.Text = cells[c - 1];
            }
        }

        /// <summary>延迟注入采样器</summary>
        public void SetSampler(PerfSampler shared)
        {
            _sampler = shared;
        }

        /// <summary>清空计时、曲线与统计（不影响正在进行的烤机）</summary>
        public void ResetAll()
        {
            _sec = 0;
            _time.Text = "00:00";
            if (_chart != null) _chart.Clear();
            RefreshStats();
            if (_btnStart.Enabled) _info.Text = "已重置。点「开始烤机」开始新的记录。";
            else _info.Text = "计时与曲线已清零，烤机仍在进行。";
        }

        public void StopAllBurn() { StopBurn(); }
    }
}
