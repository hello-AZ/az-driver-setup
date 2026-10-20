using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace DriverSetup
{
    /// <summary>系统排查：蓝屏记录（bugcheck 码 + Minidump 文件）</summary>
    public class BlueScreenPage : Panel
    {
        private static readonly Color Paper = Theme.Bg;
        private static readonly Color Paper2 = Theme.Card;
        private static readonly Color Ink = Theme.Text;
        private static readonly Color Ink2 = Theme.Text2;
        private static readonly Color Ink3 = Theme.Text3;
        private static readonly Color Line = Theme.Line;
        private static readonly Color Red = Theme.Red;
        private static readonly Color Green = Theme.Green;

        private Panel _list;
        private Label _summary;

        /// <summary>常见 bugcheck 码 → 名称|说明</summary>
        private static readonly Dictionary<uint, string> Known = new Dictionary<uint, string>
        {
            { 0x0000000A, "IRQL_NOT_LESS_OR_EQUAL|驱动访问了不该访问的内存（最常见，多为第三方驱动）" },
            { 0x0000001A, "MEMORY_MANAGEMENT|内存管理错误，可能是内存条或驱动" },
            { 0x0000001E, "KMODE_EXCEPTION_NOT_HANDLED|内核模式异常未处理" },
            { 0x0000003B, "SYSTEM_SERVICE_EXCEPTION|系统服务异常，常与显卡驱动有关" },
            { 0x00000050, "PAGE_FAULT_IN_NONPAGED_AREA|访问了不存在的内存页，多为驱动或内存故障" },
            { 0x0000007E, "SYSTEM_THREAD_EXCEPTION_NOT_HANDLED|系统线程异常未处理" },
            { 0x0000009F, "DRIVER_POWER_STATE_FAILURE|驱动电源状态切换失败（休眠/唤醒相关）" },
            { 0x000000C2, "BAD_POOL_CALLER|驱动程序发出错误的池请求" },
            { 0x000000C5, "DRIVER_OVERRAN_STACK_BUFFER|驱动栈溢出" },
            { 0x000000D1, "DRIVER_IRQL_NOT_LESS_OR_EQUAL|驱动在错误的中断级别访问内存" },
            { 0x00000124, "WHEA_UNCORRECTABLE_ERROR|硬件不可纠正错误（CPU/内存/主板/供电）" },
            { 0x00000133, "DPC_WATCHDOG_VIOLATION|延迟过程调用超时，多为存储或网卡驱动" },
            { 0x00000139, "KERNEL_SECURITY_CHECK_FAILURE|内核安全检查失败" },
            { 0x00000154, "UNEXPECTED_STORE_EXCEPTION|存储异常" },
            { 0xC000021A, "STATUS_SYSTEM_PROCESS_TERMINATED|关键系统进程终止" },
        };

        private class CrashInfo
        {
            public DateTime Time;
            public uint Code;
            public string Raw = "";
            public string DumpPath = "";
        }

        public BlueScreenPage()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            BuildUi();
        }

        private void BuildUi()
        {
            var head = new Panel();
            head.Dock = DockStyle.Top;
            head.Height = 52;
            head.BackColor = Paper;

            var title = new Label();
            title.Text = "系统排查 · 蓝屏记录";
            title.Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold);
            title.ForeColor = Ink;
            title.AutoSize = true;
            title.Location = new Point(24, 12);
            head.Controls.Add(title);

            var btn = new Button();
            btn.Text = "重新读取";
            btn.Size = new Size(100, 30);
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderColor = Line;
            btn.BackColor = Paper2;
            btn.ForeColor = Ink2;
            btn.Font = new Font("Microsoft YaHei UI", 8.5F);
            btn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btn.Location = new Point(Width - 124, 12);
            btn.Click += delegate { RefreshData(); };
            head.Controls.Add(btn);
            head.Resize += delegate { btn.Location = new Point(head.Width - btn.Width - 16, 12); };

            _summary = new Label();
            _summary.Dock = DockStyle.Top;
            _summary.Height = 34;
            _summary.BackColor = Paper;
            _summary.ForeColor = Ink3;
            _summary.Font = new Font("Microsoft YaHei UI", 8.5F);
            _summary.Padding = new Padding(26, 4, 24, 0);

            _list = new Panel();
            _list.Dock = DockStyle.Fill;
            _list.AutoScroll = true;
            _list.BackColor = Paper;
            _list.Padding = new Padding(24, 8, 24, 16);

            Controls.Add(_list);
            Controls.Add(_summary);
            Controls.Add(head);

            // 数据在切到本页时才读（WMI 查询 1-3 秒，不该拖慢启动）
        }

        public void RefreshData()
        {
            if (_list == null) return;

            // 先把"正在读取"摆出来，再去后台查 WMI（扫日志要 2-5 秒）
            ShowLoading("正在读取蓝屏记录…");

            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                List<CrashInfo> cs = new List<CrashInfo>();
                try { cs = ReadCrashes(); } catch { }
                List<FileInfo> ds = ListDumps();

                try
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    BeginInvoke((Action)delegate { Render(cs, ds); });
                }
                catch { }
            });
        }

        private void ShowLoading(string text)
        {
            Log.Write("[排查] " + text);
            if (_list == null) return;
            _list.SuspendLayout();
            _list.Controls.Clear();

            var p = new RoundPanel();
            p.Radius = 10;
            p.Location = new Point(4, 8);
            p.Size = new Size(Math.Max(400, _list.ClientSize.Width - 40), 64);
            p.FillColor = Paper2;

            var l = new Label();
            l.Text = text;
            l.Font = new Font("Microsoft YaHei UI", 9F);
            l.ForeColor = Ink3;
            l.AutoSize = true;
            l.Location = new Point(16, 20);
            p.Controls.Add(l);

            _list.Controls.Add(p);
            _list.ResumeLayout();

            if (_summary != null) { _summary.Text = ""; }
        }

        private void Render(List<CrashInfo> crashes, List<FileInfo> dumps)
        {
            if (_list == null) return;
            _list.SuspendLayout();
            _list.Controls.Clear();

            if (crashes.Count == 0 && dumps.Count == 0)
            {
                if (_summary != null)
                {
                    _summary.Text = "没有蓝屏记录";
                    _summary.ForeColor = Green;
                }
                AddNote("系统日志里没有 BlueScreen 事件，Minidump 目录也是空的。",
                        "这台机器没有蓝屏过，或者蓝屏转储功能已被关闭（系统属性 → 高级 → 启动和故障恢复）。");
                _list.ResumeLayout();
                return;
            }

            if (_summary != null)
            {
                _summary.Text = "找到 " + crashes.Count + " 条蓝屏记录 · " + dumps.Count + " 个转储文件";
                _summary.ForeColor = Ink3;
            }

            int y = 0;
            foreach (var c in crashes.OrderByDescending(x => x.Time))
            {
                y += AddCrashCard(c, y);
            }

            if (dumps.Count > 0)
            {
                y += 8;
                var t = new Label();
                t.Text = "转储文件";
                t.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
                t.ForeColor = Ink2;
                t.AutoSize = true;
                t.Location = new Point(4, y);
                _list.Controls.Add(t);
                y += 28;

                foreach (var d in dumps)
                {
                    var l = new Label();
                    l.Text = d.Name + "　" + Math.Round(d.Length / 1024.0 / 1024.0, 1) + " MB　" +
                             d.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
                    l.Font = new Font("Consolas", 8.5F);
                    l.ForeColor = Ink3;
                    l.AutoSize = true;
                    l.Location = new Point(8, y);
                    _list.Controls.Add(l);
                    y += 22;
                }
            }

            _list.ResumeLayout();
        }

        private void AddNote(string line1, string line2)
        {
            var p = new RoundPanel();
            p.Radius = 10;
            p.Location = new Point(4, 8);
            p.Size = new Size(Math.Max(400, _list.ClientSize.Width - 40), 92);
            p.FillColor = Paper2;

            var a = new Label();
            a.Text = line1;
            a.Font = new Font("Microsoft YaHei UI", 9F);
            a.ForeColor = Ink2;
            a.AutoSize = true;
            a.Location = new Point(16, 20);
            p.Controls.Add(a);

            var b = new Label();
            b.Text = line2;
            b.Font = new Font("Microsoft YaHei UI", 8.5F);
            b.ForeColor = Ink3;
            b.AutoSize = true;
            b.Location = new Point(16, 48);
            p.Controls.Add(b);

            _list.Controls.Add(p);
        }

        private int AddCrashCard(CrashInfo c, int y)
        {
            int w = Math.Max(400, _list.ClientSize.Width - 40);

            var card = new RoundPanel();
            card.Radius = 10;
            card.Location = new Point(4, y);
            card.Size = new Size(w, 104);
            card.FillColor = Paper2;

            var time = new Label();
            time.Text = c.Time.ToString("yyyy-MM-dd HH:mm:ss");
            time.Font = new Font("Consolas", 10F, FontStyle.Bold);
            time.ForeColor = Ink;
            time.AutoSize = true;
            time.Location = new Point(16, 12);
            card.Controls.Add(time);

            var code = new Label();
            code.Text = "0x" + c.Code.ToString("X8");
            code.Font = new Font("Consolas", 10F);
            code.ForeColor = Red;
            code.AutoSize = true;
            code.Location = new Point(200, 12);
            card.Controls.Add(code);

            string name, desc;
            if (Known.TryGetValue(c.Code, out string kn))
            {
                int bar = kn.IndexOf('|');
                name = bar > 0 ? kn.Substring(0, bar) : kn;
                desc = bar > 0 ? kn.Substring(bar + 1) : "";
            }
            else
            {
                name = "未知代码";
                desc = "查 microsoft.com/bugcheckcode 或搜索 0x" + c.Code.ToString("X8");
            }

            var n = new Label();
            n.Text = name;
            n.Font = new Font("Consolas", 9.5F, FontStyle.Bold);
            n.ForeColor = Ink2;
            n.AutoSize = true;
            n.Location = new Point(16, 40);
            card.Controls.Add(n);

            var d = new Label();
            d.Text = desc;
            d.Font = new Font("Microsoft YaHei UI", 8.5F);
            d.ForeColor = Ink3;
            d.AutoSize = false;
            d.Size = new Size(w - 32, 22);
            d.Location = new Point(16, 64);
            card.Controls.Add(d);

            if (!string.IsNullOrEmpty(c.DumpPath))
            {
                var dp = new Label();
                dp.Text = c.DumpPath;
                dp.Font = new Font("Consolas", 7.5F);
                dp.ForeColor = Ink3;
                dp.AutoSize = true;
                dp.Location = new Point(16, 86);
                card.Controls.Add(dp);
            }

            _list.Controls.Add(card);
            return card.Height + 10;
        }

        // ---------------- 数据源 ----------------

        private static List<CrashInfo> ReadCrashes()
        {
            var list = new List<CrashInfo>();

            // Event ID 1001 + 来源含 WER：Windows 把蓝屏记在这里
            string q = "SELECT TimeGenerated, Message FROM Win32_NTLogEvent " +
                       "WHERE Logfile='System' AND EventCode=1001";

            using (var searcher = new ManagementObjectSearcher(q))
            using (var col = searcher.Get())
            {
                foreach (ManagementBaseObject o in col)
                {
                    try
                    {
                        object msgObj = o["Message"];
                        string msg = msgObj == null ? "" : msgObj.ToString();
                        if (msg.IndexOf("BugCheck", StringComparison.OrdinalIgnoreCase) < 0 &&
                            msg.IndexOf("0x", StringComparison.Ordinal) < 0) continue;

                        var m = Regex.Match(msg, @"0x[0-9a-fA-F]{8}");
                        if (!m.Success) continue;

                        uint code = Convert.ToUInt32(m.Value.Substring(2), 16);

                        var info = new CrashInfo { Code = code, Raw = msg };

                        try
                        {
                            string t = o["TimeGenerated"].ToString();
                            info.Time = ManagementDateTimeConverter.ToDateTime(t);
                        }
                        catch { info.Time = DateTime.MinValue; }

                        var dm = Regex.Match(msg, @"[A-Za-z]:\\[^\s""]+\.(dmp|DMP)");
                        if (dm.Success) info.DumpPath = dm.Value;

                        list.Add(info);
                    }
                    catch { }
                }
            }
            return list;
        }

        private static List<FileInfo> ListDumps()
        {
            var list = new List<FileInfo>();
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Minidump");
            try
            {
                if (!Directory.Exists(dir)) return list;

                // 无管理员时也能列目录，但读不了内容
                foreach (var f in new DirectoryInfo(dir).GetFiles("*.dmp"))
                    list.Add(f);
            }
            catch { }
            return list;
        }
    }
}
