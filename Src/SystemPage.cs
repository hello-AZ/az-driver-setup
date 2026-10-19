using System;
using System.Drawing;
using System.Windows.Forms;

namespace DriverSetup
{
    /// <summary>系统排查页：蓝屏记录 / 驱动清理</summary>
    public class SystemPage : RoundPanel
    {
        private static readonly Color Paper = Theme.Bg;
        private static readonly Color Paper2 = Theme.Card;
        private static readonly Color Ink = Theme.Text;
        private static readonly Color Ink2 = Theme.Text2;
        private static readonly Color Line = Theme.Line;

        private Button[] _tabs;
        private Control[] _views;
        private int _current = -1;
        private BlueScreenPage _blue;
        private DriverCleanPage _clean;

        public SystemPage()
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

            string[] names = { "蓝屏记录", "驱动清理" };
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

            _blue = new BlueScreenPage();
            _clean = new DriverCleanPage();
            _views = new Control[] { _blue, _clean };

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

        /// <summary>由主窗口在切到本页时调用：加载当前子页的数据</summary>
        public void RefreshCurrent()
        {
            if (_views == null) return;
            if (_current == 0 && _blue != null) _blue.RefreshData();
            if (_current == 1 && _clean != null) _clean.RefreshData();
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
    }
}
