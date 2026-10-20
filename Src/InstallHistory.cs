using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace DriverSetup
{
    /// <summary>
    /// 安装台账：记录本工具装过哪些驱动包（对应哪些 oem*.inf）。
    /// 驱动清理页据此判断「这个驱动是不是我们自己装的」——没记录的就不许删。
    /// </summary>
    public static class InstallHistory
    {
        public class Entry
        {
            public string Time = "";
            public string Driver = "";
            public string Version = "";
            public string Package = "";
            public List<string> OemInfs = new List<string>();
        }

        private static string FilePath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "install-history.json"); }
        }

        // 台账是"读-改-写"三步，两个实例同时记录会互相覆盖
        private static readonly object _lock = new object();

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public static List<Entry> Load()
        {
            lock (_lock)
            {
                try
                {
                    if (!File.Exists(FilePath)) return new List<Entry>();
                    string json = File.ReadAllText(FilePath, Encoding.UTF8);
                    var list = JsonSerializer.Deserialize<List<Entry>>(json, JsonOpts);
                    return list ?? new List<Entry>();
                }
                catch { return new List<Entry>(); }
            }
        }

        public static void Record(Entry e)
        {
            lock (_lock)
            {
                try
                {
                    // 注意：Load 内部也 lock 同一把锁，但 C# 的 lock 可重入，不会死锁
                    var all = Load();
                    all.Add(e);

                    // 只保留最近 200 条
                    if (all.Count > 200) all = all.Skip(all.Count - 200).ToList();

                    File.WriteAllText(FilePath, JsonSerializer.Serialize(all, JsonOpts), Encoding.UTF8);
                }
                catch (Exception ex) { Log.Swallow(ex, "InstallHistory:72"); }
            }
        }

        /// <summary>按 oem*.inf 名字查台账；没有记录返回 null</summary>
        public static Entry FindByOem(string oemName)
        {
            if (string.IsNullOrEmpty(oemName)) return null;
            string key = oemName.Trim().ToLowerInvariant();

            foreach (var e in Load())
            {
                if (e == null || e.OemInfs == null) continue;
                if (e.OemInfs.Any(x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase)))
                    return e;
            }
            return null;
        }

        /// <summary>当前系统里所有 oem*.inf 的快照</summary>
        public static List<string> SnapshotOem()
        {
            var list = new List<string>();
            try
            {
                var psi = new ProcessStartInfo("pnputil.exe", "/enum-drivers")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = ConsoleEncoding(),
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) return list;
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(20000);

                    foreach (Match m in Regex.Matches(output, @"oem\d+\.inf", RegexOptions.IgnoreCase))
                    {
                        string v = m.Value.ToLowerInvariant();
                        if (!list.Contains(v)) list.Add(v);
                    }
                }
            }
            catch (Exception ex) { Log.Swallow(ex, "InstallHistory:117"); }
            return list;
        }

        private static Encoding ConsoleEncoding()
        {
            try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
            catch { return Encoding.Default; }
        }
    }
}
