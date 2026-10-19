using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DriverSetup
{
    /// <summary>一个按键的布局与状态</summary>
    public class KeyDef
    {
        public string Label = "";
        public string Sub = "";      // 小字（如 F1 下的小标注）
        public Keys Vk;
        public float X, Y;           // 单位坐标
        public float W = 1, H = 1;   // 单位宽高
        public bool Down;
        public bool Done;
    }

    /// <summary>104 键可视化键盘：按住高亮、松开变绿</summary>
    public class KeyboardView : Control
    {
        private static readonly Color Bg = Theme.Bg;
        private static readonly Color KeyFace = Theme.Card;
        private static readonly Color KeyEdge = Theme.Line;
        private static readonly Color Ink = Theme.Text;
        private static readonly Color Ink3 = Theme.Text3;
        private static readonly Color DownColor = Theme.Accent;
        private static readonly Color DoneColor = Theme.Green;

        private readonly List<KeyDef> _keys = new List<KeyDef>();
        private int _layoutId = 1;            // 默认 102 联想式

        public int TestedCount
        {
            get
            {
                int n = 0;
                foreach (var k in _keys) if (k.Done) n++;
                return n;
            }
        }

        public int TotalCount { get { return _keys.Count; } }

        public event EventHandler ProgressChanged;


        private Timer _sync;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        public KeyboardView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Bg;
            SetLayout(_layoutId);

            // 每 120ms 用真实按键状态校正一次，防止 RawInput 丢 KeyUp 导致键卡在按下态
            _sync = new Timer();
            _sync.Interval = 120;
            _sync.Tick += delegate { SyncWithPhysical(); };
            _sync.Start();
        }

        /// <summary>用 GetAsyncKeyState 校正按下状态（RawInput 偶发丢 KeyUp 时能自愈）</summary>
        private void SyncWithPhysical()
        {
            bool changed = false;
            foreach (var k in _keys)
            {
                if (k.Vk == Keys.None) continue;

                int vk = (int)k.Vk;
                // 修饰键 RawInput/GetAsyncKeyState 都只认通用 VK
                if (k.Vk == Keys.LControlKey || k.Vk == Keys.RControlKey) vk = (int)Keys.ControlKey;
                else if (k.Vk == Keys.LShiftKey || k.Vk == Keys.RShiftKey) vk = (int)Keys.ShiftKey;
                else if (k.Vk == Keys.LMenu || k.Vk == Keys.RMenu) vk = (int)Keys.Menu;

                bool down = (GetAsyncKeyState(vk) & 0x8000) != 0;
                if (k.Down != down)
                {
                    k.Down = down;
                    if (!down) k.Done = true;
                    changed = true;
                }
            }
            if (changed) Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _sync != null) { _sync.Stop(); _sync.Dispose(); _sync = null; }
            base.Dispose(disposing);
        }

        // ---------------- 布局 ----------------






        /// <summary>当前布局编号（0=104 全尺寸 1=102 联想式 2=14 寸）</summary>
        public int LayoutId { get { return _layoutId; } }

        /// <summary>切换布局模板</summary>
        public void SetLayout(int id)
        {
            if (id < 0 || id >= KeyboardLayouts.Names.Length) id = 1;
            _layoutId = id;

            _keys.Clear();
            foreach (var k in KeyboardLayouts.Build(id)) _keys.Add(k);

            Invalidate();
            if (ProgressChanged != null) ProgressChanged(this, EventArgs.Empty);
        }

        private float MaxX()
        {
            float m = 0;
            foreach (var k in _keys) { float e = k.X + k.W; if (e > m) m = e; }
            return Math.Max(1f, m + 0.25f);
        }

        private float MaxY()
        {
            float m = 0;
            foreach (var k in _keys) { float e = k.Y + k.H; if (e > m) m = e; }
            return Math.Max(1f, m + 0.25f);
        }

        // ---------------- 状态 ----------------
        public void ResetAll()
        {
            foreach (var k in _keys) { k.Done = false; k.Down = false; }
            Invalidate();
            if (ProgressChanged != null) ProgressChanged(this, EventArgs.Empty);
        }

        /// <summary>物理键事件：返回 true 表示命中并吞掉</summary>
        public bool HandleKey(Keys key, bool down)
        {
            return HandleKey(key, down, 0);
        }

        public bool HandleKey(Keys key, bool down, int scan)
        {
            bool hit = false;
            foreach (var k in _keys)
            {
                if (KeyMatch(k.Vk, key))
                {
                    // 左右修饰键同 VK 时，两个键一起亮
                    k.Down = down;
                    if (!down) k.Done = true;
                    hit = true;
                }
            }
            if (hit)
            {
                Invalidate();
                if (ProgressChanged != null) ProgressChanged(this, EventArgs.Empty);
            }
            return hit;
        }

        /// <summary>
        /// 修饰键等价判断：RawInput 只报通用 VK（Ctrl=0x11 / Shift=0x10 / Alt=0x12），
        /// 而布局里存的是左右专用 VK（LControlKey=0xA2 等），必须做映射否则永远匹配不上。
        /// </summary>
        private static bool KeyMatch(Keys layoutKey, Keys pressedKey)
        {
            if (layoutKey == pressedKey) return true;
            if (layoutKey == Keys.None) return false;

            if (layoutKey == Keys.LControlKey || layoutKey == Keys.RControlKey)
                return pressedKey == Keys.ControlKey;
            if (layoutKey == Keys.LShiftKey || layoutKey == Keys.RShiftKey)
                return pressedKey == Keys.ShiftKey;
            if (layoutKey == Keys.LMenu || layoutKey == Keys.RMenu)
                return pressedKey == Keys.Menu;
            if (layoutKey == Keys.LWin || layoutKey == Keys.RWin)
                return pressedKey == Keys.LWin || pressedKey == Keys.RWin;
            return false;
        }

        // ---------------- 绘制 ----------------
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Bg);

            float availW = Width - 24;
            float availH = Height - 20;
            float mw = MaxX(), mh = MaxY();
            float ux = availW / mw;
            float uy = availH / mh;
            float u = Math.Min(ux, uy);

            float ox = (Width - mw * u) / 2f;
            float oy = (Height - mh * u) / 2f;

            float gap = Math.Max(2f, u * 0.12f);

            foreach (var k in _keys)
            {
                float x = ox + k.X * u + gap / 2;
                float y = oy + k.Y * u + gap / 2;
                float w = k.W * u - gap;
                float h = k.H * u - gap;

                Color face = KeyFace;
                Color edge = KeyEdge;
                Color text = Ink;

                if (k.Down)
                {
                    face = DownColor;
                    edge = DownColor;
                    text = Color.White;
                }
                else if (k.Done)
                {
                    face = Color.FromArgb(228, 240, 232);
                    edge = DoneColor;
                    text = DoneColor;
                }

                using (var path = RoundRect(new RectangleF(x, y, w, h), Math.Max(3f, u * 0.12f)))
                {
                    using (var brush = new SolidBrush(face)) g.FillPath(brush, path);
                    using (var pen = new Pen(edge, k.Down ? 2f : 1f)) g.DrawPath(pen, path);
                }

                if (!string.IsNullOrEmpty(k.Label))
                {
                    float px = Math.Min(k.H * u * 0.36f, k.W * u * 0.24f);
                    float fs = px * 72f / g.DpiY;      // pt = 像素 * 72 / DPI
                    if (fs < 5f) fs = 5f;
                    using (var f = new Font("Microsoft YaHei UI", fs, FontStyle.Regular))
                    using (var brush = new SolidBrush(text))
                    {
                        var sf = new StringFormat();
                        sf.Alignment = StringAlignment.Center;
                        sf.LineAlignment = StringAlignment.Center;
                        sf.Trimming = StringTrimming.EllipsisCharacter;
                        var rect = new RectangleF(x + 2, y + 2, w - 4, h - 4);
                        g.DrawString(k.Label, f, brush, rect, sf);
                    }
                }
            }
        }

        private static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    /// <summary>全屏纯色坏点检测</summary>
    public class DeadPixelForm : Form
    {
        private static readonly Color[] Colors =
        {
            Color.White, Color.Red, Color.Green, Color.Blue, Color.Black
        };

        private int _idx;

        public DeadPixelForm() : this(null) { }

        public DeadPixelForm(Screen target)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            if (target != null)
            {
                Bounds = target.Bounds;          // 在指定屏上全屏
            }
            else
            {
                WindowState = FormWindowState.Maximized;
            }
            TopMost = true;
            KeyPreview = true;
            BackColor = Colors[0];
            Cursor = Cursors.Default;
            Text = "坏点检测 · 任意键换色 · Esc 退出";
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                Close();
                return;
            }
            NextColor();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            NextColor();
        }

        private void NextColor()
        {
            _idx = (_idx + 1) % Colors.Length;
            BackColor = Colors[_idx];
        }
    }
}
