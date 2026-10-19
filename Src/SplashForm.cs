using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DriverSetup
{
    /// <summary>启动进度窗：主窗口构造期间显示，避免用户以为程序卡死</summary>
    public class SplashForm : Form
    {
        private Label _step;
        private Label _pct;
        private int _value;
        private string _text = "正在启动…";

        public SplashForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ClientSize = new Size(430, 148);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            Font = new Font("Microsoft YaHei UI", 9F);

            var title = new Label();
            title.Text = "AZ 驱动懒人安装";
            title.Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold);
            title.ForeColor = Theme.Text;
            title.AutoSize = true;
            title.Location = new Point(28, 26);
            Controls.Add(title);

            var sub = new Label();
            sub.Text = "驱动 · 配置 · 测试";
            sub.Font = new Font("Microsoft YaHei UI", 8F);
            sub.ForeColor = Theme.Text3;
            sub.AutoSize = true;
            sub.Location = new Point(196, 33);
            Controls.Add(sub);

            _step = new Label();
            _step.Text = "正在启动…";
            _step.Font = new Font("Microsoft YaHei UI", 8.5F);
            _step.ForeColor = Theme.Text2;
            _step.AutoSize = true;
            _step.Location = new Point(28, 96);
            Controls.Add(_step);

            _pct = new Label();
            _pct.Text = "0%";
            _pct.Font = new Font("Consolas", 8.5F);
            _pct.ForeColor = Theme.Text3;
            _pct.AutoSize = true;
            _pct.Location = new Point(378, 96);
            Controls.Add(_pct);

            Paint += delegate (object s, PaintEventArgs e) { PaintShell(e.Graphics); };
        }

        private void PaintShell(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 外边框
            using (var pen = new Pen(Theme.Line, 1))
                g.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);

            // 进度槽
            int x = 28, y = 70, w = ClientSize.Width - 56, h = 8;
            using (var b = new SolidBrush(Theme.CardAlt))
                g.FillRectangle(b, x, y, w, h);

            // 进度条
            int fill = (int)(w * Math.Max(0, Math.Min(100, _value)) / 100.0);
            if (fill > 0)
            {
                using (var b = new SolidBrush(Theme.Accent))
                    g.FillRectangle(b, x, y, fill, h);
            }
        }

        public void SetProgress(int percent, string step)
        {
            try
            {
                if (IsDisposed) return;
                if (InvokeRequired)
                {
                    BeginInvoke((Action)delegate { SetProgress(percent, step); });
                    return;
                }

                _value = percent;
                _text = step;
                if (_step != null) _step.Text = step;
                if (_pct != null) _pct.Text = percent + "%";

                Invalidate();
                Application.DoEvents();   // 立刻重绘，不然后面还在算，界面不动
            }
            catch { }
        }
    }
}
