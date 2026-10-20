using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace DriverSetup
{
    /// <summary>Lenovo 官方机型档案</summary>
    public class LenovoMachine
    {
        public string MachineNo = "";
        public string MachineName = "";
        public string MTM = "";
        public string ProductModel = "";
        public string ProductSeries = "";
        public string CataLogID = "";
        public string CataLogName = "";
        public string ProductDate = "";
        public string SaleArea = "";
    }

    /// <summary>Lenovo 官网驱动清单中的一条</summary>
    public class LenovoDriver
    {
        public string PartName = "";
        public string DriverName = "";
        public string Version = "";
        public string HardwareId = "";
        public string FileName = "";
        public string FileSize = "";
        public string FilePath = "";
        public string InstallCode = "";
        public string DriverCode = "";
        public string IssuedDate = "";
        public string CoreVersion = "";

        public bool IsFirmware
        {
            get { return PartName == "BIOS" || PartName == "固件"; }
        }
    }

    /// <summary>本机设备（带硬件 ID）</summary>
    public class HwDevice
    {
        public string Name = "";
        public string DeviceId = "";
        public string PnpClass = "";
        public string Provider = "";
        public string Version = "";
        public string Date = "";
    }

    /// <summary>官网条目 vs 本机 的比对结果</summary>
    public class CompareResult
    {
        public LenovoDriver Driver;
        public string LocalVersion = "";
        public string MatchedDevice = "";
        public string Status = "";       // 已最新 / 可更新 / 本机更新 / 未安装 / 无此硬件 / 需人工
        public int StatusRank;           // 0 已最新, 1 可更新, 2 未安装, 3 本机更新, 4 无此硬件, 5 需人工
    }

    public static class LenovoApi
    {
        private const string ApiBase = "https://newsupport.lenovo.com.cn/api";
        private const string UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

        public static List<string> Log = new List<string>();

        static LenovoApi()
        {
            // .NET Framework 默认可能只开 TLS 1.0，Lenovo 站要求 TLS 1.2
            try
            {
                ServicePointManager.SecurityProtocol =
                    (SecurityProtocolType)3072 | (SecurityProtocolType)768 | SecurityProtocolType.Tls;
            }
            catch (Exception) { }
            ServicePointManager.DefaultConnectionLimit = 8;
        }

        private static string Get(string url)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = UserAgent;
            req.Timeout = 20000;
            req.ReadWriteTimeout = 20000;
            req.Accept = "application/json, text/plain, */*";
            // 国内站直连，绕开系统代理（HTTP_PROXY 指向的本地端口未必在跑）
            req.Proxy = null;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                return sr.ReadToEnd();
            }
        }

        private static Dictionary<string, object> Parse(string json)
        {
            using (var doc = JsonDocument.Parse(json))
                return ToDict(doc.RootElement);
        }

        /// <summary>把 JsonElement 摊平成普通字典/列表，等价于原来 JavaScriptSerializer 的结果</summary>
        private static Dictionary<string, object> ToDict(JsonElement e)
        {
            var d = new Dictionary<string, object>();
            foreach (var p in e.EnumerateObject()) d[p.Name] = ToObj(p.Value);
            return d;
        }

        private static object ToObj(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object: return ToDict(e);
                case JsonValueKind.Array:
                    // 原生 JavaScriptSerializer 把数组还原成 ArrayList，沿用同样的类型
                    var list = new System.Collections.ArrayList();
                    foreach (var x in e.EnumerateArray()) list.Add(ToObj(x));
                    return list;
                case JsonValueKind.String: return e.GetString();
                case JsonValueKind.Number:
                    long l;
                    if (e.TryGetInt64(out l)) return l;
                    double db;
                    if (e.TryGetDouble(out db)) return db;
                    return e.GetRawText();
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                default: return null;
            }
        }

        private static string S(Dictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null) return Convert.ToString(v).Trim();
            return "";
        }

        // ---------- 1. 本机序列号 ----------
        public static string ReadSerialNumber()
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT IdentifyingNumber, Name, Vendor FROM Win32_ComputerSystemProduct");
                foreach (System.Management.ManagementObject mo in searcher.Get())
                {
                    string sn = Convert.ToString(mo["IdentifyingNumber"]).Trim();
                    if (sn.Length > 0) return sn;
                }
            }
            catch (Exception) { }
            return "";
        }

        public static string ReadProductModel()
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT Version, Name FROM Win32_ComputerSystemProduct");
                foreach (System.Management.ManagementObject mo in searcher.Get())
                {
                    string v = Convert.ToString(mo["Version"]).Trim();
                    if (v.Length > 0) return v;
                    return Convert.ToString(mo["Name"]).Trim();
                }
            }
            catch (Exception) { }
            return "";
        }

        /// <summary>本机厂商（LENOVO / Dell Inc. / ASUSTeK ...）</summary>
        public static string ReadVendor()
        {
            // Win32_ComputerSystemProduct 只有 Vendor，没有 Manufacturer；
            // 写成多字段会把整条查询弄成非法语句，异常被吞后返回空。
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT Vendor FROM Win32_ComputerSystemProduct");
                foreach (System.Management.ManagementObject mo in searcher.Get())
                {
                    string v = Convert.ToString(mo["Vendor"]).Trim();
                    if (v.Length > 0) return v;
                }
            }
            catch (Exception) { }

            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT Manufacturer FROM Win32_ComputerSystem");
                foreach (System.Management.ManagementObject mo in searcher.Get())
                {
                    string v = Convert.ToString(mo["Manufacturer"]).Trim();
                    if (v.Length > 0) return v;
                }
            }
            catch (Exception) { }

            return "";
        }

        // ---------- 2. 机型档案 ----------
        public static LenovoMachine GetMachine(string sn)
        {
            if (string.IsNullOrEmpty(sn)) return null;
            try
            {
                string json = Get(ApiBase + "/machine/getmachineinfo?sn=" + Uri.EscapeDataString(sn));
                var root = Parse(json);
                if (S(root, "statusCode") != "200") return null;

                object dataObj;
                if (!root.TryGetValue("data", out dataObj) || dataObj == null) return null;
                var data = dataObj as Dictionary<string, object>;
                if (data == null) return null;

                var m = new LenovoMachine();
                m.MachineNo = S(data, "MachineNo");
                m.MachineName = S(data, "MachineName");
                m.MTM = S(data, "MTM");
                m.ProductModel = S(data, "ProductModel");
                m.ProductSeries = S(data, "ProductSeries");
                m.CataLogID = S(data, "CataLogID");
                m.CataLogName = S(data, "CataLogName");
                m.ProductDate = S(data, "ProductDate");
                m.SaleArea = S(data, "SaleArea");
                return m;
            }
            catch (Exception ex)
            {
                Log.Add("机型查询失败: " + ex.Message);
                return null;
            }
        }

        // ---------- 3. 官方驱动清单 ----------
        public static List<LenovoDriver> GetDrivers(string sn)
        {
            var list = new List<LenovoDriver>();
            if (string.IsNullOrEmpty(sn)) return list;
            try
            {
                string json = Get(ApiBase + "/drive/drive_listnew?searchKey=" + Uri.EscapeDataString(sn));
                var root = Parse(json);
                if (S(root, "statusCode") != "200") { Log.Add("驱动清单 statusCode=" + S(root, "statusCode")); return list; }

                object dataObj;
                if (!root.TryGetValue("data", out dataObj) || dataObj == null) return list;
                var data = dataObj as Dictionary<string, object>;
                if (data == null) return list;

                object partsObj;
                if (!data.TryGetValue("partList", out partsObj) || partsObj == null) return list;

                var parts = partsObj as System.Collections.ArrayList;
                if (parts == null) return list;

                foreach (var pv in parts)
                {
                    var part = pv as Dictionary<string, object>;
                    if (part == null) continue;
                    string partName = S(part, "PartName");

                    object drvObj;
                    if (!part.TryGetValue("drivelist", out drvObj) || drvObj == null) continue;
                    var drvs = drvObj as System.Collections.ArrayList;
                    if (drvs == null) continue;

                    foreach (var dv in drvs)
                    {
                        var d = dv as Dictionary<string, object>;
                        if (d == null) continue;
                        var ld = new LenovoDriver();
                        ld.PartName = partName;
                        ld.DriverName = S(d, "DriverName");
                        ld.Version = S(d, "Version");
                        ld.HardwareId = S(d, "HardwareId");
                        ld.FileName = S(d, "FileName");
                        ld.FileSize = S(d, "FileSize");
                        ld.FilePath = S(d, "FilePath");
                        ld.InstallCode = S(d, "InstallCode");
                        ld.DriverCode = S(d, "DriverCode");
                        ld.IssuedDate = S(d, "DriverIssuedDateTime");
                        ld.CoreVersion = S(d, "CoreVersion");
                        list.Add(ld);
                    }
                }
                Log.Add("官网清单: " + list.Count + " 条 / " + parts.Count + " 个部件");
            }
            catch (Exception ex)
            {
                Log.Add("驱动清单查询失败: " + ex.Message);
            }
            return list;
        }

        // ---------- 4. 本机设备 ----------
        public static List<HwDevice> QueryDevices()
        {
            var list = new List<HwDevice>();
            try
            {
                const string q = "SELECT DeviceName, DeviceID, DeviceClass, DriverProviderName, DriverVersion, DriverDate " +
                                 "FROM Win32_PnPSignedDriver";
                using var searcher = new System.Management.ManagementObjectSearcher(q);
                foreach (System.Management.ManagementObject mo in searcher.Get())
                {
                    var d = new HwDevice();
                    d.Name = Convert.ToString(mo["DeviceName"]).Trim();
                    d.DeviceId = Convert.ToString(mo["DeviceID"]).Trim();
                    d.PnpClass = Convert.ToString(mo["DeviceClass"]).Trim();
                    d.Provider = Convert.ToString(mo["DriverProviderName"]).Trim();
                    d.Version = Convert.ToString(mo["DriverVersion"]).Trim();
                    string dt = Convert.ToString(mo["DriverDate"]).Trim();
                    d.Date = dt.Length >= 8 ? dt.Substring(0, 4) + "-" + dt.Substring(4, 2) + "-" + dt.Substring(6, 2) : dt;
                    if (d.Name.Length > 0) list.Add(d);
                }
            }
            catch (Exception ex) { Log.Add("设备查询失败: " + ex.Message); }
            return list;
        }

        // ---------- 5. 硬件 ID 匹配 ----------
        /// <summary>部件名 → 本机 PNPClass（用于过滤误配）</summary>
        private static string[] ClassesFor(string partName)
        {
            switch (partName)
            {
                case "独立显卡":
                case "集成显卡": return new[] { "Display" };
                case "音频":
                case "声卡": return new[] { "MEDIA", "AudioEndpoint" };
                case "有线网卡":
                case "无线网卡": return new[] { "Net" };
                case "蓝牙": return new[] { "Bluetooth" };
                case "摄像头": return new[] { "Image", "MEDIA", "Camera" };
                case "读卡器": return new[] { "SCSIAdapter", "USB", "SDHost" };
                case "主板":
                case "英特尔管理引擎接口":
                case "英特尔动态平台和热框架驱动":
                case "串行总线接口":
                case "电源管理": return new[] { "System" };
                case "驱动程序": return new[] { "Monitor", "System" };
                // null（不是空数组）= 没见过的部件类别，调用方据此决定是否放弃厂商兜底
                default: return null;
            }
        }

        private static bool ClassOk(HwDevice d, string partName)
        {
            var cls = ClassesFor(partName);
            // null = 未知类别。走到这里说明硬件 ID 已经精确命中，放行是对的；
            // 厂商兜底那条路已在 MatchByVendor 里单独拦截了。
            if (cls == null) return true;
            if (cls.Length == 0) return true;
            foreach (var c in cls)
                if (string.Equals(d.PnpClass, c, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>官网 HardwareId 形如 "8086_7A00,0BDA_5852,VPC2004,INTC_1041"；本机 DeviceID 形如 "PCI\VEN_8086&amp;DEV_7A00&amp;..."</summary>
        public static List<HwDevice> MatchByHardwareId(List<HwDevice> devices, string hwIds)
        {
            var hits = new List<HwDevice>();
            if (string.IsNullOrEmpty(hwIds)) return hits;

            foreach (var raw in hwIds.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string token = raw.Trim();
                if (token.Length < 3) continue;
                if (token.IndexOf("//", StringComparison.Ordinal) >= 0) continue;   // EEE//_ENUM 这类噪声

                string[] ab = token.Split('_');
                if (ab.Length == 2)
                {
                    string a = ab[0].Trim().ToUpperInvariant();
                    string b = ab[1].Trim().ToUpperInvariant();
                    if (a.Length == 4 && b.Length == 4)
                    {
                        // 先当 PCI（VEN/DEV），再当 USB（VID/PID）
                        string pci = "VEN_" + a + "&DEV_" + b;
                        string usb = "VID_" + a + "&PID_" + b;
                        foreach (var d in devices)
                        {
                            if (d.DeviceId.IndexOf(pci, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                d.DeviceId.IndexOf(usb, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                if (!hits.Contains(d)) hits.Add(d);
                            }
                        }
                    }
                    else
                    {
                        // INTC_1041 / LTCN_0001 这种非 PCI 写法：拼起来在名字或 ID 里找
                        string joined = a + b;
                        foreach (var d in devices)
                        {
                            if (d.DeviceId.IndexOf(joined, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                if (!hits.Contains(d)) hits.Add(d);
                            }
                        }
                    }
                    continue;
                }

                // 单个 token：VPC2004 / LHK2019 / AUO98AC
                foreach (var d in devices)
                {
                    if (d.DeviceId.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (!hits.Contains(d)) hits.Add(d);
                    }
                }
            }
            return hits;
        }

        private static readonly string[] VendorKeys =
            { "Realtek", "Intel", "Genesys", "Sunplus", "NVIDIA", "Lenovo", "Sunia", "AUO", "BOE", "AMD" };

        /// <summary>官网条目没给硬件 ID 时，用驱动名里的厂商词在同类别设备里兜底</summary>
        public static List<HwDevice> MatchByVendor(List<HwDevice> devices, string driverName, string partName)
        {
            var hits = new List<HwDevice>();

            // 没见过的部件类别：厂商兜底会把同厂商的设备全算命中（Intel 一家就有集显/网卡/蓝牙）。
            // 宁愿这条显示"待确认"让用户自己判断，也不能瞎配。
            if (ClassesFor(partName) == null)
            {
                Log.Add("[匹配] 未知部件类别，放弃厂商兜底：" + partName);
                return hits;
            }

            string name = driverName ?? "";
            foreach (var v in VendorKeys)
            {
                if (name.IndexOf(v, StringComparison.OrdinalIgnoreCase) < 0) continue;
                foreach (var d in devices)
                {
                    if (!ClassOk(d, partName)) continue;
                    if (d.Name.IndexOf(v, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        d.Provider.IndexOf(v, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (!hits.Contains(d)) hits.Add(d);
                    }
                }
                if (hits.Count > 0) break;
            }
            return hits;
        }

        /// <summary>按驱动条目找本机匹配设备（供安装后校验用）</summary>
        public static List<HwDevice> MatchFor(LenovoDriver d, List<HwDevice> devices)
        {
            if (string.IsNullOrEmpty(d.HardwareId))
                return MatchByVendor(devices, d.DriverName, d.PartName);

            var list = new List<HwDevice>();
            foreach (var h in MatchByHardwareId(devices, d.HardwareId))
                if (ClassOk(h, d.PartName)) list.Add(h);
            return list;
        }

        // ---------- 6. 版本比较 ----------
        private static int[] Nums(string s)
        {
            if (string.IsNullOrEmpty(s)) return new int[0];
            var ms = Regex.Matches(s, @"\d+");
            var arr = new int[ms.Count];
            for (int i = 0; i < ms.Count; i++)
            {
                int v;
                if (int.TryParse(ms[i].Value, out v)) arr[i] = v; else arr[i] = 0;
            }
            return arr;
        }

        /// <summary>
        /// 逐段比较版本号；返回 1=官方更高, 0=相同, -1=官方更低。
        /// 缺的段按 0 补齐（左边对齐），不能从尾部对齐：
        /// 尾部对齐会让 "2.0.0" 和 "1.9.9.9" 比成 2 vs 9，误判官网更低。
        /// </summary>
        private static int CompareNumArrays(int[] off, int[] loc)
        {
            if (off.Length == 0 || loc.Length == 0) return int.MinValue;

            int n = Math.Max(off.Length, loc.Length);
            for (int i = 0; i < n; i++)
            {
                int a = i < off.Length ? off[i] : 0;
                int b = i < loc.Length ? loc[i] : 0;
                if (a != b) return a > b ? 1 : -1;
            }
            return 0;
        }

        /// <summary>
        /// 从版本串里剥出纯点分版本号，丢掉厂商名和芯片型号。
        /// "RTL8852CE_6001.16.126.323" → "6001.16.126.323"
        /// 否则芯片型号（8852）会被当成版本第一段，永远比本机"高"。
        /// </summary>
        private static string VersionPart(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var ms = Regex.Matches(s, @"\d+(?:\.\d+)+");
            if (ms.Count > 0) return ms[ms.Count - 1].Value;
            return s;
        }

        /// <summary>取官网版本串里最贴近本机的那一段比较结果</summary>
        public static int CompareVersions(string official, string local)
        {
            if (string.IsNullOrEmpty(official) || string.IsNullOrEmpty(local)) return int.MinValue;

            // 官网版本串有两层结构：
            //   ';' 分隔同一驱动的多个版本   "2.10.1072.3003;2.10.1072.3006"  → 该提示更新
            //   '/' 分隔同一驱动的硬件变体   "RTL8852CE_x/RTL8852BE_y"        → 命中任一即最新
            // 先剥掉厂商名/芯片型号，再逐变体比较；只要存在更高的版本就不能判"已最新"。
            string[] candidates = official.Split(new[] { ';', '|', '，' }, StringSplitOptions.RemoveEmptyEntries);
            int best = int.MinValue;
            bool matched = false;

            foreach (var c in candidates)
            {
                foreach (var v in c.Split('/'))
                {
                    int r = CompareNumArrays(Nums(VersionPart(v)), Nums(local));
                    if (r == int.MinValue) continue;
                    if (r == 0) matched = true;
                    if (best == int.MinValue || r > best) best = r;
                }
            }

            // 命中某个变体，且没有更高版本 → 已最新
            if (matched && (best == int.MinValue || best <= 0)) return 0;
            return best;
        }

        // ---------- 7. 总比对 ----------
        public static string ReadBiosVersion()
        {
            try
            {
                using var s = new System.Management.ManagementObjectSearcher(
                    "SELECT SMBIOSBIOSVersion, Version FROM Win32_BIOS");
                foreach (System.Management.ManagementObject mo in s.Get())
                {
                    string v = Convert.ToString(mo["SMBIOSBIOSVersion"]).Trim();
                    if (v.Length == 0) v = Convert.ToString(mo["Version"]).Trim();
                    return v;
                }
            }
            catch (Exception) { }
            return "";
        }

        /// <summary>读 Intel ME 固件版本（root\Intel_ME → ME_System.FWVersion）</summary>
        public static string ReadMeFirmware()
        {
            try
            {
                using (var s = new ManagementObjectSearcher(@"root\Intel_ME",
                           "SELECT FWVersion FROM ME_System"))
                using (var col = s.Get())
                {
                    foreach (ManagementBaseObject o in col)
                    {
                        object v = o["FWVersion"];
                        if (v != null)
                        {
                            string t = v.ToString().Trim();
                            if (t.Length > 0) return t;
                        }
                    }
                }
            }
            catch (Exception ex) { DriverSetup.Log.Swallow(ex, "LenovoApi:553"); }
            return "";
        }

        public static List<CompareResult> Compare(List<LenovoDriver> drivers, List<HwDevice> devices)
        {
            return Compare(drivers, devices, ReadBiosVersion());
        }

        public static List<CompareResult> Compare(List<LenovoDriver> drivers, List<HwDevice> devices, string biosVersion)
        {
            var results = new List<CompareResult>();

            foreach (var d in drivers)
            {
                var r = new CompareResult();
                r.Driver = d;

                // ---- BIOS：和本机 SMBIOS 版本比 ----
                if (d.PartName == "BIOS")
                {
                    r.LocalVersion = biosVersion;
                    r.MatchedDevice = "主板 BIOS / EC";
                    int cmp = CompareVersions(d.Version, biosVersion);
                    if (cmp > 0) { r.Status = "可更新"; r.StatusRank = 1; }
                    else if (cmp == 0) { r.Status = "已最新"; r.StatusRank = 0; }
                    else if (cmp == int.MinValue) { r.Status = "需人工"; r.StatusRank = 5; }
                    else { r.Status = "比官网新"; r.StatusRank = 3; }
                    results.Add(r);
                    continue;
                }

                // ---- ME 固件：本机版本不易读，只展示 ----
                if (d.PartName == "固件")
                {
                    r.LocalVersion = ReadMeFirmware();
                    r.MatchedDevice = "Intel ME 固件";
                    r.Status = "固件·需人工";
                    r.StatusRank = 5;
                    results.Add(r);
                    continue;
                }

                // ---- 纯应用：不比对版本 ----
                if (d.PartName == "应用程序")
                {
                    r.Status = "应用·不比对";
                    r.StatusRank = 6;
                    results.Add(r);
                    continue;
                }

                // ---- 硬件类 ----
                List<HwDevice> hits;
                if (string.IsNullOrEmpty(d.HardwareId))
                {
                    hits = MatchByVendor(devices, d.DriverName, d.PartName);
                }
                else
                {
                    hits = new List<HwDevice>();
                    foreach (var h in MatchByHardwareId(devices, d.HardwareId))
                        if (ClassOk(h, d.PartName)) hits.Add(h);
                }

                if (hits.Count == 0)
                {
                    r.Status = string.IsNullOrEmpty(d.HardwareId) ? "待确认" : "非本机配置";
                    r.StatusRank = 4;
                    results.Add(r);
                    continue;
                }

                // 多台设备命中时按"最需要更新"的那台判定：
                // 以前是优先选「版本与官网一致」的设备，等于挑最好的那台报平安，
                // 另一台旧驱动永远不提示。现在取差异最大（官方更高）的那台作为代表。
                HwDevice best = hits[0];
                int worst = int.MinValue;
                foreach (var h in hits)
                {
                    int c = CompareVersions(d.Version, h.Version);
                    if (c == int.MinValue) continue;
                    if (worst == int.MinValue || c > worst) { worst = c; best = h; }
                }

                r.MatchedDevice = best.Name;
                r.LocalVersion = best.Version;

                if (string.IsNullOrEmpty(best.Version))
                {
                    r.Status = "未安装";
                    r.StatusRank = 2;
                    results.Add(r);
                    continue;
                }

                int cmp2 = CompareVersions(d.Version, best.Version);
                if (cmp2 == int.MinValue) { r.Status = "需人工"; r.StatusRank = 5; }
                else if (cmp2 > 0) { r.Status = "可更新"; r.StatusRank = 1; }
                else if (cmp2 == 0) { r.Status = "已最新"; r.StatusRank = 0; }
                else { r.Status = "比官网新"; r.StatusRank = 3; }

                results.Add(r);
            }

            return results.OrderBy(x => x.StatusRank)
                          .ThenBy(x => x.Driver.PartName)
                          .ThenBy(x => x.Driver.DriverName)
                          .ToList();
        }
    }
}
