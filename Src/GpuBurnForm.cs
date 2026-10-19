using System;
using System.Drawing;
using System.Windows.Forms;

namespace DriverSetup
{
    /// <summary>GPU 烤机窗口：全屏 shader 填充 + 顶部实时状态</summary>
    public class GpuBurnForm : Form
    {
        private static readonly Color Ink = Theme.Text;
        private static readonly Color Ink2 = Theme.Text2;

        private GpuBurner _burner;
        private PerfSampler _sampler;
        private Timer _timer;
        private Panel _view;
        private Label _info;
        private bool _autoStart;
        private int _sec;

        public GpuBurnForm(PerfSampler sampler, bool autoStart)
        {
            if (Program.AppIcon != null) Icon = Program.AppIcon;

            _sampler = sampler;
            _autoStart = autoStart;

            Text = "GPU 烤机";
            ClientSize = new Size(1180, 700);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.Black;
            MinimumSize = new Size(640, 420);
            KeyPreview = true;

            _view = new Panel();
            _view.Dock = DockStyle.Fill;
            _view.BackColor = Color.Black;

            var bar = new Panel();
            bar.Dock = DockStyle.Top;
            bar.Height = 42;
            bar.BackColor = Color.FromArgb(24, 22, 20);
            bar.Padding = new Padding(16, 0, 16, 0);

            _info = new Label();
            _info.Dock = DockStyle.Fill;
            _info.TextAlign = ContentAlignment.MiddleLeft;
            _info.Font = new Font("Consolas", 9.5F);
            _info.ForeColor = Color.FromArgb(210, 200, 186);
            _info.Text = "GPU 烤机 · 按 Esc 停止";
            bar.Controls.Add(_info);

            Controls.Add(_view);
            Controls.Add(bar);

            _burner = new GpuBurner();

            _timer = new Timer();
            _timer.Interval = 1000;
            _timer.Tick += delegate { UpdateInfo(); };

            Shown += delegate
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(120);
                _burner.Start(_view.Handle, _view.ClientSize.Width, _view.ClientSize.Height);
                _timer.Start();
                _sec = 0;
            };

            KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) Close();
            };

            FormClosing += delegate
            {
                if (_timer != null) _timer.Stop();
                try { _burner.Stop(); } catch { }
            };
        }

        private void UpdateInfo()
        {
            _sec++;
            string fps = _burner.Fps > 0 ? _burner.Fps.ToString("F0") + " FPS" : "初始化…";
            string s = "GPU 烤机 · " + _sec + "s · " + fps;

            if (!string.IsNullOrEmpty(_burner.Error))
                s += " · 错误: " + _burner.Error;

            if (_sampler != null)
            {
                try
                {
                    var p = _sampler.Sample(_sec);
                    if (!double.IsNaN(p.GpuLoad)) s += " · 负载 " + p.GpuLoad.ToString("F0") + "%";
                    if (!double.IsNaN(p.GpuTemp)) s += " · " + p.GpuTemp.ToString("F0") + "°C";
                    if (!double.IsNaN(p.GpuClock)) s += " · " + p.GpuClock.ToString("F0") + "MHz";
                    if (!double.IsNaN(p.GpuPower)) s += " · " + p.GpuPower.ToString("F0") + "W";
                }
                catch { }
            }
            s += "   （Esc 停止）";
            _info.Text = s;
        }
    }
}
