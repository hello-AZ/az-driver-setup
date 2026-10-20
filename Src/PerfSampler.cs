using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;

namespace DriverSetup
{
    /// <summary>一次采样点</summary>
    public class PerfSample
    {
        public double Sec;                       // 距开始秒数
        public DateTime Time;
        public double CpuTemp = double.NaN;      // °C
        public double CpuClock = double.NaN;     // MHz
        public double CpuLoad = double.NaN;      // %
        public double GpuTemp = double.NaN;      // °C
        public double GpuClock = double.NaN;     // MHz
        public double GpuPower = double.NaN;     // W
        public double GpuLoad = double.NaN;      // %
        public double FanRpm = double.NaN;       // 风扇转速（笔记本多为 NaN）
        public double GpuVramMB = double.NaN;    // 显存占用 MB
        public double CoreMhzMin = double.NaN;   // 各核最低频率
        public double CoreMhzMax = double.NaN;   // 各核最高频率
        public double CoreMhzAvg = double.NaN;   // 各核平均频率
        public double CpuPower = double.NaN;     // CPU 封装功耗 W
        public double CpuVoltage = double.NaN;   // CPU 电压 V
    }

    /// <summary>
    /// 性能采样：CPU 温度/频率走 LibreHardwareMonitor（温度需管理员），
    /// CPU 频率另用 Windows 性能计数器兜底，GPU 全部走 LHM（底层 NVML，无需管理员）。
    /// </summary>
    public class PerfSampler : IDisposable
    {
        private Computer _comp;
        private IHardware _cpu;
        private IHardware _gpu;
        private bool _gpuDumped;
        private volatile bool _disposed;
        private PerformanceCounter _perfPct;
        private PerformanceCounter _loadPct;
        private int _baseMhz = 2200;
        private readonly List<IHardware> _fanSources = new List<IHardware>();
        private double _lastFanQuery = -99;
        private int _lenovoFan = -1;
        private PerformanceCounter[] _corePerf;
        private int _coreCount;

        public bool IsAdmin { get; private set; }
        public bool CpuTempAvailable { get; private set; }
        /// <summary>是否已经跑过至少一次采样（未采样时不能断言温度读不到）</summary>
        public bool HasSampled { get; private set; }
        public bool GpuOk { get; private set; }
        public string CpuName { get; private set; } = "CPU";
        public string GpuName { get; private set; } = "GPU";

        public PerfSampler()
        {
            try
            {
                var id = System.Security.Principal.WindowsIdentity.GetCurrent();
                var pr = new System.Security.Principal.WindowsPrincipal(id);
                IsAdmin = pr.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch (Exception ex) { Log.Swallow(ex, "PerfSampler:67"); }

            try
            {
                using (var m = new ManagementObjectSearcher("SELECT MaxClockSpeed, Name FROM Win32_Processor"))
                using (var col = m.Get())
                {
                    foreach (ManagementBaseObject o in col)
                    {
                        object mc = o["MaxClockSpeed"];
                        if (mc != null) int.TryParse(mc.ToString(), out _baseMhz);
                        object nm = o["Name"];
                        if (nm != null) CpuName = nm.ToString().Trim();
                        break;
                    }
                }
            }
            catch (Exception ex) { Log.Swallow(ex, "PerfSampler:84"); }

            try
            {
                _perfPct = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total");
                _perfPct.NextValue();
            }
            catch { _perfPct = null; }

            try
            {
                _loadPct = new PerformanceCounter("Processor Information", "% Processor Time", "_Total");
                _loadPct.NextValue();
            }
            catch { _loadPct = null; }

            // 每核频率：走性能计数器，不依赖管理员
            try
            {
                _coreCount = Environment.ProcessorCount;
                _corePerf = new PerformanceCounter[_coreCount];
                for (int i = 0; i < _coreCount; i++)
                {
                    try
                    {
                        _corePerf[i] = new PerformanceCounter("Processor Information",
                            "% Processor Performance", "0," + i);
                        _corePerf[i].NextValue();     // 预热
                    }
                    catch { _corePerf[i] = null; }
                }
            }
            catch (Exception ex) { Log.Swallow(ex, "PerfSampler:116"); }

            try
            {
                _comp = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    IsMemoryEnabled = false,
                    IsMotherboardEnabled = true,
                    IsControllerEnabled = true,
                    IsStorageEnabled = false,
                    IsNetworkEnabled = false,
                };
                _comp.Open();

                // 初始化后立刻把 GPU 传感器名落盘，方便核对命名（不依赖是否开始烤机）
                try
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var hw in _comp.Hardware)
                    {
                        sb.AppendLine("== " + hw.HardwareType + " : " + hw.Name);
                        hw.Update();
                        foreach (var s in hw.Sensors)
                            sb.AppendLine("   " + s.SensorType + " | " + s.Name + " = " + (s.Value.HasValue ? s.Value.Value.ToString() : "null"));
                    }
                    System.IO.File.WriteAllText(
                        System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gpu-sensors.txt"),
                        sb.ToString());
                }
                catch (Exception ex)
                {
                    try
                    {
                        System.IO.File.WriteAllText(
                            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gpu-sensors.txt"),
                            "dump 失败: " + ex);
                    }
                    catch (Exception ex2) { Log.Swallow(ex2, "PerfSampler:155"); }
                }

