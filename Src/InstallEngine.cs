using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DriverSetup
{
    /// <summary>备份进度</summary>
    public class BackupProgress
    {
        public string Stage = "";
        public int Done;
        public int Total;
        public string Detail = "";
    }

    public static class InstallEngine
    {
        public const string InnoSilentArgs = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-";
        private const string UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

        /// <summary>程序所在目录（下载的包、备份都放这里）</summary>
        public static string AppDir
        {
            get { return AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'); }
        }

        public static string DownloadDir
        {
            get { return Path.Combine(AppDir, "Downloads"); }
        }

        public static string BackupDir
        {
            get { return Path.Combine(AppDir, "Backup"); }
        }

        public static bool IsAdmin
        {
            get
            {
                try
                {
                    var id = WindowsIdentity.GetCurrent();
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
                }
                catch (Exception) { return false; }
            }
        }

        /// <summary>以管理员身份重启自身</summary>
        public static bool RelaunchAsAdmin()
        {
            try
            {
                var exe = Process.GetCurrentProcess().MainModule.FileName;
                var psi = new ProcessStartInfo(exe);
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                Process.Start(psi);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ---------- 下载 ----------
        public static Task DownloadAsync(string url, string dest, IProgress<int> progress)
        {
            return Task.Run(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest));

                var req = (HttpWebRequest)WebRequest.Create(url);
                req.UserAgent = UserAgent;
                req.Proxy = null;
                req.Timeout = 60000;
                req.ReadWriteTimeout = 120000;

                string tmp = dest + ".part";
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var src = resp.GetResponseStream())
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 81920))
                {
                    long total = resp.ContentLength;
                    long got = 0;
                    byte[] buf = new byte[81920];
                    int n;
                    while (src != null && (n = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        fs.Write(buf, 0, n);
                        got += n;
                        if (total > 0 && progress != null)
                            progress.Report((int)(got * 100 / total));
                    }
                }

                if (File.Exists(dest)) File.Delete(dest);
                File.Move(tmp, dest);
                if (progress != null) progress.Report(100);
            });
        }

        // ---------- 安装 ----------
        /// <summary>安装器日志路径（Inno 的 /LOG 输出到包旁的 .install.log）</summary>
        public static string LastLogPath(string exePath)
        {
            return Path.ChangeExtension(exePath, ".install.log");
        }

        /// <summary>
        /// 静默运行安装包。返回退出码：0 成功；3010 成功需重启；1641 已发起重启；其他见 DescribeExitCode。
        /// </summary>
        public static Task<int> RunInstallerAsync(string exePath, string args)
        {
            return Task.Run(() =>
            {
                string log = LastLogPath(exePath);
                string fullArgs = (string.IsNullOrEmpty(args) ? InnoSilentArgs : args) +
                                  " /LOG=\"" + log + "\"";

                var psi = new ProcessStartInfo();
                psi.FileName = exePath;
                psi.Arguments = fullArgs;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = Path.GetDirectoryName(exePath);

                using (var p = Process.Start(psi))
                {
                    if (p == null) return -1;
                    if (!p.WaitForExit(900000))   // 最多等 15 分钟
                    {
                        try { p.Kill(); } catch (Exception) { }
                        return -2;
                    }
                    return p.ExitCode;
                }
            });
        }

        public static string DescribeExitCode(int code)
        {
            switch (code)
            {
                case 0: return "安装成功";
                case 259: return "安装器已完成（pnputil 返回 259：没有需要安装的项目，通常表示同版本已存在）";
                case 3010: return "安装成功，需要重启";
                case 1641: return "安装完成，已发起重启";
                case 1602: return "用户取消了安装";
                case 1603: return "安装失败（致命错误）";
                case -1: return "启动安装程序失败";
                case -2: return "安装超时（已终止）";
                default: return "安装结束，退出码 " + code;
            }
        }

        // ---------- 备份 / 回滚 ----------
        public static int CountInstalledDrivers()
        {
            try
            {
                var psi = new ProcessStartInfo("pnputil.exe", "/enum-drivers");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.StandardOutputEncoding = Encoding.Default;

                using (var p = Process.Start(psi))
                {
                    string all = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(90000);
                    return Regex.Matches(all, @"oem\d+\.inf", RegexOptions.IgnoreCase).Count;
                }
            }
            catch (Exception) { return 0; }
        }

        private static int CountSubDirs(string dir)
        {
            try { return Directory.GetDirectories(dir).Length; }
            catch (Exception) { return 0; }
        }

        /// <summary>
        /// 用 pnputil 导出本机全部第三方驱动到 Backup\时间戳。
        /// pnputil 自身不报进度，这里靠轮询目标目录的子目录数估算。
        /// </summary>
        public static Task<string> BackupDriversAsync(IProgress<BackupProgress> progress, CancellationToken ct)
        {
            return Task.Run((Func<string>)(() =>
            {
                if (progress != null)
                    progress.Report(new BackupProgress { Stage = "读取驱动清单" });

                int total = CountInstalledDrivers();

                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string dir = Path.Combine(BackupDir, stamp);
                Directory.CreateDirectory(dir);

                if (progress != null)
                    progress.Report(new BackupProgress { Stage = "正在导出驱动", Done = 0, Total = total, Detail = "" });

                var psi = new ProcessStartInfo("pnputil.exe", "/export-driver * \"" + dir + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = Encoding.Default;
                psi.StandardErrorEncoding = Encoding.Default;

                using (var p = Process.Start(psi))
                {
                    // 异步抽干管道，避免缓冲区满导致 pnputil 卡住
                    var outTask = p.StandardOutput.ReadToEndAsync();
                    var errTask = p.StandardError.ReadToEndAsync();

                    while (!p.WaitForExit(400))
                    {
                        if (ct.IsCancellationRequested)
                        {
                            try { p.Kill(); } catch (Exception) { }
                            break;
                        }
                        int done = CountSubDirs(dir);
                        if (progress != null)
                            progress.Report(new BackupProgress { Stage = "正在导出驱动", Done = done, Total = total, Detail = "" });
                    }

                    if (!p.HasExited)
                    {
                        try { p.Kill(); } catch (Exception) { }
                    }

                    ct.ThrowIfCancellationRequested();

                    int final = CountSubDirs(dir);
                    if (progress != null)
                        progress.Report(new BackupProgress
                        {
                            Stage = "备份完成",
                            Done = final,
                            Total = Math.Max(total, final),
                            Detail = final + " 个驱动"
                        });
                }

                return dir;
            }), ct);
        }

        /// <summary>把备份目录里的 INF 全部重新安装</summary>
        public static Task<int> RestoreDriversAsync(string dir, IProgress<string> log)
        {
            return Task.Run(() =>
            {
                if (!Directory.Exists(dir)) return -3;
                int total = 0, ok = 0;
                var infs = Directory.GetFiles(dir, "*.inf", SearchOption.AllDirectories);
                foreach (var inf in infs)
                {
                    total++;
                    var psi = new ProcessStartInfo("pnputil.exe",
                        "/add-driver \"" + inf + "\" /install");
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    using (var p = Process.Start(psi))
                    {
                        p.WaitForExit(120000);
                        if (p.ExitCode == 0) ok++;
                    }
                    if (log != null) log.Report("回滚 " + Path.GetFileName(inf) + " → " + (ok > 0 ? "ok" : "fail"));
                }
                if (log != null) log.Report("回滚完成：" + ok + "/" + total);
                return ok;
            });
        }

        public static string LatestBackup()
        {
            try
            {
                if (!Directory.Exists(BackupDir)) return "";
                var dirs = Directory.GetDirectories(BackupDir);
                if (dirs.Length == 0) return "";
                Array.Sort(dirs);
                return dirs[dirs.Length - 1];
            }
            catch (Exception) { return ""; }
        }

        /// <summary>目录总大小（用于备份页展示）</summary>
        public static string CountSubDirSize(string dir)
        {
            try
            {
                long size = 0;
                foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { size += new FileInfo(f).Length; } catch (Exception) { }
                }
                if (size >= 1073741824) return (size / 1073741824.0).ToString("0.0") + " GB";
                return (size / 1048576.0).ToString("0") + " MB";
            }
            catch (Exception) { return "大小未知"; }
        }
    }
}
