using System;
using System.IO;
using System.Reflection;
using System.Drawing;
using System.Windows.Forms;

namespace DriverSetup
{
    internal static class Program
    {
        /// <summary>全局窗口图标（从嵌入的 app.ico 载入）</summary>
        public static Icon AppIcon;

        [STAThread]
        private static void RunPerfProbe()
        {
            var sb = new System.Text.StringBuilder();
            using (var sampler = new PerfSampler())
            using (var burner = new CpuBurner())
            {
                sampler.Probe();
                sb.AppendLine("admin=" + sampler.IsAdmin + " cpuTemp=" + sampler.CpuTempAvailable +
                              " gpu=" + sampler.GpuOk + " gpuName=" + sampler.GpuName);
                sb.AppendLine("sec   cpuT  cpuMHz  cpuL%  gpuT  gpuMHz  gpuW");
                var sw = System.Diagnostics.Stopwatch.StartNew();
                Action<double> take = delegate (double sec)
                {
                    var p = sampler.Sample(sec);
                    sb.AppendLine(string.Format(
                        "{0,5:F1} cpuT={1,5:F1} cpuMHz={2,5:F0} cpuL={3,3:F0}% gpuT={4,4:F1} gpuMHz={5,5:F0} gpuW={6,5:F1} fan={7} cpuW={8} cpuV={9}",
                        p.Sec, p.CpuTemp, p.CpuClock, p.CpuLoad, p.GpuTemp, p.GpuClock, p.GpuPower,
                        double.IsNaN(p.FanRpm) ? "NaN" : p.FanRpm.ToString("F0"),
                        double.IsNaN(p.CpuPower) ? "NaN" : p.CpuPower.ToString("F1"),
                        double.IsNaN(p.CpuVoltage) ? "NaN" : p.CpuVoltage.ToString("F2")));
                };
                for (int i = 0; i < 3; i++) { take(sw.Elapsed.TotalSeconds); System.Threading.Thread.Sleep(1000); }
                burner.Start(Environment.ProcessorCount);
                sb.AppendLine("--- burn start ---");
                for (int i = 0; i < 6; i++) { take(sw.Elapsed.TotalSeconds); System.Threading.Thread.Sleep(1000); }
                burner.Stop();
                sb.AppendLine("--- burn end ---");
                for (int i = 0; i < 2; i++) { take(sw.Elapsed.TotalSeconds); System.Threading.Thread.Sleep(1000); }
            }
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "perf-test.txt"), sb.ToString());
        }

        private static void RunGpuProbe()
        {
            var sb = new System.Text.StringBuilder();
            var f = new Form();
            f.FormBorderStyle = FormBorderStyle.None;
            f.ClientSize = new Size(1280, 720);
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(60, 60);
            f.BackColor = Color.Black;

            var gb = new GpuBurner();
            var ps = new PerfSampler();
            try
            {
                f.Show();
                Application.DoEvents();
                System.Threading.Thread.Sleep(300);
                ps.Probe();

                gb.Start(f.Handle, f.ClientSize.Width, f.ClientSize.Height);

                for (int i = 1; i <= 12; i++)
                {
                    System.Threading.Thread.Sleep(1000);
                    Application.DoEvents();
                    var s2 = ps.Sample(i);
                    sb.AppendLine(string.Format(
                        "{0,2}s  gpuLoad={1,5:F0}%  gpuTemp={2,4:F0}C  gpuClock={3,5:F0}MHz  fps={4,6:F0}  err={5}",
                        i, s2.GpuLoad, s2.GpuTemp, s2.GpuClock, gb.Fps, string.IsNullOrEmpty(gb.Error) ? "-" : gb.Error));
                }
                gb.Stop();
            }
            catch (Exception ex) { sb.AppendLine("EX " + ex); }
            finally
            {
                try { gb.Dispose(); } catch (Exception ex) { Log.Swallow(ex, "Program:83"); }
                try { ps.Dispose(); } catch (Exception ex) { Log.Swallow(ex, "Program:84"); }
                try { f.Close(); } catch (Exception ex) { Log.Swallow(ex, "Program:85"); }
            }
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gpu-test.txt"), sb.ToString());
        }

        [STAThread]

        private static void Main(string[] args)
        {
            // 探针：DRV_TEST_PERF=1 采样 11 秒（后 6 秒烤机）写文件后退出
            if (Environment.GetEnvironmentVariable("DRV_TEST_GPU") == "1")
            {
                try { RunGpuProbe(); }
                catch (Exception ex) { System.IO.File.WriteAllText("gpu-error.txt", ex.ToString()); }
                return;
            }
            if (Environment.GetEnvironmentVariable("DRV_TEST_PERF") == "1")
            {
                try { RunPerfProbe(); }
                catch (Exception ex) { System.IO.File.WriteAllText("perf-error.txt", ex.ToString()); }
                return;
            }

            // 探针：DRV_TEST_LHM=1 时把传感器清单写到文件后退出
            if (Environment.GetEnvironmentVariable("DRV_TEST_LHM") == "1")
            {
                var sb = new System.Text.StringBuilder();
                var c = new LibreHardwareMonitor.Hardware.Computer();
                c.IsCpuEnabled = true;
                c.IsGpuEnabled = true;
                c.IsMemoryEnabled = false;
                c.IsMotherboardEnabled = true;
                c.IsControllerEnabled = false;
                c.IsStorageEnabled = false;
                c.IsNetworkEnabled = false;
                try
                {
                    c.Open();
                    foreach (var hw in c.Hardware)
                    {
                        hw.Update();
                        sb.AppendLine("HW  " + hw.HardwareType + " | " + hw.Name);
                        foreach (var sub in hw.SubHardware)
                        {
                            sub.Update();
                            sb.AppendLine("  SUB " + sub.HardwareType + " | " + sub.Name);
                            foreach (var ss in sub.Sensors)
                                sb.AppendLine("     " + ss.SensorType + " " + ss.Name + " = " + ss.Value);
                        }
                        foreach (var s2 in hw.Sensors)
                            sb.AppendLine("     " + s2.SensorType + " " + s2.Name + " = " + s2.Value);
                    }
                }
                catch (Exception ex) { sb.AppendLine("ERR " + ex); }
                try { c.Close(); } catch (Exception ex) { Log.Swallow(ex, "Program:169"); }
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lhm-test.txt"), sb.ToString());
                return;
            }

            // --page N 指定起始分页（提权后环境变量会丢，所以走参数）
            // 必须放在所有分支之前：原来它写在 --burn 分支里，普通启动时参数被忽略
            if (args != null)
            {
                int pi = Array.IndexOf(args, "--page");
                if (pi >= 0 && pi + 1 < args.Length)
                    Environment.SetEnvironmentVariable("DRV_START_PAGE", args[pi + 1]);
            }

            bool burnMode = Environment.GetEnvironmentVariable("DRV_START_BURN") == "1" ||
                            (args != null && Array.IndexOf(args, "--burn") >= 0);
            bool burnAuto = Environment.GetEnvironmentVariable("DRV_BURN_AUTO") == "1" ||
                            (args != null && Array.IndexOf(args, "--burn-auto") >= 0);
            if (burnMode)
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                using (var bf = new BurnHostForm())
                {
                    bf.AutoStartNow = burnAuto || args != null && Array.IndexOf(args, "--burn-auto") >= 0;
                    bf.ShowDialog();
                }
                return;
            }

            Application.EnableVisualStyles();
            try
            {
                using (var st = Assembly.GetExecutingAssembly()
                           .GetManifestResourceStream("DriverSetup.app.ico"))
                {
                    if (st != null) AppIcon = new Icon(st);   // 含全部尺寸，系统自选
                }
            }
            catch (Exception ex) { Log.Swallow(ex, "Program:210"); }

            Application.SetCompatibleTextRenderingDefault(false);

            // 启动期异常落到文件，避免"窗口 237x39"这类静默失败
            // 注意：SetUnhandledExceptionMode 必须在任何窗口句柄创建之前调用
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
            {
                Dump(e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Dump(e.ExceptionObject as Exception);
            };

            // --refresh：直接开刷新率测试（调试/验证用）。
            // 放在异常处理安装之后，这样这个入口自己崩了也会落盘；
            // 必须晚于 SetUnhandledExceptionMode（那行要求句柄未创建）但早于主窗体。
            if (args != null && Array.IndexOf(args, "--refresh") >= 0)
            {
                using (var rf = new RefreshTestForm()) rf.ShowDialog();
                return;
            }

            // 进度窗放在这之后：窗口一旦创建，上面那行 SetUnhandledExceptionMode 就调不了了
            SplashForm splash = new SplashForm();
            splash.Show();
            splash.SetProgress(4, "正在启动…");

            try
            {
                var main = new MainForm(splash);
                splash.SetProgress(100, "就绪");
                splash.Close();
                splash = null;
                Application.Run(main);
            }
            catch (Exception ex)
            {
                Dump(ex);
                throw;
            }
        }

        private static void Dump(Exception ex)
        {
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "drvsetup-crash.txt"),
                    DateTime.Now.ToString("HH:mm:ss") + " " + (ex == null ? "(null)" : ex.ToString())
                    + Environment.NewLine + Environment.NewLine);
            }
            catch (Exception) { }
        }
    }
}
