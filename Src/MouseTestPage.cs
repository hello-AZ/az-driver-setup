using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DriverSetup
{
    /// <summary>鼠标测试：按键计数、回报率、双击故障检测、移动轨迹</summary>
    public class MouseTestPage : Panel
    {
        private static readonly Color Paper = Theme.Bg;
        private static readonly Color Paper2 = Theme.Card;
        private static readonly Color Ink = Theme.Text;
        private static readonly Color Ink2 = Theme.Text2;
        private static readonly Color Ink3 = Theme.Text3;
        private static readonly Color Line = Theme.Line;
        private static readonly Color Green = Theme.Green;
        private static readonly Color Red = Theme.Red;
        private static readonly Color Blue = Theme.Accent;

        // ---- Raw Input ----
        private const int WM_INPUT = 0x00FF;
        private const uint RID_INPUT = 0x10000003;
        private const uint RIDEV_INPUTSINK = 0x00000100;
        private const uint HID_USAGE_PAGE_GENERIC = 0x01;
        private const uint HID_USAGE_GENERIC_MOUSE = 0x02;

        private const ushort RI_MOUSE_LEFT_BUTTON_DOWN = 0x0001;
        private const ushort RI_MOUSE_LEFT_BUTTON_UP = 0x0002;
        private const ushort RI_MOUSE_RIGHT_BUTTON_DOWN = 0x0004;
        private const ushort RI_MOUSE_RIGHT_BUTTON_UP = 0x0008;
        private const ushort RI_MOUSE_MIDDLE_BUTTON_DOWN = 0x0010;
        private const ushort RI_MOUSE_MIDDLE_BUTTON_UP = 0x0020;
        private const ushort RI_MOUSE_BUTTON_4_DOWN = 0x0040;
        private const ushort RI_MOUSE_BUTTON_4_UP = 0x0080;
        private const ushort RI_MOUSE_BUTTON_5_DOWN = 0x0100;
        private const ushort RI_MOUSE_BUTTON_5_UP = 0x0200;
        private const ushort RI_MOUSE_WHEEL = 0x0400;

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICE
        {
            public ushort UsagePage;
            public ushort Usage;
            public uint Flags;
            public IntPtr Target;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTHEADER
        {
            public uint Type;
            public uint Size;
            public IntPtr Device;
            public IntPtr WParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWMOUSE
        {
            public ushort usFlags;
            public ushort usButtonFlags;
            public ushort usButtonData;
            public uint ulRawButtons;
            public int lLastX;
            public int lLastY;
            public uint ulExtraInformation;
        }

        [DllImport("user32.dll")]
        private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);

        [DllImport("user32.dll")]
        private static extern uint GetRawInputData(IntPtr hRawInput, uint command,
            IntPtr pData, ref uint size, uint headerSize);

        // ---- 状态 ----
        private int[] _counts = new int[5];              // 左 右 中 侧1 侧2
        private bool[] _down = new bool[5];
        private long _moves;
        private long _pixels;
        private readonly List<double> _clickTimes = new List<double>();   // 左键按下时刻
        private int _doubleClickSuspects;
        private double _lastLeftDown = -1;
        private readonly List<int> _intervals = new List<int>();          // 最近间隔（ms）
        private readonly List<PointF> _trail = new List<PointF>();
        private PointF _cursor;
        private readonly IntPtr _buf = Marshal.AllocHGlobal(512);   // 复用缓冲，避免每个事件分配
        private float _accX, _accY;

        private Label _lblRate, _lblMoves, _lblDbl;
        private KeyCard[] _cards = new KeyCard[5];
        private TrailPanel _trailPanel;
        private Timer _timer;
        private long _lastMoves;
        private int _rate;
        private readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();

        private class KeyCard : Panel
        {
            public Label Count;
            public string Name;

            public KeyCard(string name)
            {
                Name = name;
                BackColor = Paper2;
                BorderStyle = BorderStyle.FixedSingle;
            }

            public void SetActive(bool on, bool tested)
            {
                if (on) BackColor = Blue;
                else if (tested) BackColor = Color.FromArgb(224, 240, 229);
                else BackColor = Paper2;
                Invalidate();
            }
        }

        private class TrailPanel : Panel
        {
            public List<PointF> Points;
            public PointF Cursor;
            public TrailPanel()
            {
                DoubleBuffered = true;
                BackColor = Paper2;
                BorderStyle = BorderStyle.FixedSingle;
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                if (Points == null || Points.Count < 2) return;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

                // 1:1 比例，最后一个点钉在画面中心：轨迹严格跟着手走，不做缩放
                float scale = 1f;
                var tail = Points[Points.Count - 1];
                float ox = Width / 2f - tail.X;
                float oy = Height / 2f - tail.Y;

                if (Points.Count > 1)
                {
                    var arr = new PointF[Points.Count];
                    for (int i = 0; i < Points.Count; i++)
                        arr[i] = new PointF(Points[i].X * scale + ox, Points[i].Y * scale + oy);
                    using (var pen = new Pen(Color.FromArgb(130, Blue), 1.5f))
                        e.Graphics.DrawLines(pen, arr);
                }
                float lx = Points[Points.Count - 1].X * scale + ox;
                float ly = Points[Points.Count - 1].Y * scale + oy;
                using (var b = new SolidBrush(Red))
                    e.Graphics.FillEllipse(b, lx - 3.5f, ly - 3.5f, 7, 7);
            }
        }

        public MouseTestPage()
        {
            BackColor = Paper;
            BuildUi();
            RegisterMouse();

            _timer = new Timer();
            _timer.Interval = 60;
            _timer.Tick += delegate { UpdateRate(); };
            _timer.Start();
        }

        private void BuildUi()
        {
            var head = new Panel();
            head.Dock = DockStyle.Top;
            head.Height = 46;
            head.BackColor = Paper;

            var sub = new Label();
            sub.Text = "按下鼠标各键看计数；移动鼠标看回报率；左键连点测试双击故障（正常人手速最快约 100ms）。";
            sub.Font = new Font("Microsoft YaHei UI", 8.5F);
            sub.ForeColor = Ink3;
            sub.AutoSize = true;
            sub.Location = new Point(0, 13);
            var btnReset = new Button();
            btnReset.Text = "重置";
            btnReset.Size = new Size(84, 30);
            btnReset.FlatStyle = FlatStyle.Flat;
            btnReset.FlatAppearance.BorderColor = Line;
            btnReset.BackColor = Paper2;
            btnReset.ForeColor = Ink2;
            btnReset.Font = new Font("Microsoft YaHei UI", 9F);
            btnReset.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnReset.Cursor = Cursors.Hand;
            btnReset.Click += delegate { ResetAll(); };
            head.Controls.Add(btnReset);
            head.Resize += delegate { btnReset.Location = new Point(head.Width - btnReset.Width - 4, 8); };

            head.Controls.Add(sub);

            // ---- 指标行 ----
            var bar = new Panel();
            bar.Dock = DockStyle.Top;
            bar.Height = 44;
            bar.BackColor = Paper2;
            bar.BorderStyle = BorderStyle.FixedSingle;

            _lblRate = MkStat(bar, 16, "回报率", "— Hz");
            _lblMoves = MkStat(bar, 210, "移动", "0 次");
            _lblDbl = MkStat(bar, 420, "双击故障", "0 次");

            // ---- 按键卡片 ----
            var keys = new Panel();
            keys.Dock = DockStyle.Top;
            keys.Height = 96;
            keys.BackColor = Paper;
            keys.Padding = new Padding(0, 10, 0, 10);

            string[] names = { "左键", "右键", "中键", "侧键 1", "侧键 2" };
            for (int i = 0; i < 5; i++)
            {
                var c = new KeyCard(names[i]);
                c.Size = new Size(120, 74);

                var n = new Label();
                n.Text = names[i];
                n.Font = new Font("Microsoft YaHei UI", 8.5F);
                n.ForeColor = Ink2;
                n.AutoSize = true;
                n.Location = new Point(10, 8);
                c.Controls.Add(n);

                c.Count = new Label();
                c.Count.Text = "0";
                c.Count.Font = new Font("Consolas", 18F, FontStyle.Bold);
                c.Count.ForeColor = Ink;
                c.Count.AutoSize = true;
                c.Count.Location = new Point(10, 28);
                c.Controls.Add(c.Count);

                int idx = i;
                keys.Controls.Add(c);
                _cards[i] = c;
            }
            keys.Resize += delegate
            {
                int x = 0;
                for (int i = 0; i < 5; i++)
                {
                    _cards[i].Location = new Point(x, 10);
                    x += 128;
                }
            };

            // ---- 轨迹 ----
            _trailPanel = new TrailPanel();
            _trailPanel.Dock = DockStyle.Fill;
            _trailPanel.Margin = new Padding(0, 8, 0, 0);
            _trailPanel.Points = _trail;
            _trailPanel.Cursor = _cursor;

            var trailHost = new Panel();
            trailHost.Dock = DockStyle.Fill;
            trailHost.Padding = new Padding(0, 8, 0, 0);
            trailHost.BackColor = Paper;
            trailHost.Controls.Add(_trailPanel);

            Controls.Add(trailHost);
            Controls.Add(keys);
            Controls.Add(bar);
            Controls.Add(head);
        }

        private Label MkStat(Panel host, int x, string caption, string value)
        {
            var cap = new Label();
            cap.Text = caption;
            cap.Font = new Font("Microsoft YaHei UI", 8F);
            cap.ForeColor = Ink3;
            cap.AutoSize = true;
            cap.Location = new Point(x, 6);
            host.Controls.Add(cap);

            var val = new Label();
            val.Text = value;
            val.Font = new Font("Consolas", 11F, FontStyle.Bold);
            val.ForeColor = Ink;
            val.AutoSize = true;
            val.Location = new Point(x, 20);
            host.Controls.Add(val);
            return val;
        }

        private void RegisterMouse()
        {
            try
            {
                var dev = new RAWINPUTDEVICE[1];
                dev[0].UsagePage = (ushort)HID_USAGE_PAGE_GENERIC;
                dev[0].Usage = (ushort)HID_USAGE_GENERIC_MOUSE;
                dev[0].Flags = RIDEV_INPUTSINK;
                dev[0].Target = Handle;
                RegisterRawInputDevices(dev, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
            }
            catch { }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_INPUT)
            {
                try { ParseInput(m.LParam); } catch { }
            }
            base.WndProc(ref m);
        }

        private void ParseInput(IntPtr lParam)
        {
            uint size = 0;
            uint headerSize = (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER));
            GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, headerSize);
            if (size == 0 || size > 4096) return;

            IntPtr buf = _buf;
            {
                if (size > 512) return;
                if (GetRawInputData(lParam, RID_INPUT, buf, ref size, headerSize) != size) return;

                // 手写字节解析，绕开结构体对齐风险
                int off = (int)headerSize;
                if (size < off + 24) return;
                uint type = (uint)Marshal.ReadInt32(buf, 0);
                if (type != 0) return;                         // 0 = RIM_TYPEMOUSE

                // usFlags(2) 后有 2 字节填充：union 里是 ULONG，要求 4 字节对齐
                ushort f = (ushort)Marshal.ReadInt16(buf, off + 4);
                int lx = Marshal.ReadInt32(buf, off + 12);
                int ly = Marshal.ReadInt32(buf, off + 16);

                _moves++;
                _pixels += Math.Abs(lx) + Math.Abs(ly);
                Check(f, RI_MOUSE_LEFT_BUTTON_DOWN, 0, true);
                Check(f, RI_MOUSE_LEFT_BUTTON_UP, 0, false);
                Check(f, RI_MOUSE_RIGHT_BUTTON_DOWN, 1, true);
                Check(f, RI_MOUSE_RIGHT_BUTTON_UP, 1, false);
                Check(f, RI_MOUSE_MIDDLE_BUTTON_DOWN, 2, true);
                Check(f, RI_MOUSE_MIDDLE_BUTTON_UP, 2, false);
                Check(f, RI_MOUSE_BUTTON_4_DOWN, 3, true);
                Check(f, RI_MOUSE_BUTTON_4_UP, 3, false);
                Check(f, RI_MOUSE_BUTTON_5_DOWN, 4, true);
                Check(f, RI_MOUSE_BUTTON_5_UP, 4, false);

                // 轨迹（把相对位移累积成一条走线）
                if (lx != 0 || ly != 0)
                {
                    _accX += lx;
                    _accY += ly;
                    _trail.Add(new PointF(_accX, _accY));
                    if (_trail.Count > 800) _trail.RemoveAt(0);
                }
            }
        }

        private void Check(ushort flags, ushort mask, int idx, bool down)
        {
            if ((flags & mask) == 0) return;
            _down[idx] = down;

            if (down)
            {
                _counts[idx]++;
                if (idx == 0) NoteLeftDown();
            }

            var c = _cards[idx];
            if (c != null)
            {
                c.Count.Text = _counts[idx].ToString();
                c.SetActive(down, _counts[idx] > 0);
            }

            if (idx == 0 && !down)
            {
                var card = _cards[0];
                if (card != null) card.SetActive(false, _counts[0] > 0);
            }
        }

        private void NoteLeftDown()
        {
            double now = _sw.Elapsed.TotalMilliseconds;
            if (_lastLeftDown > 0)
            {
                int gap = (int)(now - _lastLeftDown);
                _intervals.Add(gap);
                if (_intervals.Count > 8) _intervals.RemoveAt(0);

                // 正常双击：间隔 100~400ms。低于 80ms 的连续按下，多半是触点抖动
                if (gap < 80) _doubleClickSuspects++;

                if (_lblDbl != null)
                {
                    _lblDbl.Text = _doubleClickSuspects + " 次";
                    _lblDbl.ForeColor = _doubleClickSuspects > 0 ? Red : Ink;
                }
            }
            _lastLeftDown = now;
        }

        private double _rateTime;
        private long _rateBase;

        private void UpdateRate()
        {
            if (_trailPanel != null) _trailPanel.Invalidate();

            double t = _sw.Elapsed.TotalSeconds;
            if (t - _rateTime < 1.0) return;

            _rate = (int)((_moves - _rateBase) / (t - _rateTime));
            _rateBase = _moves;
            _rateTime = t;

            if (_lblRate != null)
            {
                _lblRate.Text = _rate + " Hz";
                _lblRate.ForeColor = _rate >= 500 ? Green : (_rate > 0 ? Ink : Ink3);
            }
            if (_lblMoves != null) _lblMoves.Text = _moves + " 次 / " + _pixels + " px";

            // 松开态复位（避免卡在按下色）
            for (int i = 0; i < 5; i++)
                if (!_down[i] && _cards[i] != null) _cards[i].SetActive(false, _counts[i] > 0);
        }

        public void ResetAll()
        {
            for (int i = 0; i < 5; i++) { _counts[i] = 0; _down[i] = false; if (_cards[i] != null) { _cards[i].Count.Text = "0"; _cards[i].SetActive(false, false); } }
            _moves = 0; _pixels = 0; _doubleClickSuspects = 0; _lastLeftDown = -1;
            _intervals.Clear(); _trail.Clear();
            if (_lblDbl != null) { _lblDbl.Text = "0 次"; _lblDbl.ForeColor = Ink; }
            if (_lblMoves != null) _lblMoves.Text = "0 次";
            if (_trailPanel != null) _trailPanel.Invalidate();
        }
    }
}
