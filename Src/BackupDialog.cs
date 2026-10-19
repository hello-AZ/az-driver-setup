using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DriverSetup
{
    /// <summary>备份驱动的二级窗口：显示阶段、条数、进度条，可取消</summary>
    public class BackupDialog : Form
    {
        private static readonly Color Paper = Theme.Bg;
        private static readonly Color Paper2 = Theme.Card;
        private static readonly Color Ink = Theme.Text;
        private static readonly Color Ink2 = Theme.Text2;
        private static readonly Color Ink3 = Theme.Text3;
        private static readonly Color Line = Theme.Line;
        private static readonly Color Blue = Theme.Accent;
        private static readonly Color Red = Theme.Red;

        private Label _title;
        private Label _stage;
        private Label _count;
        private Label _hint;
        private ProgressBar _bar;
        private Button _btn;

        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private bool _running;

        /// <summary>备份目录（成功时）</summary>
        public string ResultDir = "";
        public Exception Error;

        public BackupDialog()
        {
            if (Program.AppIcon != null) Icon = Program.AppIcon;
            Text = "备份驱动";
            ClientSize = new Size(510, 264);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Paper;
            Font = new Font("Microsoft YaHei UI", 9.5F);

            BuildUi();
            Shown += OnShown;
            FormClosing += delegate
            {
                if (_running) _cts.Cancel();
            };
        }

        private void BuildUi()
        {
            _title = new Label();
            _title.Text = "备份本机驱动";
            _title.Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold);
            _title.ForeColor = Ink;
            _title.Location = new Point(26, 20);
            _title.AutoSize = true;

            _hint = new Label();
            _hint.Text = "用 pnputil 导出全部第三方驱动，体积较大，请耐心等待。";
            _hint.Font = new Font("Microsoft YaHei UI", 8F);
            _hint.ForeColor = Ink3;
            _hint.Location = new Point(26, 54);
            _hint.Size = new Size(452, 20);

            var box = new Panel();
            box.Location = new Point(22, 76);
            box.Size = new Size(462, 84);
            box.BackColor = Paper2;
            box.BorderStyle = BorderStyle.FixedSingle;

            _stage = new Label();
            _stage.Text = "准备中…";
            _stage.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            _stage.ForeColor = Blue;
            _stage.Location = new Point(14, 12);
            _stage.Size = new Size(260, 20);

            _count = new Label();
            _count.Text = "";
            _count.Font = new Font("Consolas", 9F);
            _count.ForeColor = Ink3;
            _count.TextAlign = ContentAlignment.MiddleRight;
            _count.Location = new Point(300, 12);
            _count.Size = new Size(146, 22);

            _bar = new ProgressBar();
            _bar.Location = new Point(14, 42);
            _bar.Size = new Size(434, 26);
            _bar.Style = ProgressBarStyle.Continuous;
            _bar.Minimum = 0;
            _bar.Maximum = 100;
            _bar.Value = 0;

            box.Controls.Add(_stage);
            box.Controls.Add(_count);
            box.Controls.Add(_bar);

            _btn = new Button();
            _btn.Text = "取消";
            _btn.Size = new Size(110, 38);
            _btn.Location = new Point(ClientSize.Width - 134, ClientSize.Height - 60);
            _btn.FlatStyle = FlatStyle.Flat;
            _btn.FlatAppearance.BorderColor = Line;
            _btn.BackColor = Paper2;
            _btn.ForeColor = Ink2;
            _btn.Click += OnButtonClick;

            Controls.Add(_title);
            Controls.Add(_hint);
            Controls.Add(box);
            Controls.Add(_btn);
        }

        private async void OnShown(object sender, EventArgs e)
        {
            _running = true;

            // 界面自测：DRV_UI_TEST=1 时只跑假进度，不碰磁盘
            if (Environment.GetEnvironmentVariable("DRV_UI_TEST") == "1")
            {
                await Task.Delay(400);
                for (int i = 0; i <= 86; i += 2)
                {
                    UpdateUi(new BackupProgress { Stage = "正在导出驱动", Done = i, Total = 86 });
                    await Task.Delay(45);
                }
                ResultDir = Path.Combine(InstallEngine.BackupDir, "20261019-141731");
                Finish(true, "备份完成");
                return;
            }

            try
            {
                var prog = new Progress<BackupProgress>(UpdateUi);
                ResultDir = await InstallEngine.BackupDriversAsync(prog, _cts.Token);
                Finish(true, "备份完成");
            }
            catch (OperationCanceledException)
            {
                Finish(false, "已取消");
            }
            catch (Exception ex)
            {
                Error = ex;
                Finish(false, "失败：" + ex.Message);
            }
        }

        private void UpdateUi(BackupProgress p)
        {
            if (p == null) return;
            _stage.Text = p.Stage;
            if (p.Total > 0)
            {
                int pct = (int)Math.Min(100, Math.Max(0, (long)p.Done * 100 / p.Total));
                _bar.Value = pct;
                _count.Text = p.Done + " / " + p.Total + "  (" + pct + "%)";
            }
            else
            {
                // 分母未知：跑个不确定态
                _bar.Style = ProgressBarStyle.Marquee;
                _count.Text = p.Done > 0 ? p.Done.ToString() : "";
            }
        }

        private void Finish(bool ok, string message)
        {
            _running = false;
            _bar.Style = ProgressBarStyle.Continuous;
            if (ok) _bar.Value = 100;
            _stage.Text = message;
            _stage.ForeColor = ok ? Blue : Red;
            if (ok && !string.IsNullOrEmpty(ResultDir))
                _hint.Text = "已保存到：" + ResultDir;
            _btn.Text = "关闭";
            _btn.Click -= OnButtonClick;
            _btn.Click += delegate { Close(); };
        }

        private void OnButtonClick(object sender, EventArgs e)
        {
            if (_running)
            {
                _btn.Enabled = false;
                _btn.Text = "正在取消…";
                _cts.Cancel();
            }
            else
            {
                Close();
            }
        }
    }
}
