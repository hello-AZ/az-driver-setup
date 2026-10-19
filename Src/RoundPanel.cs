using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DriverSetup
{
    /// <summary>圆角卡片面板：按设计稿的柔和圆角 + 极淡描边</summary>
    public class RoundPanel : Panel
    {
        public int Radius = 16;
        public Color FillColor = Theme.Card;
        public bool DrawBorder = true;
        public bool DrawFill = true;
        public Image BgImage = null;
        public Color BorderColor = Color.FromArgb(38, 150, 130, 170);

        public RoundPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        public static GraphicsPath MakePath(Rectangle r, int radius)
        {
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;

            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // 子控件透明时会回调到这里，所以背景图画在这一层才透得下去
            if (BgImage != null)
            {
                e.Graphics.DrawImage(BgImage, ClientRectangle);
                return;
            }
            base.OnPaintBackground(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);

            using (var path = MakePath(rect, Radius))
            {
                if (DrawFill)
                    using (var br = new SolidBrush(FillColor))
                        g.FillPath(br, path);

                if (DrawBorder)
                    using (var pen = new Pen(BorderColor, 1f))
                        g.DrawPath(pen, path);
            }

            base.OnPaint(e);
        }
    }
}
