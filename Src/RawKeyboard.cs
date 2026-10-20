using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DriverSetup
{
    /// <summary>
    /// Raw Input 键盘监听：能拿到真实扫描码和「哪个设备按的」。
    /// 普通 KeyDown 事件拿不到这两样，所以未知键检测和内置/外置区分都靠它。
    /// </summary>
    public class RawKeyboard
    {
        public const int WM_INPUT = 0x00FF;

        private const uint RID_INPUT = 0x10000003;
        private const uint RIDI_DEVICENAME = 0x20000007;

        private const uint RIDEV_INPUTSINK = 0x00000100;
        private const uint RIDEV_REMOVE = 0x00000001;
        private const uint HID_USAGE_PAGE_GENERIC = 0x01;
        private const uint HID_USAGE_GENERIC_KEYBOARD = 0x06;

        private const uint RI_KEY_BREAK = 0x01;
        private const uint RI_KEY_E0 = 0x02;

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
        private struct RAWKEYBOARD
        {
            public ushort MakeCode;
            public ushort Flags;
            public ushort Reserved;
            public ushort VKey;
            public uint Message;
            public uint ExtraInformation;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);

        [DllImport("user32.dll")]
        private static extern uint GetRawInputData(IntPtr hRawInput, uint command,
            IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint GetRawInputDeviceInfo(IntPtr hDevice, uint command,
            IntPtr pData, ref uint pcbSize);

        /// <summary>按键事件：虚拟键、完整扫描码、是否按下、设备路径</summary>
        public event Action<int, int, bool, string> KeyEvent;

        private readonly IntPtr _hwnd;

        public bool Registered { get; private set; }

        public RawKeyboard(IntPtr hwnd)
        {
            _hwnd = hwnd;
            var dev = new RAWINPUTDEVICE[1];
            dev[0].UsagePage = (ushort)HID_USAGE_PAGE_GENERIC;
            dev[0].Usage = (ushort)HID_USAGE_GENERIC_KEYBOARD;
            dev[0].Flags = RIDEV_INPUTSINK;     // 窗口不在前台也能收到
            dev[0].Target = hwnd;
            Registered = RegisterRawInputDevices(dev, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
        }

        /// <summary>
        /// 注销全局监听。窗口销毁前必须调用：
        /// 注册留着会指向已失效的 hwnd，重启程序会不断累积。
        /// </summary>
        public void Unregister()
        {
            if (!Registered) return;
            try
            {
                var dev = new RAWINPUTDEVICE[1];
                dev[0].UsagePage = (ushort)HID_USAGE_PAGE_GENERIC;
                dev[0].Usage = (ushort)HID_USAGE_GENERIC_KEYBOARD;
                dev[0].Flags = RIDEV_REMOVE;
                dev[0].Target = IntPtr.Zero;
                RegisterRawInputDevices(dev, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
            }
            catch (Exception ex) { Log.Swallow(ex, "RawKeyboard.Unregister"); }
            Registered = false;
        }

        /// <summary>在 WndProc 里调用；返回 true 表示是本类消费的消息</summary>
        public bool ProcessMessage(ref Message m)
        {
            if (m.Msg != WM_INPUT) return false;

            uint size = 0;
            uint headerSize = (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER));
            GetRawInputData(m.LParam, RID_INPUT, IntPtr.Zero, ref size, headerSize);
            if (size == 0) return true;

            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                uint got = GetRawInputData(m.LParam, RID_INPUT, buf, ref size, headerSize);
                if (got == uint.MaxValue) return true;

                var header = (RAWINPUTHEADER)Marshal.PtrToStructure(buf, typeof(RAWINPUTHEADER));
                if (header.Type != 1) return true;   // 1 = RIM_TYPEKEYBOARD

                int kbOff = Marshal.SizeOf(typeof(RAWINPUTHEADER));
                var kb = (RAWKEYBOARD)Marshal.PtrToStructure(
                    new IntPtr(buf.ToInt64() + kbOff), typeof(RAWKEYBOARD));

                bool down = (kb.Flags & RI_KEY_BREAK) == 0;
                int scan = kb.MakeCode;
                if ((kb.Flags & RI_KEY_E0) != 0) scan |= 0xE000;

                string path = GetDeviceName(header.Device);

                if (KeyEvent != null)
                    KeyEvent(kb.VKey, scan, down, path);
            }
            catch (Exception) { }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
            return true;
        }

        private static string GetDeviceName(IntPtr hDevice)
        {
            try
            {
                uint size = 0;
                GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, IntPtr.Zero, ref size);
                if (size == 0) return "";
                IntPtr buf = Marshal.AllocHGlobal((int)(size * 2));
                try
                {
                    uint ok = GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, buf, ref size);
                    if (ok == uint.MaxValue) return "";
                    return Marshal.PtrToStringUni(buf) ?? "";
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            catch (Exception) { return ""; }
        }

        /// <summary>是否是笔记本内置键盘（ACPI 挂载的 PS/2 键盘）</summary>
        public static bool IsBuiltIn(string devicePath)
        {
            if (string.IsNullOrEmpty(devicePath)) return false;
            string p = devicePath.ToUpperInvariant();
            return p.Contains("ACPI#") || p.Contains("MSFT0001") || p.Contains("PNP0F13") ||
                   p.Contains("PNP0303") || p.Contains("PNP0300");
        }

        /// <summary>设备路径转成短名，方便显示</summary>
        public static string ShortDeviceName(string devicePath)
        {
            if (string.IsNullOrEmpty(devicePath)) return "未知设备";
            if (IsBuiltIn(devicePath)) return "内置键盘";
            int i = devicePath.IndexOf("VID_", StringComparison.OrdinalIgnoreCase);
            if (i >= 0 && devicePath.Length >= i + 8)
                return "外接 " + devicePath.Substring(i, 8);
            return "外接设备";
        }

        /// <summary>扫描码转可读名字（用于未知键提示）</summary>
        public static string ScanCodeText(int scan, int vkey)
        {
            string name = "";
            try
            {
                var ka = new byte[256];
                uint sc = (uint)(scan & 0xFF);
                bool e0 = (scan & 0xE000) != 0;
                uint mapType = 0;
                if (e0) mapType |= 1;   // MAPVK_VSC_TO_VK_EX
                uint vk = MapVirtualKey(sc, mapType);
                if (vk != 0)
                {
                    var sb = new System.Text.StringBuilder(64);
                    uint lparam = (sc & 0xFF) << 16;
                    if (e0) lparam |= 1u << 24;
                    if (GetKeyNameText((int)lparam, sb, sb.Capacity) > 0)
                        name = sb.ToString();
                }
            }
            catch (Exception) { }
            if (string.IsNullOrEmpty(name)) name = "未知";
            return string.Format("{0} · ScanCode 0x{1:X4} · VK 0x{2:X2}", name, scan, vkey);
        }

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint code, uint mapType);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetKeyNameText(int lParam, System.Text.StringBuilder buffer, int size);
    }
}
