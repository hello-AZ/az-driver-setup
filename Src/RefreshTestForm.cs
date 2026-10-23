using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DriverSetup
{
    /// <summary>
    /// 刷新率测试。
    ///
    /// 早先的版本靠"重绘循环跑多快"来推算刷新率，这是错的：窗口化模式下
    /// DWM 不会因为垂直同步拦住你的循环，量到的其实是 CPU 重绘速度
    /// （165Hz 屏报 180+、超频 210Hz 屏报 110+ 都是这么来的）。
    ///
    /// 现在以系统数据为准，取两个来源互相印证：
    ///   ① QueryDisplayConfig(路径 targetInfo.refreshRate) —— 显示子系统实际输出的刷新率
    ///   ② EnumDisplaySettings(ENUM_CURRENT_SETTINGS) —— 系统当前模式里记录的刷新率
    /// 两者差得远，就说明"设置归设置、实际归实际"（超频没生效、带宽不够等）。
    ///
    /// DwmGetCompositionTimingInfo 在 Win11 上返回 0x88980090，取不到数据，已弃用。
    /// 重绘帧率仍显示，但只作参考，明确标注不代表刷新率。
    /// </summary>
    public class RefreshTestForm : Form
    {
        private readonly Screen _screen;
        private volatile bool _running;
        private float _x;
        private double _renderFps;
        private long _frames;
        private readonly Stopwatch _sw = Stopwatch.StartNew();

        private int _modeHz;                  // 系统当前模式（DEVMODE）
        private double _qdcHz;                // 本屏 QueryDisplayConfig 实测
        private bool _qdcExact;               // true=按设备名精确匹配，false=按系统模式推断
        private string _allRates = "";        // 全部显示器的刷新率摘要
        private double _lastPoll = -9999;
        private double _lastFrameMs = -1;

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

            ReadRefresh();

            KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Space) Close();
            };
            MouseClick += delegate { Close(); };

            Shown += delegate { RunLoop(); };
        }

        private void ReadRefresh()
        {
            // ① 系统当前模式
            try
            {
                var dm = new DEVMODE();
                dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
                if (EnumDisplaySettings(_screen.DeviceName, ENUM_CURRENT_SETTINGS, ref dm))
                    _modeHz = dm.dmDisplayFrequency;
            }
            catch (Exception ex) { Log.Swallow(ex, "RefreshTest.ReadMode"); }

            // ② 显示子系统实际输出
            try
            {
                var rates = ReadAllRates();
                _qdcHz = 0;
                var parts = new List<string>();
                _qdcExact = false;
                for (int i = 0; i < rates.Count; i++)
                {
                    var r = rates[i];
                    // 编号用真实路径序号，不能拿已加入的条数当序号（中间失败会错位）
                    string shortName = string.IsNullOrEmpty(r.GdiName)
                        ? "#" + (i + 1)
                        : r.GdiName.Substring(r.GdiName.LastIndexOf('\\') + 1);
                    parts.Add(shortName + " " + r.Hz.ToString("F1") + " Hz");
                    if (!string.IsNullOrEmpty(r.GdiName) &&
                        r.GdiName.Equals(_screen.DeviceName, StringComparison.OrdinalIgnoreCase))
                    {
                        _qdcHz = r.Hz;
                        _qdcExact = true;
                    }
                }
                _allRates = string.Join("　·　", parts.ToArray());

                // DisplayConfigGetDeviceInfo 在部分 Win11 版本上返回 31（设备未就绪），
                // 拿不到 \\.\DISPLAYn 名字。退回启发式：本屏的系统模式值，
                // 跟哪条活动路径的实测值最接近，就认哪条。
                if (_qdcHz <= 0 && rates.Count > 0)
                {
                    if (_modeHz > 0)
                    {
                        double bestDiff = double.MaxValue;
                        foreach (var r in rates)
                        {
                            double d = Math.Abs(r.Hz - _modeHz);
                            if (d < bestDiff) { bestDiff = d; _qdcHz = r.Hz; }
                        }
                    }
                    else _qdcHz = rates[0].Hz;
                }
            }
            catch (Exception ex) { Log.Swallow(ex, "RefreshTest.ReadQdc"); }
        }

        // ---- QueryDisplayConfig ----

        private sealed class DisplayRate
        {
            public string GdiName;
            public double Hz;
        }

        private static List<DisplayRate> ReadAllRates()
        {
            var list = new List<DisplayRate>();
            uint numPath, numMode;
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out numPath, out numMode) != 0)
                return list;
            if (numPath == 0) return list;

            var paths = new DISPLAYCONFIG_PATH_INFO[numPath];
            var modes = new byte[numMode * 128];
            uint pathCount = numPath, modeCount = numMode;

            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths,
                                   ref modeCount, modes, IntPtr.Zero) != 0)
                return list;

            for (uint i = 0; i < pathCount; i++)
            {
                var t = paths[i].targetInfo;
                if (t.targetAvailable == 0 || t.refreshRate.Denominator == 0) continue;

                double hz = (double)t.refreshRate.Numerator / t.refreshRate.Denominator;
                if (hz <= 0 || hz > 2000) continue;

                string gdi = null;
                try
                {
                    var src = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
                    src.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
                    src.header.adapterId = paths[i].sourceInfo.adapterId;
                    src.header.id = paths[i].sourceInfo.id;
                    src.header.size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME));
                    if (DisplayConfigGetDeviceInfo(ref src) == 0) gdi = src.viewGdiDeviceName;
                }
                catch (Exception ex) { Log.Swallow(ex, "RefreshTest.SourceName"); }

                var item = new DisplayRate();
                item.GdiName = gdi;
                item.Hz = hz;
                list.Add(item);
            }
            return list;
        }

        // ---- 动画循环（只负责画面与帧率参考值） ----

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

                // 每 2 秒重读一次：用户可能中途改刷新率或超频生效
                if (now - _lastPoll > 2000)
                {
                    _lastPoll = now;
                    ReadRefresh();
                }

                // 按时间移动，不能按帧算：210Hz 屏上每帧固定像素会让方块快 3.4 倍，
                // 拖影完全看不清。固定「每秒移动 1/3 屏宽」，所有刷新率下手感一致。
                double frameDt = _lastFrameMs < 0 ? 16.0 : now - _lastFrameMs;
                _lastFrameMs = now;
                if (frameDt > 0 && frameDt < 200)
                    _x += (float)(Math.Max(0.5, w / 3.0) * frameDt / 1000.0);
                if (_x > w) _x = -90;

                _frames++;
                fpsFrames++;
                if (now - fpsWindow >= 700)
                {
                    _renderFps = fpsFrames * 1000.0 / (now - fpsWindow);
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

            using (var p = new Pen(Color.FromArgb(26, 26, 30), 2))
            {
                for (int x = 0; x < w; x += 24) g.DrawLine(p, x, 0, x, h);
            }

            var box = new Rectangle((int)_x, h / 2 - 55, 90, 110);
            using (var b = new SolidBrush(Color.White)) g.FillRectangle(b, box);

            double primary = _qdcHz > 0 ? _qdcHz : _modeHz;

            using (var f1 = new Font("Consolas", 30F, FontStyle.Bold))
            using (var f2 = new Font("Consolas", 12F))
            using (var b1 = new SolidBrush(Color.White))
            using (var b2 = new SolidBrush(Color.FromArgb(170, 170, 170)))
            {
                g.DrawString(primary > 0 ? primary.ToString("F1") + " Hz" : "— Hz", f1, b1, 40, 32);

                string qdcText = _qdcHz > 0
                    ? _qdcHz.ToString("F2") + " Hz" + (_qdcExact ? "" : "（按系统模式推断）")
                    : "读取失败";

                g.DrawString("系统模式 " + (_modeHz > 0 ? _modeHz + " Hz" : "未知") +
                             "      实测输出 " + qdcText,
                             f2, b2, 44, 92);

                if (!string.IsNullOrEmpty(_allRates))
                    g.DrawString("全部显示器　" + _allRates, f2, b2, 44, 116);

                g.DrawString("重绘帧率 " + _renderFps.ToString("F1") + " FPS" +
                             "（只反映 CPU 画得多快，不代表刷新率）", f2, b2, 44, 140);

                g.DrawString("累计 " + _frames + " 帧", f2, b2, 44, 164);

                string verdict;
                Color vc = Color.FromArgb(120, 220, 150);
                if (_qdcHz > 0 && _modeHz > 0)
                {
                    double diff = Math.Abs(_qdcHz - _modeHz);
                    if (diff <= Math.Max(1.0, _modeHz * 0.02))
                        verdict = "✓ 实际刷新率与系统设置一致";
                    else if (_qdcHz < _modeHz * 0.9)
                    {
                        verdict = "⚠ 系统设成 " + _modeHz + " Hz，实际只有 " + _qdcHz.ToString("F0") +
                                  " Hz —— 超频未生效 / 线材带宽不足 / 驱动回落";
                        vc = Color.FromArgb(235, 150, 90);
                    }
                    else
                    {
                        verdict = "⚠ 实际 " + _qdcHz.ToString("F0") + " Hz 高于系统设置 " +
                                  _modeHz + " Hz，请复查";
                        vc = Color.FromArgb(235, 150, 90);
                    }
                }
                else if (_modeHz > 0)
                {
                    verdict = "系统报告 " + _modeHz + " Hz（显示子系统未返回实测值，仅供参照）";
                }
                else verdict = "读取中…";

                using (var b3 = new SolidBrush(vc))
                    g.DrawString(verdict, f2, b3, 44, 194);
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

        // ---- Win32 ----

        private const int ENUM_CURRENT_SETTINGS = -1;
        private const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
        private const uint DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
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
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
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

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_RATIONAL
        {
            public uint Numerator;
            public uint Denominator;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_SOURCE_INFO
        {
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_TARGET_INFO
        {
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx;
            public uint outputTechnology;
            public uint rotation;
            public uint scaling;
            public DISPLAYCONFIG_RATIONAL refreshRate;
            public uint scanLineOrdering;
            public int targetAvailable;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_INFO
        {
            public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
            public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
            public uint flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
        {
            public uint type;
            public LUID adapterId;
            public uint id;
            public uint size;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string viewGdiDeviceName;
        }

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

        [DllImport("user32.dll")]
        private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPath, out uint numMode);

        [DllImport("user32.dll")]
        private static extern int QueryDisplayConfig(uint flags, ref uint numPath,
            [Out] DISPLAYCONFIG_PATH_INFO[] pathArray, ref uint numMode,
            [Out] byte[] modeInfoArray, IntPtr currentTopologyId);

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME deviceName);
    }
}
