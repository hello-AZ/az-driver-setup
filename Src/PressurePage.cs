using System;
using System.Drawing;
using System.Windows.Forms;

namespace DriverSetup
{
    /// <summary>浮窗包装：把 BurnPanel 装进独立窗口（命令行 --burn 调试用）</summary>
    public class BurnHostForm : Form
    {
        private BurnPanel _panel;

        public bool AutoStartNow
        {
            get { return _panel != null && _panel.AutoStartNow; }
            set { if (_panel != null) _panel.AutoStartNow = value; }
        }

        public BurnHostForm()
        {
            if (Program.AppIcon != null) Icon = Program.AppIcon;
            Text = "烤机与温度监控";
            ClientSize = new Size(1000, 740);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(760, 600);
            BackColor = Color.FromArgb(247, 241, 229);

            _panel = new BurnPanel(null);
            _panel.Dock = DockStyle.Fill;
            Controls.Add(_panel);
        }
    }

    /// <summary>压力测试页：只放施加负载的烤机（CPU / GPU）</summary>
    public class PressurePage : RoundPanel
    {
        private static readonly Color Paper = Theme.Bg;
        private static readonly Color Paper2 = Theme.Card;
        private static readonly Color Ink = Theme.Text;
        private static readonly Color Ink2 = Theme.Text2;
        private static readonly Color Line = Theme.Line;

        private Button[] _tabs;
        private Control[] _views;
        private int _current = -1;
        private PerfSampler _sampler;
        private BurnPanel _burnPanel;
        private GpuBurnPanel _gpuPanel;

        public PressurePage()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            BuildUi();
        }

        private void BuildUi()
        {
            var head = new Panel();
            head.Dock = DockStyle.Top;
            head.Height = 58;
            head.BackColor = Paper;

            string[] names = { "CPU 烤机", "GPU 烤机" };
            _tabs = new Button[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                var b = new Button();
                b.Text = names[i];
                b.Size = new Size(104, 32);
                b.Location = new Point(24 + i * 112, 13);
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderColor = Line;
                b.BackColor = Paper2;
                b.ForeColor = Ink2;
                b.Font = new Font("Microsoft YaHei UI", 9F);
                b.Cursor = Cursors.Hand;
                int idx = i;
                b.Click += delegate { ShowView(idx); };
                _tabs[i] = b;
                head.Controls.Add(b);
            }

            var headLine = new Panel();
            headLine.Dock = DockStyle.Bottom;
            headLine.Height = 1;
            headLine.BackColor = Line;
            head.Controls.Add(headLine);

            // 采样器延迟到切进本页时再建（LHM 初始化要 3-6 秒，不该拖慢启动）
            var burn = new BurnPanel(null);
            var gpu = new GpuBurnPanel(null);
            _burnPanel = burn;
            _gpuPanel = gpu;
            _views = new Control[] { burn, gpu };

            var host = new RoundPanel { DrawFill = false, DrawBorder = false };
            host.Dock = DockStyle.Fill;
            
            foreach (var v in _views)
            {
                v.Dock = DockStyle.Fill;
                v.Visible = false;
                host.Controls.Add(v);
            }

            Controls.Add(host);
            Controls.Add(head);

            ShowView(0);
        }

        private void ShowView(int index)
        {
            if (index < 0 || index >= _views.Length) return;
            _current = index;

            for (int i = 0; i < _views.Length; i++)
            {
                if (_views[i] == null) continue;
                _views[i].Visible = (i == index);
                if (_tabs != null && _tabs[i] != null)
                {
                    bool on = (i == index);
                    _tabs[i].BackColor = on ? Paper : Paper2;
                    _tabs[i].ForeColor = on ? Ink : Ink2;
                    _tabs[i].Font = new Font("Microsoft YaHei UI", 9F, on ? FontStyle.Bold : FontStyle.Regular);
                }
            }
            if (index < _views.Length && _views[index] != null) _views[index].BringToFront();
        }

        /// <summary>由主窗口在切到本页时调用：初始化采样器</summary>
        private bool _samplerLoading;

        public void EnsureSampler()
        {
            if (_sampler != null || _samplerLoading) return;
            _samplerLoading = true;

            // 先把提示摆出来，别让界面僵在那儿
            Log.Write("[压力] 开始初始化传感器");
            if (_burnPanel != null) _burnPanel.ShowLoading("正在初始化传感器…（首次约 3-6 秒，之后秒开）");

            System.Threading.Tasks.Task.Run((Action)delegate
            {
                PerfSampler s = null;
                string err = null;
                try { s = new PerfSampler(); }
                catch (Exception ex) { err = ex.Message; }

                bool scheduled = false;
                try
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    BeginInvoke((Action)delegate
                    {
                        _samplerLoading = false;
                        if (s != null)
                        {
                            Log.Write("[压力] 传感器就绪");
                            _sampler = s;
                            if (_burnPanel != null) _burnPanel.SetSampler(s);
                            if (_gpuPanel != null) _gpuPanel.SetSampler(s);
                        }
                        else if (_burnPanel != null)
                        {
                            Log.Write("[压力] 传感器初始化失败：" + err);
                            _burnPanel.ShowLoading("传感器初始化失败：" + err);
                        }
                    });
                    scheduled = true;
                }
                catch (Exception ex) { Log.Swallow(ex, "PressurePage:171"); }
                finally
                {
                    // 兜底：句柄失效时上面的 return 会跳过 BeginInvoke，
                    // _samplerLoading 卡在 true 之后，这一页永远显示"正在初始化传感器…"
                    if (!scheduled)
                    {
                        // 没交出去的那份采样器要自己收掉，别漏 LHM 句柄
                        if (s != null) { try { s.Dispose(); } catch (Exception ex) { Log.Swallow(ex, "PressurePage 释放未用采样器"); } }

                        bool recovered = false;
                        try
                        {
                            if (!IsDisposed && IsHandleCreated)
                            {
                                BeginInvoke((Action)delegate { _samplerLoading = false; });
                                recovered = true;
                            }
                        }
                        catch (Exception ex) { Log.Swallow(ex, "PressurePage._samplerLoading 兜底复位"); }

                        if (!recovered) _samplerLoading = false;
                    }
                }
            });
        }

        /// <summary>停止一切负载（切走分页时调用）</summary>
        public void StopAll()
        {
            if (_views == null) return;
            var b = _views[0] as BurnPanel;
            if (b != null) b.StopAllBurn();
            if (_views.Length > 1)
            {
                var g = _views[1] as GpuBurnPanel;
                if (g != null) g.StopAllBurn();
            }
        }
    }
}
