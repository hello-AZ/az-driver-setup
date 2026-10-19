using System;
using System.Runtime.InteropServices;

namespace DriverSetup
{
    /// <summary>显卡规格详情（走 NVIDIA NVML；非 N 卡或没驱动时 Ok=false）</summary>
    public static class GpuDetail
    {
        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int nvmlInit_v2();

        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int nvmlShutdown();

        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int nvmlDeviceGetHandleByIndex_v2(uint index, ref IntPtr device);

        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int nvmlDeviceGetName(IntPtr device, byte[] name, uint length);

        [StructLayout(LayoutKind.Sequential)]
        private struct NvmlMemory
        {
            public ulong Total;
            public ulong Free;
            public ulong Used;
        }

        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int nvmlDeviceGetMemoryInfo(IntPtr device, ref NvmlMemory memory);

        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int nvmlDeviceGetMemoryBusWidth(IntPtr device, ref uint busWidth);

        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int nvmlDeviceGetClockInfo(IntPtr device, int type, ref uint clock);

        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int nvmlDeviceGetPowerManagementLimit(IntPtr device, ref uint limit);

        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int nvmlDeviceGetPowerManagementDefaultLimit(IntPtr device, ref uint limit);

        public class Info
        {
            public string Name = "";
            public double MemTotalGB;
            public uint BusWidth;          // bit
            public double MemClockMHz;
            public double BandwidthGBs;    // 显存带宽
            public double PowerLimitW;     // 当前功耗墙
            public double PowerDefaultW;   // 出厂功耗墙（TGP）
            public bool Ok;
        }

        public static Info Read()
        {
            var info = new Info();
            try
            {
                if (nvmlInit_v2() != 0) return info;

                IntPtr dev = IntPtr.Zero;
                if (nvmlDeviceGetHandleByIndex_v2(0, ref dev) != 0) { try { nvmlShutdown(); } catch { } return info; }

                var buf = new byte[96];
                if (nvmlDeviceGetName(dev, buf, (uint)buf.Length) == 0)
                {
                    int len = Array.IndexOf(buf, (byte)0);
                    if (len < 0) len = buf.Length;
                    info.Name = System.Text.Encoding.ASCII.GetString(buf, 0, len).Trim();
                }

                var mem = new NvmlMemory();
                if (nvmlDeviceGetMemoryInfo(dev, ref mem) == 0)
                    info.MemTotalGB = mem.Total / 1024.0 / 1024.0 / 1024.0;

                uint bus = 0;
                if (nvmlDeviceGetMemoryBusWidth(dev, ref bus) == 0)
                    info.BusWidth = bus;

                uint clk = 0;
                if (nvmlDeviceGetClockInfo(dev, 1, ref clk) == 0)   // 1 = memory
                    info.MemClockMHz = clk;

                if (info.BusWidth > 0 && info.MemClockMHz > 0)
                    info.BandwidthGBs = info.BusWidth / 8.0 * info.MemClockMHz * 2 / 1000.0;   // GDDR 双沿

                uint pl = 0;
                if (nvmlDeviceGetPowerManagementLimit(dev, ref pl) == 0 && pl > 0)
                    info.PowerLimitW = pl / 1000.0;

                uint pd = 0;
                if (nvmlDeviceGetPowerManagementDefaultLimit(dev, ref pd) == 0 && pd > 0)
                    info.PowerDefaultW = pd / 1000.0;

                info.Ok = true;

                try { nvmlShutdown(); } catch { }
            }
            catch { }
            return info;
        }
    }
}
