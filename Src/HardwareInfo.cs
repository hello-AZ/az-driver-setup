using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DriverSetup
{
    // ---------- 数据模型 ----------
    public class CpuInfo
    {
        public string Name = "";
        public int Cores;
        public int Threads;
    }

    public class GpuInfo
    {
        public string Name = "";
        public long VramBytes;
        public string DriverVersion = "";
        public int CurWidth;
        public int CurHeight;
        public int CurRefresh;
        public string VramText
        {
            get
            {
                if (VramBytes <= 0) return "未知";
                double gb = VramBytes / 1073741824.0;
                return gb >= 1 ? gb.ToString("0.#") + " GB" : (VramBytes / 1048576) + " MB";
            }
        }
    }

    public class MemModule
    {
        public long Capacity;
        public int ConfiguredMHz;
        public int RatedMHz;
        public string Manufacturer = "";
        public string PartNumber = "";
        public string Slot = "";
        public string CapacityText { get { return (Capacity / 1073741824) + " GB"; } }
    }

    public class DiskInfo
    {
        public string Model = "";
        public long SizeBytes;
        public string BusType = "";
        public bool HealthOk = true;
        public long PowerOnHours = -1;
        public int Temperature = -1;
        public int Wear = -1;
        public string SizeText { get { return (SizeBytes / 1073741824.0).ToString("0") + " GB"; } }
    }

    public class MonitorInfo
    {
        public string PnpId = "";
        public string Name = "";
        public int WidthCm;
        public int HeightCm;
        public int PrefWidth;
        public int PrefHeight;
        public int PrefRefresh;
        public double SrgbPct;
        public double DciP3Pct;
        public bool Active;
        public double DiagInch
        {
            get
            {
                if (WidthCm <= 0 || HeightCm <= 0) return 0;
                return Math.Sqrt(WidthCm * WidthCm + HeightCm * HeightCm) / 2.54;
            }
        }
    }

    public class BatteryInfo
    {
        public bool Present;
        public string Name = "";
        public long DesignedMWh;
        public long FullMWh;
        public int Cycles = -1;
        public string Chemistry = "";
        public int CurrentPercent = -1;   // -1 = 未知
        public double HealthPct
        {
            get
            {
                if (DesignedMWh <= 0 || FullMWh <= 0) return 0;
                return FullMWh * 100.0 / DesignedMWh;
            }
        }
    }

    public class NicInfo
    {
        public string Name = "";
        public string Mac = "";
        public bool Connected;
    }

    public class HardwareSnapshot
    {
        public CpuInfo Cpu = new CpuInfo();
        public List<GpuInfo> Gpus = new List<GpuInfo>();
        public List<MemModule> Memory = new List<MemModule>();
        public List<DiskInfo> Disks = new List<DiskInfo>();
        public List<MonitorInfo> Monitors = new List<MonitorInfo>();
        public BatteryInfo Battery = new BatteryInfo();
        public List<NicInfo> Nics = new List<NicInfo>();
        public List<string> Errors = new List<string>();
        public long ElapsedMs;

        public long TotalMemory
        {
            get
            {
                long t = 0;
                foreach (var m in Memory) t += m.Capacity;
                return t;
            }
        }
    }

    /// <summary>本机硬件采集（全部只读：WMI / 注册表 EDID / powercfg）</summary>
    public static class HardwareInfo
    {
        public static HardwareSnapshot Collect()
        {
            var sw = Stopwatch.StartNew();
            var s = new HardwareSnapshot();

            Safe("CPU", s, delegate { CollectCpu(s); });
            Safe("显卡", s, delegate { CollectGpu(s); });
            Safe("内存", s, delegate { CollectMemory(s); });
            Safe("硬盘", s, delegate { CollectDisk(s); });
            Safe("屏幕", s, delegate { CollectMonitor(s); });
            Safe("电池", s, delegate { CollectBattery(s); });
            Safe("网卡", s, delegate { CollectNic(s); });

            sw.Stop();
            s.ElapsedMs = sw.ElapsedMilliseconds;
            return s;
        }

        private static void Safe(string name, HardwareSnapshot s, Action act)
        {
            try { act(); }
            catch (Exception ex) { s.Errors.Add(name + "：" + ex.Message); }
        }

        // ---------- CPU ----------
        private static void CollectCpu(HardwareSnapshot s)
        {
            using (var q = new ManagementObjectSearcher(
                "SELECT Name,NumberOfCores,NumberOfLogicalProcessors FROM Win32_Processor"))
            {
                foreach (ManagementObject mo in q.Get())
                {
                    s.Cpu.Name = (mo["Name"] as string ?? "").Trim();
                    s.Cpu.Cores = ToInt(mo["NumberOfCores"]);
                    s.Cpu.Threads = ToInt(mo["NumberOfLogicalProcessors"]);
                    break;
                }
            }
        }

        // ---------- 显卡 ----------
        private static void CollectGpu(HardwareSnapshot s)
        {
            var vramByName = ReadVramFromRegistry();

            using (var q = new ManagementObjectSearcher(
                "SELECT Name,DriverVersion,AdapterRAM,CurrentHorizontalResolution,CurrentVerticalResolution,CurrentRefreshRate,Status FROM Win32_VideoController"))
            {
                foreach (ManagementObject mo in q.Get())
                {
                    var g = new GpuInfo();
                    g.Name = (mo["Name"] as string ?? "").Trim();
                    g.DriverVersion = (mo["DriverVersion"] as string ?? "").Trim();
                    g.CurWidth = ToInt(mo["CurrentHorizontalResolution"]);
                    g.CurHeight = ToInt(mo["CurrentVerticalResolution"]);
                    g.CurRefresh = ToInt(mo["CurrentRefreshRate"]);

                    // AdapterRAM 在 >4 GB 时会溢出，优先用注册表 qwMemorySize
                    long regVram;
                    if (!string.IsNullOrEmpty(g.Name) && vramByName.TryGetValue(g.Name, out regVram) && regVram > 0)
                        g.VramBytes = regVram;
                    else
                        g.VramBytes = ToLong(mo["AdapterRAM"]);

                    if (!string.IsNullOrEmpty(g.Name))
                        s.Gpus.Add(g);
                }
            }
        }

        private static Dictionary<string, long> ReadVramFromRegistry()
        {
            var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var cls = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                {
                    if (cls == null) return map;
                    foreach (string sub in cls.GetSubKeyNames())
                    {
                        if (!Regex.IsMatch(sub, @"^\d{4}$")) continue;
                        using (var k = cls.OpenSubKey(sub))
                        {
                            if (k == null) continue;
                            string desc = k.GetValue("DriverDesc") as string;
                            object qw = k.GetValue("HardwareInformation.qwMemorySize");
                            if (desc == null || qw == null) continue;
                            long v;
                            if (long.TryParse(qw.ToString(), out v) && v > 0)
                                map[desc.Trim()] = v;
                        }
                    }
                }
            }
            catch (Exception) { }
            return map;
        }

        // ---------- 内存 ----------
        private static void CollectMemory(HardwareSnapshot s)
        {
            using (var q = new ManagementObjectSearcher(
                "SELECT Capacity,ConfiguredClockSpeed,Speed,Manufacturer,PartNumber,DeviceLocator FROM Win32_PhysicalMemory"))
            {
                foreach (ManagementObject mo in q.Get())
                {
                    var m = new MemModule();
                    m.Capacity = ToLong(mo["Capacity"]);
                    m.ConfiguredMHz = ToInt(mo["ConfiguredClockSpeed"]);
                    m.RatedMHz = ToInt(mo["Speed"]);
                    m.Manufacturer = Clean(mo["Manufacturer"]);
                    m.PartNumber = Clean(mo["PartNumber"]);
                    m.Slot = Clean(mo["DeviceLocator"]);
                    s.Memory.Add(m);
                }
            }
        }

        // ---------- 硬盘 ----------
        private static void CollectDisk(HardwareSnapshot s)
        {
            var healthByName = new Dictionary<string, bool>();
            var wearByName = new Dictionary<string, int>();
            var tempByName = new Dictionary<string, int>();
            var hoursByName = new Dictionary<string, long>();

            // MSFT_PhysicalDisk：健康状态 / 媒体类型 / 温度
            try
            {
                using (var q = new ManagementObjectSearcher(
                    @"root\Microsoft\Windows\Storage",
                    "SELECT FriendlyName,HealthStatus,MediaType,Size FROM MSFT_PhysicalDisk"))
                {
                    foreach (ManagementObject mo in q.Get())
                    {
                        string n = Clean(mo["FriendlyName"]);
                        if (string.IsNullOrEmpty(n)) continue;
                        int hs = ToInt(mo["HealthStatus"]);
                        healthByName[n] = (hs == 0 || hs == 1);   // 0=Healthy 1=Warning
                    }
                }
            }
            catch (Exception) { }

            // 可靠性计数器：温度 / 磨损 / 通电时间
            try
            {
                using (var q = new ManagementObjectSearcher(
                    @"root\Microsoft\Windows\Storage",
                    "SELECT DeviceId,PowerOnHours,Wear,Temperature FROM MSFT_StorageReliabilityCounter"))
                {
                    foreach (ManagementObject mo in q.Get())
                    {
                        string id = Clean(mo["DeviceId"]);
                        object h = mo["PowerOnHours"];
                        object w = mo["Wear"];
                        object t = mo["Temperature"];
                        if (h != null) hoursByName[id] = ToLong(h);
                        if (w != null) wearByName[id] = ToInt(w);
                        if (t != null) tempByName[id] = ToInt(t);
                    }
                }
            }
            catch (Exception) { }

            using (var q = new ManagementObjectSearcher(
                "SELECT Model,Size,InterfaceType,PNPDeviceID FROM Win32_DiskDrive"))
            {
                foreach (ManagementObject mo in q.Get())
                {
                    var d = new DiskInfo();
                    d.Model = Clean(mo["Model"]);
                    d.SizeBytes = ToLong(mo["Size"]);
                    d.BusType = Clean(mo["InterfaceType"]);

                    bool ok;
                    if (healthByName.TryGetValue(d.Model, out ok)) d.HealthOk = ok;

                    foreach (var kv in tempByName)
                        if (kv.Key.IndexOf(d.Model, StringComparison.OrdinalIgnoreCase) >= 0) d.Temperature = kv.Value;
                    foreach (var kv in wearByName)
                        if (kv.Key.IndexOf(d.Model, StringComparison.OrdinalIgnoreCase) >= 0) d.Wear = kv.Value;
                    foreach (var kv in hoursByName)
                        if (kv.Key.IndexOf(d.Model, StringComparison.OrdinalIgnoreCase) >= 0) d.PowerOnHours = kv.Value;

                    s.Disks.Add(d);
                }
            }
        }

        // ---------- 屏幕 ----------
        private static void CollectMonitor(HardwareSnapshot s)
        {
            var activePnp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var q = new ManagementObjectSearcher(@"root\wmi",
                    "SELECT InstanceName,Active FROM WmiMonitorID"))
                {
                    foreach (ManagementObject mo in q.Get())
                    {
                        if (ToInt(mo["Active"]) != 0)
                        {
                            string inst = Clean(mo["InstanceName"]);
                            string[] parts = inst.Split('\\');
                            if (parts.Length >= 2) activePnp.Add(parts[1]);
                        }
                    }
                }
            }
            catch (Exception) { }

            try
            {
                using (var display = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Enum\DISPLAY"))
                {
                    if (display == null) return;
                    foreach (string pnp in display.GetSubKeyNames())
                    {
                        using (var pk = display.OpenSubKey(pnp))
                        {
                            if (pk == null) continue;
                            foreach (string inst in pk.GetSubKeyNames())
                            {
                                using (var dk = pk.OpenSubKey(inst + @"\Device Parameters"))
                                {
                                    if (dk == null) continue;
                                    byte[] edid = dk.GetValue("EDID") as byte[];
                                    if (edid == null || edid.Length < 128) continue;
                                    var m = ParseEdid(edid);
                                    if (m == null) continue;
                                    m.PnpId = pnp;
                                    m.Active = activePnp.Contains(pnp);
                                    s.Monitors.Add(m);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception) { }

            // 去重：同一面板在注册表里可能有多个枚举实例
            var seen = new HashSet<string>();
            var uniq = new List<MonitorInfo>();
            foreach (var m in s.Monitors)
            {
                string key = m.PnpId + "|" + m.PrefWidth + "x" + m.PrefHeight + "|" + m.WidthCm + "x" + m.HeightCm;
                if (seen.Add(key)) uniq.Add(m);
            }
            s.Monitors = uniq;
        }

        private static MonitorInfo ParseEdid(byte[] e)
        {
            var m = new MonitorInfo();

            int v = (e[8] << 8) | e[9];
            m.PnpId = m.PnpId ?? "";
            m.WidthCm = e[21];
            m.HeightCm = e[22];

            double rx = ((e[27] << 2) | ((e[25] >> 6) & 3)) / 1024.0;
            double ry = ((e[28] << 2) | ((e[25] >> 4) & 3)) / 1024.0;
            double gx = ((e[29] << 2) | ((e[25] >> 2) & 3)) / 1024.0;
            double gy = ((e[30] << 2) | (e[25] & 3)) / 1024.0;
            double bx = ((e[31] << 2) | ((e[26] >> 6) & 3)) / 1024.0;
            double by = ((e[32] << 2) | ((e[26] >> 4) & 3)) / 1024.0;
            double wx = ((e[33] << 2) | ((e[26] >> 2) & 3)) / 1024.0;
            double wy = ((e[34] << 2) | (e[26] & 3)) / 1024.0;

            double area = TriArea(rx, ry, gx, gy, wx, wy)
                        + TriArea(gx, gy, bx, by, wx, wy)
                        + TriArea(bx, by, rx, ry, wx, wy);

            m.SrgbPct = area * 100.0 / GamutArea(0.640, 0.330, 0.300, 0.600, 0.150, 0.060);
            m.DciP3Pct = area * 100.0 / GamutArea(0.680, 0.320, 0.265, 0.690, 0.150, 0.060);

            for (int j = 0; j < 4; j++)
            {
                int off = 54 + j * 18;
                byte[] d = new byte[18];
                Array.Copy(e, off, d, 0, 18);

                if (d[0] == 0 && d[1] == 0 && d[3] == 0xFC)
                {
                    m.Name = DescText(d).Trim();
                }
                else if (d[0] == 0 && d[1] == 0 && d[2] == 0)
                {
                    continue;
                }
                else if (m.PrefWidth == 0)
                {
                    double pixclk = ((d[1] << 8) | d[0]) * 10.0;
                    int ha = d[2] | ((d[4] & 0xF0) << 4);
                    int hb = d[3] | ((d[4] & 0x0F) << 8);
                    int va = d[5] | ((d[7] & 0xF0) << 4);
                    int vb = d[6] | ((d[7] & 0x0F) << 8);
                    if (pixclk > 0 && ha > 0 && va > 0)
                    {
                        m.PrefWidth = ha;
                        m.PrefHeight = va;
                        m.PrefRefresh = (int)Math.Round(pixclk * 1000.0 / ((ha + hb) * (va + vb)));
                    }
                }
            }
            return m;
        }

        private static string DescText(byte[] d)
        {
            var sb = new StringBuilder();
            for (int i = 5; i < 18; i++)
            {
                if (d[i] == 0x0A || d[i] == 0) break;
                sb.Append((char)d[i]);
            }
            return sb.ToString();
        }

        private static double TriArea(double x1, double y1, double x2, double y2, double x3, double y3)
        {
            return Math.Abs((x2 - x1) * (y3 - y1) - (x3 - x1) * (y2 - y1)) / 2.0;
        }

        private static double GamutArea(double rx, double ry, double gx, double gy, double bx, double by)
        {
            const double wx = 0.3127, wy = 0.3290;
            return TriArea(rx, ry, gx, gy, wx, wy) + TriArea(gx, gy, bx, by, wx, wy) + TriArea(bx, by, rx, ry, wx, wy);
        }

        // ---------- 电池 ----------
        private static void CollectBattery(HardwareSnapshot s)
        {
            // 先走 WMI（部分机型可用）
            try
            {
                using (var q = new ManagementObjectSearcher(@"root\wmi",
                    "SELECT DesignedCapacity FROM BatteryStaticData"))
                {
                    foreach (ManagementObject mo in q.Get())
                    {
                        s.Battery.DesignedMWh = ToLong(mo["DesignedCapacity"]);
                        s.Battery.Present = s.Battery.DesignedMWh > 0;
                        break;
                    }
                }
            }
            catch (Exception) { }

            try
            {
                using (var q = new ManagementObjectSearcher(@"root\wmi",
                    "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity"))
                {
                    foreach (ManagementObject mo in q.Get())
                    {
                        s.Battery.FullMWh = ToLong(mo["FullChargedCapacity"]);
                        break;
                    }
                }
            }
            catch (Exception) { }

            try
            {
                using (var q = new ManagementObjectSearcher(@"root\wmi",
                    "SELECT CycleCount FROM BatteryCycleCount"))
                {
                    foreach (ManagementObject mo in q.Get())
                    {
                        s.Battery.Cycles = ToInt(mo["CycleCount"]);
                        break;
                    }
                }
            }
            catch (Exception) { }

            ReadCurrentPercent(s.Battery);

            if (s.Battery.DesignedMWh > 0 && s.Battery.FullMWh > 0) return;

            // 兜底：powercfg /batteryreport（与系统电池页同源）
            string html = Path.Combine(Path.GetTempPath(), "drvsetup-battery.html");
            try
            {
                var psi = new ProcessStartInfo("powercfg.exe",
                    "/batteryreport /output \"" + html + "\" /duration 0");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                using (var p = Process.Start(psi))
                {
                    if (p == null) return;
                    p.WaitForExit(15000);
                }
                if (!File.Exists(html)) return;

                string text = File.ReadAllText(html, Encoding.UTF8);
                text = Regex.Replace(text, "<[^>]+>", " ");
                text = Regex.Replace(text, @"\s+", " ");

                var mName = Regex.Match(text, @"NAME\s+([A-Z0-9\-]+)\s+MANUFACTURER", RegexOptions.IgnoreCase);
                if (mName.Success) s.Battery.Name = mName.Groups[1].Value;

                var mChem = Regex.Match(text, @"CHEMISTRY\s+([A-Za-z]+)", RegexOptions.IgnoreCase);
                if (mChem.Success) s.Battery.Chemistry = mChem.Groups[1].Value;

                var mDes = Regex.Match(text, @"DESIGN CAPACITY\s+([\d,]+)\s*mWh", RegexOptions.IgnoreCase);
                if (mDes.Success) s.Battery.DesignedMWh = ParseNum(mDes.Groups[1].Value);

                var mFull = Regex.Match(text, @"FULL CHARGE CAPACITY\s+([\d,]+)\s*mWh", RegexOptions.IgnoreCase);
                if (mFull.Success) s.Battery.FullMWh = ParseNum(mFull.Groups[1].Value);

                var mCyc = Regex.Match(text, @"CYCLE COUNT\s+([\d,]+)", RegexOptions.IgnoreCase);
                if (mCyc.Success) s.Battery.Cycles = (int)ParseNum(mCyc.Groups[1].Value);

                s.Battery.Present = s.Battery.DesignedMWh > 0;
                ReadCurrentPercent(s.Battery);
            }
            catch (Exception) { }
            finally
            {
                try { if (File.Exists(html)) File.Delete(html); } catch (Exception) { }
            }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

        private static void ReadCurrentPercent(BatteryInfo b)
        {
            try
            {
                SYSTEM_POWER_STATUS st;
                if (GetSystemPowerStatus(out st))
                {
                    if (st.BatteryLifePercent != 255)
                        b.CurrentPercent = st.BatteryLifePercent;
                }
            }
            catch (Exception) { }
        }

        private static long ParseNum(string s)
        {
            long v;
            long.TryParse(s.Replace(",", "").Replace(" ", ""), out v);
            return v;
        }

        // ---------- 网卡 ----------
        private static void CollectNic(HardwareSnapshot s)
        {
            using (var q = new ManagementObjectSearcher(
                "SELECT Name,MACAddress,NetConnectionStatus FROM Win32_NetworkAdapter WHERE PhysicalAdapter=True"))
            {
                foreach (ManagementObject mo in q.Get())
                {
                    var n = new NicInfo();
                    n.Name = Clean(mo["Name"]);
                    n.Mac = Clean(mo["MACAddress"]);
                    // NetConnectionStatus: 2=已连接 7=媒体断开 0=断开
                    n.Connected = ToInt(mo["NetConnectionStatus"]) == 2;
                    if (!string.IsNullOrEmpty(n.Name)) s.Nics.Add(n);
                }
            }
        }

        // ---------- 工具 ----------
        private static string Clean(object o)
        {
            if (o == null) return "";
            string s = o.ToString();
            return s.Replace("\0", "").Trim();
        }

        private static int ToInt(object o)
        {
            if (o == null) return 0;
            if (o is bool) return ((bool)o) ? 1 : 0;      // WMI 的 Active 是 Boolean
            try { return Convert.ToInt32(o.ToString(), CultureInfo.InvariantCulture); }
            catch (Exception) { return 0; }
        }

        private static long ToLong(object o)
        {
            if (o == null) return 0;
            try { return Convert.ToInt64(o.ToString(), CultureInfo.InvariantCulture); }
            catch (Exception) { return 0; }
        }
    }
}