                foreach (var hw in _comp.Hardware)
                {
                    if (hw.HardwareType == HardwareType.Cpu) _cpu = hw;
                    else if (hw.HardwareType == HardwareType.GpuNvidia ||
                             hw.HardwareType == HardwareType.GpuAmd ||
                             hw.HardwareType == HardwareType.GpuIntel)
                    {
                        _gpu = hw;
                        // 枚举到就算检测到，不等第一次采样（否则状态行一直显示"未检测到"）
                        try { GpuName = hw.Name; GpuOk = true; } catch (Exception ex) { Log.Swallow(ex, "PerfSampler:167"); }
                    }

                    // 风扇可能在主板或嵌入式控制器下
                    try
                    {
                        hw.Update();
                        foreach (var sen in hw.Sensors)
                            if (sen.SensorType == SensorType.Fan) { _fanSources.Add(hw); break; }

                        foreach (var sub in hw.SubHardware)
                        {
                            sub.Update();
                            foreach (var sen in sub.Sensors)
                                if (sen.SensorType == SensorType.Fan) { _fanSources.Add(sub); break; }
                        }
                    }
                    catch (Exception ex) { Log.Swallow(ex, "PerfSampler:184"); }
                }
            }
            catch (Exception ex) { Log.Swallow(ex, "PerfSampler:187"); }
        }

        /// <summary>探一次，确认 CPU 温度到底能不能读</summary>
        public void Probe()
        {
            if (_cpu == null) return;
            try
            {
                _cpu.Update();
                foreach (var s in _cpu.Sensors)
                {
                    if (s.SensorType == SensorType.Temperature && s.Value != null)
                    {
                        CpuTempAvailable = true;
                        break;
                    }
                }
            }
            catch (Exception ex) { Log.Swallow(ex, "PerfSampler:206"); }

            if (_gpu != null)
            {
                try
                {
                    _gpu.Update();
                    GpuName = _gpu.Name;
                    foreach (var s in _gpu.Sensors)
                        if (s.SensorType == SensorType.Temperature && s.Value != null) { GpuOk = true; break; }
                }
                catch (Exception ex) { Log.Swallow(ex, "PerfSampler:217"); }
            }
        }

        public PerfSample Sample(double sec)
        {
            HasSampled = true;

            // 窗口关闭时 Dispose 会 _comp.Close()，采样线程可能正好在跑。
            // 用标志快速退出，避免往已释放的对象里读。
            if (_disposed) return new PerfSample();
            var p = new PerfSample { Sec = sec, Time = DateTime.Now };

            // ---- CPU（LHM）----
            if (_cpu != null)
            {
                try
                {
                    _cpu.Update();
                    double coreMax = double.NaN, coreAvg = double.NaN, c1 = double.NaN;
                    double tempFallback = double.NaN;   // 兜底：所有温度里的最高值
                    double clockFallback = double.NaN;  // 兜底：任意核心频率
                    foreach (var s in _cpu.Sensors)
                    {
                        if (s.Value == null) continue;
                        if (s.SensorType == SensorType.Temperature)
                        {
                            double v = s.Value.Value;
                            if (s.Name == "Core Max") coreMax = v;
                            else if (s.Name == "Core Average") coreAvg = v;

                            // AMD 的 LHM 传感器名是 "Core (Tctl/Tdie)" / "CCD1 (Tdie)" 之类，
                            // 跟 Intel 的 "Core Max" 对不上，硬编码名字会读到 NaN。
                            // 这里同时收集最大值兜底——_cpu 只含 CPU 传感器，不会混进主板温度。
                            if (double.IsNaN(tempFallback) || v > tempFallback) tempFallback = v;
                        }
                        else if (s.SensorType == SensorType.Clock && s.Name == "CPU Core #1")
                        {
                            c1 = s.Value.Value;
                        }
                        else if (s.SensorType == SensorType.Clock && s.Name != null &&
                                 s.Name.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            double cv = s.Value.Value;
                            if (double.IsNaN(clockFallback) || cv > clockFallback) clockFallback = cv;
                        }
                        else if (s.SensorType == SensorType.Power && s.Name != null &&
                                 s.Name.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            p.CpuPower = s.Value.Value;
                        }
                        else if (s.SensorType == SensorType.Voltage && s.Name != null &&
                                 s.Name.IndexOf("CPU Core", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            p.CpuVoltage = s.Value.Value;
                        }
                    }
                    // 优先 Intel 的 Core Max / Core Average，认不到就用兜底最大值
                    p.CpuTemp = !double.IsNaN(coreMax) ? coreMax
                              : (!double.IsNaN(coreAvg) ? coreAvg : tempFallback);
                    CpuTempAvailable = !double.IsNaN(p.CpuTemp);
                    if (!double.IsNaN(c1)) p.CpuClock = c1;
                    else if (!double.IsNaN(clockFallback)) p.CpuClock = clockFallback;
                }
                catch (Exception ex) { Log.Swallow(ex, "PerfSampler:260"); }
            }

            // ---- CPU 频率兜底（性能计数器：基频 × 性能比）----
            if (double.IsNaN(p.CpuClock) && _perfPct != null)
            {
                try
                {
                    float v = _perfPct.NextValue();
                    if (v > 0) p.CpuClock = _baseMhz * v / 100.0;
                }
                catch (Exception ex) { Log.Swallow(ex, "PerfSampler:271"); }
            }

            if (_loadPct != null)
            {
                try { p.CpuLoad = _loadPct.NextValue(); } catch (Exception ex) { Log.Swallow(ex, "PerfSampler:276"); }
            }

            // 每核频率
            if (_corePerf != null && _corePerf.Length > 0)
            {
                double mn = double.MaxValue, mx = 0, sum = 0;
                int cnt = 0;
                foreach (var pc in _corePerf)
                {
                    if (pc == null) continue;
                    try
                    {
                        float v = pc.NextValue();
                        if (v <= 0) continue;
                        double mhz = _baseMhz * v / 100.0;
                        if (mhz < mn) mn = mhz;
                        if (mhz > mx) mx = mhz;
                        sum += mhz;
                        cnt++;
                    }
                    catch (Exception ex) { Log.Swallow(ex, "PerfSampler:297"); }
                }
                if (cnt > 0)
                {
                    p.CoreMhzMin = mn;
                    p.CoreMhzMax = mx;
                    p.CoreMhzAvg = sum / cnt;
                }
            }

            // ---- 风扇：优先联想官方 WMI（每 3 秒查一次，避免 WMI 开销）----
            if (sec - _lastFanQuery >= 3)
            {
                _lastFanQuery = sec;
                int lf = 0;
                try { lf = LenovoWmi.GetFanSpeed(); } catch (Exception ex) { Log.Swallow(ex, "PerfSampler:312"); }
                if (lf > 0) _lenovoFan = lf;
            }
            if (_lenovoFan > 0) p.FanRpm = _lenovoFan;

            // ---- 风扇：LHM 兜底（台式机 SuperIO）----
            if (_fanSources.Count > 0)
            {
                try
                {
                    double maxFan = double.NaN;
                    foreach (var src in _fanSources)
                    {
                        src.Update();
                        foreach (var sen in src.Sensors)
                        {
                            if (sen.SensorType != SensorType.Fan || sen.Value == null) continue;
                            double v = sen.Value.Value;
                            if (double.IsNaN(maxFan) || v > maxFan) maxFan = v;
                        }
                    }
                    if (!double.IsNaN(maxFan) && (double.IsNaN(p.FanRpm) || maxFan > p.FanRpm))
                        p.FanRpm = maxFan;
                }
                catch (Exception ex) { Log.Swallow(ex, "PerfSampler:336"); }
            }

            // ---- GPU（LHM / NVML）----
            if (_gpu != null)
            {
                try
                {
                    _gpu.Update();

                    // 第一次采样时把所有 GPU 传感器名 dump 出来，便于核对命名
                    if (!_gpuDumped)
                    {
                        _gpuDumped = true;
                        try
                        {
                            var sb = new System.Text.StringBuilder();
                            foreach (var ss in _gpu.Sensors)
                                sb.AppendLine(ss.SensorType + " | " + ss.Name);
                            System.IO.File.WriteAllText(
                                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gpu-sensors.txt"),
                                sb.ToString());
                        }
                        catch (Exception ex) { Log.Swallow(ex, "PerfSampler:359"); }
                    }

                    foreach (var s in _gpu.Sensors)
                    {
                        if (s.Value == null) continue;
                        switch (s.SensorType)
                        {
                            case SensorType.Temperature:
                                if (s.Name == "GPU Core") p.GpuTemp = s.Value.Value;
                                break;
                            case SensorType.Clock:
                                if (s.Name == "GPU Core") p.GpuClock = s.Value.Value;
                                break;
                            case SensorType.Power:
                                if (s.Name == "GPU Package" || s.Name == "GPU Power")
                                    p.GpuPower = s.Value.Value;
                                break;
                            case SensorType.Load:
                                if (s.Name == "GPU Core") p.GpuLoad = s.Value.Value;
                                break;
                            case SensorType.SmallData:
                                if (s.Name != null && s.Name.IndexOf("Dedicated Memory Used",
                                        StringComparison.OrdinalIgnoreCase) >= 0)
                                    p.GpuVramMB = s.Value.Value;
                                break;
                        }
                    }
                }
                catch (Exception ex) { Log.Swallow(ex, "PerfSampler:388"); }
            }

            return p;
        }

        public void Dispose()
        {
            _disposed = true;
            try { if (_comp != null) _comp.Close(); } catch (Exception ex) { Log.Swallow(ex, "PerfSampler:396"); }
            try { if (_perfPct != null) _perfPct.Dispose(); } catch (Exception ex) { Log.Swallow(ex, "PerfSampler:397"); }
            try { if (_loadPct != null) _loadPct.Dispose(); } catch (Exception ex) { Log.Swallow(ex, "PerfSampler:398"); }
            if (_corePerf != null)
                foreach (var pc in _corePerf) { try { if (pc != null) pc.Dispose(); } catch (Exception ex) { Log.Swallow(ex, "PerfSampler:400"); } }
        }
    }

    /// <summary>CPU 满载负载（每核一线程，纯浮点运算）</summary>
    public class CpuBurner : IDisposable
    {
        private CancellationTokenSource _cts;
        private readonly List<Thread> _threads = new List<Thread>();

        public int ActiveThreads { get { return _threads.Count; } }

        public void Start(int threads)
        {
            Stop();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            for (int i = 0; i < threads; i++)
            {
                var t = new Thread(delegate ()
                {
                    double x = 1.0;
                    while (!token.IsCancellationRequested)
                    {
                        for (int k = 0; k < 200000; k++)
                        {
                            x = x * 1.0000001 + 0.1234567;
                        }
                        if (x > 1e12) x = 1.0;
                    }
                });
                t.IsBackground = true;
                t.Priority = ThreadPriority.Normal;
                t.Start();
                _threads.Add(t);
            }
        }

        public void Stop()
        {
            if (_cts != null)
            {
                try { _cts.Cancel(); } catch (Exception ex) { Log.Swallow(ex, "PerfSampler:443"); }
                _cts = null;
            }
            foreach (var t in _threads)
            {
                try { if (!t.Join(300)) { } } catch (Exception ex) { Log.Swallow(ex, "PerfSampler:448"); }
            }
            _threads.Clear();
        }

        public void Dispose() { Stop(); }
    }
}
