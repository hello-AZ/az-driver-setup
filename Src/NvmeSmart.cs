using System;
using System.Runtime.InteropServices;

namespace DriverSetup
{
    public class NvmeSmart
    {
        public int DriveNumber;
        public byte CriticalWarning;
        public int Temperature;        // ℃
        public int AvailableSpare;     // %
        public int PercentageUsed;     // %
        public ulong PowerOnHours;
        public ulong DataUnitsWritten; // 每单位 512000 字节
        public ulong PowerCycles;
        public ulong UnsafeShutdowns;

        public long WrittenGB
        {
            get { return (long)(DataUnitsWritten * 512000L / 1000000000L); }
        }
    }

    /// <summary>
    /// 直接向物理磁盘发 IOCTL_STORAGE_QUERY_PROPERTY 读 NVMe SMART/Health Log (0x02)。
    /// 需要管理员权限，否则 CreateFile 打不开 PhysicalDrive。
    /// </summary>
    public static class NvmeSmartReader
    {
        /// <summary>最近一次失败原因（便于诊断）</summary>
        public static string LastError = "";

        private const uint GENERIC_READ = 0x80000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;

        private const int StorageDeviceProtocolSpecificProperty = 50;
        private const int PropertyStandardQuery = 0;
        private const int ProtocolTypeNvme = 3;
        private const int NvmeDataTypeLogPage = 2;
        private const int SmartLogPage = 0x02;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFileW(string name, uint access, uint share,
            IntPtr sec, uint disp, uint flags, IntPtr tmpl);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(IntPtr h, uint code,
            IntPtr inBuf, int inSize, IntPtr outBuf, int outSize, out int ret, IntPtr ov);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr h);

        /// <summary>读取指定物理磁盘的 NVMe SMART。失败返回 null。</summary>
        public static NvmeSmart Read(int driveNumber)
        {
            // 先试 Device 级，失败再试 Adapter 级
            var r = ReadCore(driveNumber, StorageDeviceProtocolSpecificProperty);
            if (r != null) return r;
            return ReadCore(driveNumber, 49);
        }

        private static NvmeSmart ReadCore(int driveNumber, int propertyId)
        {
            string path = @"\\.\PhysicalDrive" + driveNumber;
            IntPtr h = CreateFileW(path, GENERIC_READ,
                                   FILE_SHARE_READ | FILE_SHARE_WRITE,
                                   IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == new IntPtr(-1))
            {
                LastError = "CreateFile: " + Marshal.GetLastWin32Error();
                return null;
            }

            const int inSize = 12 + 40;    // sizeof(STORAGE_PROPERTY_QUERY)=12（含对齐）+ 40
            const int outSize = 40 + 512;  // 返回的 SPECIFIC_DATA + SMART payload

            IntPtr ib = Marshal.AllocHGlobal(inSize);
            IntPtr ob = Marshal.AllocHGlobal(outSize);
            try
            {
                for (int i = 0; i < inSize; i++) Marshal.WriteByte(ib, i, 0);

                Marshal.WriteInt32(ib, 0, propertyId);
                Marshal.WriteInt32(ib, 4, PropertyStandardQuery);
                // STORAGE_PROTOCOL_SPECIFIC_DATA 从偏移 8 开始
                Marshal.WriteInt32(ib, 8, ProtocolTypeNvme);
                Marshal.WriteInt32(ib, 12, NvmeDataTypeLogPage);
                Marshal.WriteInt32(ib, 16, SmartLogPage);
                Marshal.WriteInt32(ib, 20, 0);
                Marshal.WriteInt32(ib, 24, 40);   // ProtocolDataOffset
                Marshal.WriteInt32(ib, 28, 512);  // ProtocolDataLength

                int ret;
                if (!DeviceIoControl(h, IOCTL_STORAGE_QUERY_PROPERTY,
                                     ib, inSize, ob, outSize, out ret, IntPtr.Zero))
                {
                    LastError = "DeviceIoControl: " + Marshal.GetLastWin32Error();
                    return null;
                }

                // 输出首部是 STORAGE_PROTOCOL_SPECIFIC_DATA，ProtocolDataOffset 在第 16 字节
                int off = Marshal.ReadInt32(ob, 16);
                if (off < 40 || off > outSize - 512) off = 40;

                byte[] d = new byte[512];
                Marshal.Copy(new IntPtr(ob.ToInt64() + off), d, 0, 512);

                var s = new NvmeSmart();
                s.DriveNumber = driveNumber;
                s.CriticalWarning = d[0];
                s.Temperature = (d[2] | (d[3] << 8)) - 273;
                s.AvailableSpare = d[4];
                s.PercentageUsed = d[6];
                s.DataUnitsWritten = BitConverter.ToUInt64(d, 24);
                s.PowerCycles = BitConverter.ToUInt64(d, 88);
                s.PowerOnHours = BitConverter.ToUInt64(d, 104);
                s.UnsafeShutdowns = BitConverter.ToUInt64(d, 120);
                return s;
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(ib);
                Marshal.FreeHGlobal(ob);
                CloseHandle(h);
            }
        }
    }
}
