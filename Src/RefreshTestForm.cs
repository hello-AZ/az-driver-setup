using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DriverSetup
{
    /// <summary>
    /// 刷新率测试：方块高速横移，用帧间隔推算真实刷新率。
    /// Win11 的 DWM 有垂直同步，重绘会被同步到显示器刷新，所以能测出来。
    /// </summary>
    public class RefreshTestForm : Form
    {
        private readonly Screen _screen;
        private volatile bool _running;
        private float _x;
        private double _fps;
        private double _intervalMs;
        private double _minMs = double.MaxValue;
        private double _maxMs;
        private long _frames;
        private readonly Stopwatch _sw = Stopwatch.StartNew();

        private int _nominal;

        public RefreshTestForm() : this(null) { }

        public RefreshTestForm(Screen target)
        {
            if (Program.AppIcon != null) Icon = Program.AppIcon;
            _screen = target ?? Screen.PrimaryScreen;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = _screen.Bounds;
            TopMost = true;
            BackColor = Color.Black;
            DoubleBuffered = true;
            KeyPreview = true;
            Cursor.Hide();
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);

            try
            {
                // 用当前显示模式的刷新率做参照
                var dm = new DEVMODE();
                dm.dmSize = (short)System.Runtime.InteropServices.Marshal.SizeOf(typeof(DEVMODE));
                if (EnumDisplaySettings(_screen.DeviceName, -1, ref dm))
                    _nominal = dm.dmDisplayFrequency;
            }
            catch (Exception ex) { Log.Swallow(ex, "RefreshTestForm:53"); }

            KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Space) Close();
            };
            MouseClick += delegate { Close(); };

            Shown += delegate { RunLoop(); };
        }

        private void RunLoop()
        {
            _running = true;
            var last = _sw.Elapsed.TotalMilliseconds;
            double fpsWindow = last;
            long fpsFrames = 0;
            int w = ClientSize.Width;

            while (_running && !IsDisposed)
            {
                Application.DoEvents();

                double now = _sw.Elapsed.TotalMilliseconds;
                double dt = now - last;
                last = now;

                if (dt > 0 && dt < 500)
                {
                    _minMs = Math.Min(_minMs, dt);
                    _maxMs = Math.Max(_maxMs, dt);
                    _intervalMs = _intervalMs * 0.85 + dt * 0.15;
                }

                _x += (float)(Math.Max(6.0, w / 62.0));
                if (_x > w) _x = -90;

                _frames++;
                fpsFrames++;
                if (now - fpsWindow >= 700)
                {
                    _fps = fpsFrames * 1000.0 / (now - fpsWindow);
                    fpsFrames = 0;
                    fpsWindow = now;
                }

                Invalidate();
                Update();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.Black);
            g.SmoothingMode = SmoothingMode.None;

            int w = ClientSize.Width, h = ClientSize.Height;

            // 竖条纹背景：拖影一眼可见
            using (var p = new Pen(Color.FromArgb(26, 26, 30), 2))
            {
                for (int x = 0; x < w; x += 24) g.DrawLine(p, x, 0, x, h);
            }

            // 移动方块
            var box = new Rectangle((int)_x, h / 2 - 55, 90, 110);
            using (var b = new SolidBrush(Color.White)) g.FillRectangle(b, box);

            // 数据
            using (var f1 = new Font("Consolas", 30F, FontStyle.Bold))
            using (var f2 = new Font("Consolas", 12F))
            using (var b1 = new SolidBrush(Color.White))
            using (var b2 = new SolidBrush(Color.FromArgb(170, 170, 170)))
            {
                g.DrawString(_fps.ToString("F1") + " FPS", f1, b1, 40, 36);
                g.DrawString("显示器标称 " + (_nominal > 0 ? _nominal + " Hz" : "未知"), f2, b2, 44, 92);
                g.DrawString("帧间隔 平均 " + _intervalMs.ToString("F2") + " ms   最小 " +
                             (_minMs < double.MaxValue ? _minMs.ToString("F2") : "—") +
                             " ms   最大 " + _maxMs.ToString("F2") + " ms", f2, b2, 44, 116);
                g.DrawString("累计 " + _frames + " 帧", f2, b2, 44, 140);

                string verdict;
                if (_fps > 0 && _nominal > 0)
                {
                    double d = Math.Abs(_fps - _nominal);
                    if (d < _nominal * 0.06) verdict = "✓ 接近标称，刷新率正常";
                    else if (_fps < _nominal * 0.9) verdict = "⚠ 低于标称，可能被降频或线材带宽不足";
                    else verdict = "⚠ 高于标称，测量可能未受垂直同步约束";
                }
                else verdict = "测量中…";

                using (var b3 = new SolidBrush(Color.FromArgb(120, 220, 150)))
                    g.DrawString(verdict, f2, b3, 44, 172);
            }

            using (var f = new Font("Microsoft YaHei UI", 10F))
            using (var b = new SolidBrush(Color.FromArgb(130, 130, 130)))
                g.DrawString("Esc / 空格 / 点击 退出", f, b, w - 220, h - 40);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _running = false;
            try { Cursor.Show(); } catch (Exception ex) { Log.Swallow(ex, "RefreshTestForm:157"); }
            base.OnFormClosing(e);
        }

        // --- 取显示器当前模式 ---
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential,
            CharSet = System.Runtime.InteropServices.CharSet.Ansi)]
        private struct DEVMODE
        {
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Ansi)]
        private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);
    }
}
