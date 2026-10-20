using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DriverSetup
{
    /// <summary>
    /// 从某次备份恢复驱动：显示阶段、条数、进度条，可取消。
    /// 备份由 pnputil /export-driver 生成，恢复是逐个 pnputil /add-driver ... /install。
    /// </summary>
    public class RestoreDialog : Form
    {
        private static readonly Color Paper = Theme.Bg;
        private static readonly Color Paper2 = Theme.Card;
        private static readonly Color Ink = Theme.Text;
        private static readonly Color Ink2 = Theme.Text2;
        private static readonly Color Ink3 = Theme.Text3;
        private static readonly Color Line = Theme.Line;
        private static readonly Color Blue = Theme.Accent;
        private static readonly Color Red = Theme.Red;
        private static readonly Color Green = Theme.Green;

        private Label _title;
        private Label _hint;
        private Label _stage;
        private Label _count;
        private Label _detail;
        private ProgressBar _bar;
        private Button _btn;

        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private bool _running;

        /// <summary>恢复结果（完成后可用）</summary>
        public RestoreResult Result;

        private readonly string _dir;

        public RestoreDialog(string backupDir)
        {
            _dir = backupDir;

            if (Program.AppIcon != null) Icon = Program.AppIcon;
            Text = "恢复驱动";
            ClientSize = new Size(510, 280);
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
            _title.Text = "恢复备份的驱动";
            _title.Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold);
            _title.ForeColor = Ink;
            _title.Location = new Point(26, 20);
            _title.AutoSize = true;
            Controls.Add(_title);

            _hint = new Label();
            _hint.Text = "从备份目录逐个安装 .inf。不需要的设备会自动跳过。";
            _hint.Font = new Font("Microsoft YaHei UI", 8F);
            _hint.ForeColor = Ink3;
            _hint.Location = new Point(26, 54);
            _hint.Size = new Size(452, 20);
            Controls.Add(_hint);

            var box = new Panel();
            box.Location = new Point(22, 78);
            box.Size = new Size(462, 96);
            box.BackColor = Paper2;
            box.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(box);

            var pathLbl = new Label();
            pathLbl.Text = Path.GetFileName(_dir);
            pathLbl.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            pathLbl.ForeColor = Ink;
            pathLbl.Location = new Point(14, 10);
            pathLbl.Size = new Size(430, 20);
            box.Controls.Add(pathLbl);

            _stage = new Label();
            _stage.Text = "准备中…";
            _stage.Font = new Font("Microsoft YaHei UI", 9F);
            _stage.ForeColor = Ink2;
            _stage.Location = new Point(14, 34);
            _stage.Size = new Size(280, 20);
            box.Controls.Add(_stage);

            _count = new Label();
            _count.Text = "";
            _count.Font = new Font("Microsoft YaHei UI", 9F);
            _count.ForeColor = Ink3;
            _count.Location = new Point(300, 34);
            _count.Size = new Size(148, 20);
            _count.TextAlign = ContentAlignment.MiddleRight;
            box.Controls.Add(_count);

            _bar = new ProgressBar();
            _bar.Location = new Point(14, 62);
            _bar.Size = new Size(434, 16);
            _bar.Style = ProgressBarStyle.Continuous;
            box.Controls.Add(_bar);

            _detail = new Label();
            _detail.Text = "";
            _detail.Font = new Font("Consolas", 8F);
            _detail.ForeColor = Ink3;
            _detail.Location = new Point(22, 184);
            _detail.Size = new Size(462, 34);
            _detail.AutoEllipsis = true;
            Controls.Add(_detail);

            _btn = new Button();
            _btn.Text = "取消";
            _btn.Size = new Size(96, 30);
            _btn.Location = new Point(ClientSize.Width - 118, ClientSize.Height - 46);
            _btn.FlatStyle = FlatStyle.Flat;
            _btn.FlatAppearance.BorderColor = Line;
            _btn.BackColor = Paper2;
            _btn.ForeColor = Ink;
            _btn.Font = new Font("Microsoft YaHei UI", 9F);
            _btn.Click += delegate
            {
                if (_running) { _cts.Cancel(); _btn.Enabled = false; _btn.Text = "正在取消…"; }
                else Close();
            };
            Controls.Add(_btn);
        }

        private void UpdateUi(BackupProgress p)
        {
            if (IsDisposed) return;

            if (!string.IsNullOrEmpty(p.Stage)) _stage.Text = p.Stage;

            if (p.Total > 0)
            {
                _count.Text = p.Done + " / " + p.Total;
                _bar.Style = ProgressBarStyle.Continuous;
                _bar.Maximum = p.Total;
                _bar.Value = Math.Min(p.Done, p.Total);
            }
            else
            {
                _count.Text = "";
                _bar.Style = ProgressBarStyle.Marquee;
            }

            if (!string.IsNullOrEmpty(p.Detail)) _detail.Text = p.Detail;
        }

        private async void OnShown(object sender, EventArgs e)
        {
            _running = true;

            try
            {
                var prog = new Progress<BackupProgress>(UpdateUi);
                Result = await InstallEngine.RestoreFromBackupAsync(_dir, prog, _cts.Token);
                Finish(true);
            }
            catch (OperationCanceledException)
            {
                Finish(false, "已取消");
            }
            catch (Exception ex)
            {
                Log.Swallow(ex, "RestoreDialog.OnShown", _dir);
                Finish(false, ex.Message);
            }
        }

        private void Finish(bool ok, string err = null)
        {
            _running = false;

            if (!ok)
            {
                _stage.Text = err ?? "失败";
                _stage.ForeColor = Red;
                _btn.Text = "关闭";
                _btn.Enabled = true;
                return;
            }

            var r = Result;
            if (r == null) { _stage.Text = "完成"; _btn.Text = "关闭"; _btn.Enabled = true; return; }

            _bar.Style = ProgressBarStyle.Continuous;
            _stage.ForeColor = r.Fail > 0 ? Red : Green;
            _stage.Text = string.Format("完成：成功 {0} · 跳过 {1} · 失败 {2}", r.Ok, r.Skip, r.Fail);
            _count.Text = "";

            if (r.Errors.Count > 0)
            {
                string first = r.Errors[0];
                if (first.Length > 70) first = first.Substring(0, 70) + "…";
                _detail.Text = first + (r.Errors.Count > 1 ? "（共 " + r.Errors.Count + " 条错误）" : "");
            }
            else
            {
                _detail.Text = r.Ok > 0 ? "驱动已装回，部分设备可能需要重启后生效。" : "没有可安装的驱动。";
            }

            _btn.Text = "关闭";
            _btn.Enabled = true;

            // 把完整错误清单落盘，方便排查
            if (r.Errors.Count > 0)
            {
                try
                {
                    System.IO.File.WriteAllLines(
                        System.IO.Path.Combine(InstallEngine.AppDir, "restore-errors.txt"),
                        r.Errors.ToArray());
                }
                catch (Exception ex) { Log.Swallow(ex, "RestoreDialog.WriteErrors"); }
            }
        }
    }
}
