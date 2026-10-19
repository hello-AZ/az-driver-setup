using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace DriverSetup
{
    /// <summary>
    /// 通用双轴曲线：左轴温度，右轴频率。
    /// CPU 烤机与 GPU 烤机共用这一套绘制逻辑。
    /// </summary>
    public class ChartView : Panel
    {
        private static readonly Color Paper2 = Theme.Card;
        private static readonly Color Ink = Theme.Text;
        private static readonly Color Ink2 = Theme.Text2;
        private static readonly Color Ink3 = Theme.Text3;
        private static readonly Color Line = Theme.Line;

        public static readonly Color Red = Theme.Red;
        public static readonly Color Orange = Theme.Orange;
        public static readonly Color Blue = Theme.Accent;
        public static readonly Color Teal = Theme.Teal;

        private const double TempMin = 20.0;
        private const double TempMax = 100.0;

        public List<PerfSample> Data = new List<PerfSample>();
        public string[] Series = { "CpuTemp", "GpuTemp", "CpuClock", "GpuClock" };
        public string Hint = "按「开始烤机」后曲线会出现在这里";

        private int _hover = -1;

        public ChartView()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Paper2;

            MouseMove += ChartMouseMove;
            MouseLeave += delegate { _hover = -1; Invalidate(); };
        }

        public void SetSeries(params string[] series)
        {
            Series = series;
            Invalidate();
        }

        public void Clear()
        {
            Data.Clear();
            _hover = -1;
            Invalidate();
        }

        // ---------------- 颜色与取值 ----------------

        public static Color ColorOf(string key)
        {
            switch (key)
            {
                case "CpuTemp": return Red;
                case "GpuTemp": return Orange;
                case "CpuClock": return Blue;
                case "GpuClock": return Teal;
                default: return Ink2;
            }
        }

        public static string LabelOf(string key)
        {
            switch (key)
            {
                case "CpuTemp": return "CPU 温度";
                case "GpuTemp": return "GPU 温度";
                case "CpuClock": return "CPU 频率";
                case "GpuClock": return "GPU 频率";
                default: return key;
            }
        }

        public static double ValueOf(PerfSample p, string key)
        {
            switch (key)
            {
                case "CpuTemp": return p.CpuTemp;
                case "GpuTemp": return p.GpuTemp;
                case "CpuClock": return p.CpuClock;
                case "GpuClock": return p.GpuClock;
                default: return double.NaN;
            }
        }

        private static bool IsTemp(string key)
        {
            return key.EndsWith("Temp", StringComparison.Ordinal);
        }

        // ---------------- 绘制 ----------------

        private double FreqMax()
        {
            double m = 3000;
            foreach (var p in Data)
            {
                foreach (var s in Series)
                {
                    if (IsTemp(s)) continue;
                    double v = ValueOf(p, s);
                    if (!double.IsNaN(v) && v > m) m = v;
                }
            }
            return Math.Ceiling(m / 1000.0) * 1000.0 + 500;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Paper2);

            int padL = 54, padR = 62, padT = 30, padB = 34;
            int w = ClientSize.Width, h = ClientSize.Height;
            var plot = new Rectangle(padL, padT, Math.Max(10, w - padL - padR), Math.Max(10, h - padT - padB));

            double fMax = FreqMax();
            bool hasFreq = Series.Any(s => !IsTemp(s));

            using (var pen = new Pen(Line, 1))
            using (var br = new SolidBrush(Ink3))
            using (var f = new Font("Consolas", 7.5F))
            {
                for (int i = 0; i <= 4; i++)
                {
                    int y = plot.Bottom - plot.Height * i / 4;
                    g.DrawLine(pen, plot.Left, y, plot.Right, y);

                    double tv = TempMin + (TempMax - TempMin) * i / 4;
                    g.DrawString(tv.ToString("F0"), f, br, 8, y - 7);

                    if (hasFreq)
                    {
                        double fv = fMax * i / 4;
                        string s2 = fv >= 1000 ? (fv / 1000).ToString("F1") + "G" : fv.ToString("F0");
                        g.DrawString(s2, f, br, plot.Right + 6, y - 7);
                    }
                }
                g.DrawRectangle(pen, plot);
                g.DrawString("°C", new Font("Consolas", 7.5F), br, 8, padT - 16);
                if (hasFreq) g.DrawString("MHz", new Font("Consolas", 7.5F), br, plot.Right + 6, padT - 16);
            }

            if (Data.Count < 2)
            {
                using (var br2 = new SolidBrush(Ink3))
                using (var f2 = new Font("Microsoft YaHei UI", 10F))
                {
                    string tip = Data.Count == 0 ? Hint : "正在采集…";
                    var sz = g.MeasureString(tip, f2);
                    g.DrawString(tip, f2, br2, plot.Left + (plot.Width - sz.Width) / 2, plot.Top + plot.Height / 2 - 10);
                }
                return;
            }

            double t0 = Data[0].Sec;
            double t1 = Data[Data.Count - 1].Sec;
            if (t1 - t0 < 30) t1 = t0 + 30;

            Func<double, float> mapX = delegate (double sec)
            {
                return (float)(plot.Left + plot.Width * (sec - t0) / (t1 - t0));
            };
            Func<double, float> mapTempY = delegate (double v)
            {
                double c = Math.Max(TempMin, Math.Min(TempMax, v));
                return (float)(plot.Bottom - plot.Height * (c - TempMin) / (TempMax - TempMin));
            };
            Func<double, float> mapFreqY = delegate (double v)
            {
                double c = Math.Max(0, Math.Min(fMax, v));
                return (float)(plot.Bottom - plot.Height * c / fMax);
            };

            foreach (var key in Series)
            {
                var vals = Data.Select(x => ValueOf(x, key)).ToArray();
                var mapY = IsTemp(key) ? mapTempY : mapFreqY;
                DrawSeries(g, vals, mapX, mapY, ColorOf(key), IsTemp(key) ? 2f : 1.6f);
            }

            DrawLegend(g, plot);

            // ---- 悬停 ----
            if (_hover >= 0 && _hover < Data.Count)
            {
                var p = Data[_hover];
                float hx = mapX(p.Sec);

                using (var pen = new Pen(Color.FromArgb(120, Ink), 1))
                {
                    pen.DashStyle = DashStyle.Dash;
                    g.DrawLine(pen, hx, plot.Top, hx, plot.Bottom);
                }

                foreach (var key in Series)
                {
                    double v = ValueOf(p, key);
                    if (double.IsNaN(v) || v <= 0) continue;
                    float y = (IsTemp(key) ? mapTempY : mapFreqY)(v);
                    using (var b = new SolidBrush(ColorOf(key)))
                        g.FillEllipse(b, hx - 3.5f, y - 3.5f, 7, 7);
                }

                // 信息板
                var lines = new List<string>();
                lines.Add("时间  " + TimeSpan.FromSeconds(p.Sec).ToString(@"mm\:ss"));
                lines.Add("CPU   " + Fmt(p.CpuTemp, "°C") + "   " + Fmt(p.CpuClock, "MHz") + "   " + Fmt(p.CpuLoad, "%"));
                lines.Add("GPU   " + Fmt(p.GpuTemp, "°C") + "   " + Fmt(p.GpuClock, "MHz") + "   " + Fmt(p.GpuPower, "W"));

                float bw = 258, bh = 62;
                float bx = Math.Min(hx + 12, plot.Right - bw - 4);
                float by = plot.Top + 8;

                using (var bgb = new SolidBrush(Color.FromArgb(238, Paper2)))
                    g.FillRectangle(bgb, bx, by, bw, bh);
                using (var bd = new Pen(Line))
                    g.DrawRectangle(bd, bx, by, bw, bh);
                using (var f3 = new Font("Consolas", 8.5F))
                using (var b3 = new SolidBrush(Ink))
                {
                    for (int i = 0; i < lines.Count; i++)
                        g.DrawString(lines[i], f3, b3, bx + 8, by + 6 + i * 18);
                }
            }
        }

        private static string Fmt(double v, string unit)
        {
            if (double.IsNaN(v) || v <= 0) return "—";
            return v.ToString("F0") + unit;
        }

        private void DrawSeries(Graphics g, double[] vals,
                                Func<double, float> mapX, Func<double, float> mapY, Color color, float width)
        {
            using (var pen = new Pen(color, width))
            {
                pen.LineJoin = LineJoin.Round;
                PointF? prev = null;
                for (int i = 0; i < vals.Length; i++)
                {
                    double v = vals[i];
                    if (double.IsNaN(v) || v <= 0) { prev = null; continue; }
                    var pt = new PointF(mapX(Data[i].Sec), mapY(v));
                    if (prev.HasValue) g.DrawLine(pen, prev.Value, pt);
                    prev = pt;
                }
            }
        }

        private void DrawLegend(Graphics g, Rectangle plot)
        {
            using (var f = new Font("Microsoft YaHei UI", 8F))
            {
                float x = plot.Left;
                foreach (var key in Series)
                {
                    using (var pen = new Pen(ColorOf(key), 2.4f))
                        g.DrawLine(pen, x, plot.Top - 14, x + 16, plot.Top - 14);
                    using (var br = new SolidBrush(Ink2))
                        g.DrawString(LabelOf(key), f, br, x + 21, plot.Top - 22);
                    x += 84;
                }
            }
        }

        private void ChartMouseMove(object sender, MouseEventArgs e)
        {
            if (Data.Count < 2) return;

            int padL = 54, padR = 62;
            int w = ClientSize.Width;
            int plotL = padL, plotR = w - padR;
            int plotW = Math.Max(10, plotR - plotL);

            double t0 = Data[0].Sec;
            double t1 = Data[Data.Count - 1].Sec;
            if (t1 - t0 < 30) t1 = t0 + 30;

            double sec = t0 + (e.X - plotL) * (t1 - t0) / plotW;

            int best = -1;
            double bestD = double.MaxValue;
            for (int i = 0; i < Data.Count; i++)
            {
                double d = Math.Abs(Data[i].Sec - sec);
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best != _hover)
            {
                _hover = best;
                Invalidate();
            }
        }
    }
}
