using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace DriverSetup
{
    /// <summary>烤机与温度监控窗口</summary>
    public class BurnPanel : Panel
    {
        // ---- 纸本配色 ----
        private static readonly Color Paper = Theme.Bg;
        private static readonly Color Paper2 = Theme.Card;
        private static readonly Color Ink = Theme.Text;
        private static readonly Color Ink2 = Theme.Text2;
        private static readonly Color Ink3 = Theme.Text3;
        private static readonly Color Line = Theme.Line;
        private static readonly Color Red = Theme.Red;
        private static readonly Color Orange = Theme.Orange;
        private static readonly Color Blue = Theme.Accent;
        private static readonly Color Teal = Theme.Teal;

        private PerfSampler _sampler;
        private CpuBurner _burner;
        private Timer _timer;
        
        private double _sec;
        private bool _running;
        private int _hover = -1;

        /// <summary>显示后立即开始烤机</summary>
        public bool AutoStartNow { get; set; }

        /// <summary>显示后自动打开 GPU 烤机</summary>
        public bool AutoStartGpu { get; set; }

        private ChartView _chart;
        private Button _btnStart, _btnStop, _btnClear, _btnGpu;
        private Label _lblTime, _lblState;
        private TableLayoutPanel _stats;
        private Label[] _sensorVals;



        private bool _ownsSampler;

        public BurnPanel(PerfSampler shared)
        {
            BackColor = Paper;
            Font = new Font("Microsoft YaHei UI", 9F);

            BuildUi();

            _burner = new CpuBurner();
            try
            {
                if (shared != null) { _sampler = shared; _ownsSampler = false; _sampler.Probe(); }
                else { _sampler = null; _ownsSampler = false; }   // 等 SetSampler（避免拖慢启动）
            }
            catch (Exception ex)
            {
                _sampler = null;
                _lblState.Text = "采样器初始化失败：" + ex.Message;
            }

            _timer = new Timer();
            _timer.Interval = 1000;
            _timer.Tick += delegate { Tick(); };

            UpdateStateText();
            Disposed += delegate { StopBurn(); if (_timer != null) _timer.Dispose(); if (_ownsSampler && _sampler != null) _sampler.Dispose(); };
        }

        private void BuildUi()
        {
            // ---------- 顶部操作条 ----------
            var head = new Panel();
            head.Dock = DockStyle.Top;
            head.Height = 64;
            head.BackColor = Paper;

            var title = new Label();
            title.Text = "烤机与温度监控";
            title.Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold);
            title.ForeColor = Ink;
            title.AutoSize = true;
            title.Location = new Point(22, 12);
            head.Controls.Add(title);

            _lblTime = new Label();
            _lblTime.Text = "00:00";
            _lblTime.Font = new Font("Consolas", 15F, FontStyle.Bold);
            _lblTime.ForeColor = Ink2;
            _lblTime.AutoSize = true;
            _lblTime.Location = new Point(240, 10);
            head.Controls.Add(_lblTime);

            _btnStart = MkButton("开始烤机", Red, Color.White);
            _btnStart.Location = new Point(336, 12);
            _btnStart.Click += delegate { StartBurn(); };
            head.Controls.Add(_btnStart);

            _btnStop = MkButton("停止", Ink2, Color.White);
            _btnStop.Location = new Point(458, 12);
            _btnStop.Enabled = false;
            _btnStop.Click += delegate { StopBurn(); };
            head.Controls.Add(_btnStop);

            _btnClear = MkButton("清空曲线", Paper2, Ink2);
            _btnClear.Location = new Point(580, 12);
            _btnClear.FlatAppearance.BorderColor = Line;
            _btnClear.Click += delegate { _chart.Clear(); _sec = 0; _hover = -1; _lblTime.Text = "00:00"; _chart.Invalidate(); RefreshStats(); };
            head.Controls.Add(_btnClear);

            // ---------- 状态行 ----------
            _lblState = new Label();
            _lblState.Dock = DockStyle.Top;
            _lblState.Height = 34;
            _lblState.BackColor = Paper;
            _lblState.ForeColor = Ink3;
            _lblState.Font = new Font("Microsoft YaHei UI", 8.5F);
            _lblState.Padding = new Padding(24, 4, 24, 0);
            _lblState.TextAlign = ContentAlignment.MiddleLeft;

            // ---------- 传感器行 ----------
            var sensors = new Panel();
            sensors.Dock = DockStyle.Top;
            sensors.Height = 60;
            sensors.BackColor = Paper;
            sensors.Padding = new Padding(24, 6, 24, 6);

            string[] caps = { "CPU 频率", "CPU 功耗", "CPU 电压", "GPU 功耗" };
            _sensorVals = new Label[caps.Length];
            for (int i = 0; i < caps.Length; i++)
            {
                var box = new Panel();
                box.Size = new Size(158, 46);
                box.Location = new Point(24 + i * 170, 6);
                box.BackColor = Paper2;
                box.BorderStyle = BorderStyle.FixedSingle;
                sensors.Controls.Add(box);

                var cap = new Label();
                cap.Text = caps[i];
                cap.Font = new Font("Microsoft YaHei UI", 7.5F);
                cap.ForeColor = Ink3;
                cap.AutoSize = true;
                cap.Location = new Point(9, 5);
                box.Controls.Add(cap);

                var val = new Label();
                val.Text = "—";
                val.Font = new Font("Consolas", 12F, FontStyle.Bold);
                val.ForeColor = Ink2;
                val.AutoSize = true;
                val.Location = new Point(9, 19);
                box.Controls.Add(val);
                _sensorVals[i] = val;
            }

            // ---------- 曲线 ----------
            _chart = new ChartView();
            _chart.Dock = DockStyle.Fill;
            _chart.Margin = new Padding(0);
            _chart.Series = new[] { "CpuTemp", "GpuTemp", "CpuClock", "GpuClock" };
            _chart.Hint = "按「开始烤机」后曲线会出现在这里";

            var chartHost = new Panel();
            chartHost.Dock = DockStyle.Fill;
            chartHost.Padding = new Padding(22, 6, 22, 6);
            chartHost.BackColor = Paper;
            chartHost.Controls.Add(_chart);

            // ---------- 统计表 ----------
            _stats = new TableLayoutPanel();
            _stats.Dock = DockStyle.Bottom;
            _stats.Height = 150;
            _stats.BackColor = Paper;
            _stats.Padding = new Padding(24, 4, 24, 6);
            _stats.ColumnCount = 5;
            _stats.RowCount = 5;
            _stats.ColumnStyles.Clear();
            _stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
            for (int i = 1; i < 5; i++) _stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18.5f));
            _stats.RowStyles.Clear();
            _stats.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            for (int i = 1; i < 5; i++) _stats.RowStyles.Add(new RowStyle(SizeType.Percent, 25));

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
            string[] rows = { "CPU 温度", "CPU 频率", "GPU 温度", "GPU 频率" };
            Color[] colors = { Red, Blue, Orange, Teal };
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
                    v.Name = "cell_" + r + "_" + c;
                    _stats.Controls.Add(v, c, r + 1);
                }
            }

            Controls.Add(chartHost);
            Controls.Add(_stats);
            Controls.Add(sensors);
            Controls.Add(_lblState);
            Controls.Add(head);
        }

        private Button MkButton(string text, Color back, Color fore)
        {
            var b = new Button();
            b.Text = text;
            b.Size = new Size(106, 32);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = back == Paper2 ? 1 : 0;
            b.BackColor = back;
            b.ForeColor = fore;
            b.Font = new Font("Microsoft YaHei UI", 9F);
            b.Cursor = Cursors.Hand;
            return b;
        }

        private void UpdateStateText()
        {
            if (_sampler == null) return;
            string s = "CPU：" + _sampler.CpuName + "   ·   GPU：" + (_sampler.GpuOk ? _sampler.GpuName : "未检测到");
            s += "   ·   权限：" + (_sampler.IsAdmin ? "管理员" : "普通用户（CPU 温度不可读）");
            s += "   ·   风扇由 EC 管理，Windows 侧读不到";
            // 没采样过不等于没权限：这时什么都不提
            if (_sampler.HasSampled && !_sampler.CpuTempAvailable)
                s += "   ·   读不到 CPU 温度（需要以管理员身份运行）";
            else if (!_sampler.HasSampled)
                s += "   ·   按「开始烤机」后读取传感器";
            _lblState.Text = s;
        }

        private void StartBurn()
        {
            if (_sampler == null) { Log.Write("[CPU烤机] 没有采样器，无法开始"); return; }
            Log.Write("[CPU烤机] 开始");
            _chart.Clear();
            _sec = 0;
            _running = true;
            _btnStart.Enabled = false;
            _btnStop.Enabled = true;

            int n = Environment.ProcessorCount;
            _burner.Start(n);
            _timer.Start();
            _chart.Invalidate();
        }

        /// <summary>延迟注入采样器（切到本页时才初始化 LHM）</summary>
        /// <summary>外部提示（例如传感器还在初始化）</summary>
        public void ShowLoading(string text)
        {
            if (_lblState != null) _lblState.Text = text;
        }

        public void SetSampler(PerfSampler shared)
        {
            if (shared == null) return;
            if (_ownsSampler && _sampler != null) { try { _sampler.Dispose(); } catch { } }
            _sampler = shared;
            _ownsSampler = false;
            // 不在这里 Probe：_cpu.Update() 要读 MSR，会卡 UI 线程。
            // 第一次 Sample 时自然填充，状态行随之更新。
            UpdateStateText();
        }

        /// <summary>外部调用：停止全部负载</summary>
        public void StopAllBurn()
        {
            StopBurn();
            try { if (_burner != null) _burner.Stop(); } catch { }
        }

        private void StopBurn()
        {
            Log.Write("[CPU烤机] 停止");
            _running = false;
            try { _burner.Stop(); } catch { }
            if (_timer != null) _timer.Stop();
            if (_btnStart != null) _btnStart.Enabled = true;
            if (_btnStop != null) _btnStop.Enabled = false;
        }

        private void Tick()
        {
            UpdateStateText();   // 采样后同步状态行（GPU/温度可读性会变）

            if (_sampler == null) return;
            _sec += 1.0;
            var p = _sampler.Sample(_sec);
            _chart.Data.Add(p);

            _lblTime.Text = TimeSpan.FromSeconds(_sec).ToString(@"mm\:ss");
            UpdateSensors(p);
            _chart.Invalidate();
            RefreshStats();
        }

        private void UpdateSensors(PerfSample p)
        {
            if (_sensorVals == null) return;
            if (!double.IsNaN(p.CoreMhzAvg))
            {
                // 平均 / 最低：平均值看整体，最低值暴露降频
                _sensorVals[0].Text = (p.CoreMhzAvg / 1000.0).ToString("F2") + " / " +
                                      (p.CoreMhzMin / 1000.0).ToString("F2");
                _sensorVals[0].ForeColor = Ink;
            }
            else
            {
                _sensorVals[0].Text = "—";
                _sensorVals[0].ForeColor = Ink3;
            }
            _sensorVals[1].Text = double.IsNaN(p.CpuPower) ? "—" : p.CpuPower.ToString("F0") + " W";
            _sensorVals[2].Text = double.IsNaN(p.CpuVoltage) ? "—" : p.CpuVoltage.ToString("F2") + " V";
            _sensorVals[3].Text = double.IsNaN(p.GpuPower) ? "—" : p.GpuPower.ToString("F0") + " W";
        }

        private void RefreshStats()
        {
            if (_stats == null) return;

            double[] cpuT = _chart.Data.Select(x => x.CpuTemp).ToArray();
            double[] cpuF = _chart.Data.Select(x => x.CpuClock).ToArray();
            double[] gpuT = _chart.Data.Select(x => x.GpuTemp).ToArray();
            double[] gpuF = _chart.Data.Select(x => x.GpuClock).ToArray();

            Fill(0, cpuT, 0);
            Fill(1, cpuF, 0);
            Fill(2, gpuT, 0);
            Fill(3, gpuF, 0);
        }

        private void Fill(int row, double[] vals, int decimals)
        {
            var good = vals.Where(v => !double.IsNaN(v) && v > 0).ToArray();
            string cur = "—", avg = "—", max = "—", min = "—";
            if (good.Length > 0)
            {
                string fmt = "F0";
                cur = good[good.Length - 1].ToString(fmt);
                avg = good.Average().ToString(fmt);
                max = good.Max().ToString(fmt);
                min = good.Min().ToString(fmt);
            }
            string[] cells = { cur, avg, max, min };
            for (int c = 1; c <= 4; c++)
            {
                var l = _stats.Controls["cell_" + row + "_" + c] as Label;
                if (l != null) l.Text = cells[c - 1];
            }
        }

        // ---------------- 曲线绘制 ----------------
        private const double TempMax = 100.0;
        private const double TempMin = 20.0;












    }
}
