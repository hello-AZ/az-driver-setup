using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;

namespace DriverSetup
{
    /// <summary>
    /// 全局主题色板。
    /// 取自参考图（葡萄藤下的白发少女）：暖白背景、葡萄紫强调、叶绿、青绿眼睛、柔粉。
    /// 改主题只改这里，各页面引用 Theme.Xxx。
    /// </summary>
    public static class Theme
    {
        // ---- 背景 ----
        /// <summary>页面底色：暖白（葡萄叶透下的光）</summary>
        public static readonly Color Bg = Color.FromArgb(0xF7, 0xF1, 0xE5);

        /// <summary>卡片/条幅底色：纯白</summary>
        public static readonly Color Card = Color.FromArgb(0xFF, 0xFD, 0xF8);

        /// <summary>交替行：极淡米色</summary>
        public static readonly Color CardAlt = Color.FromArgb(0xF2, 0xE8, 0xD7);

        /// <summary>次级底色（导航栏、状态带）</summary>
        public static readonly Color Surface = Color.FromArgb(0xF2, 0xE8, 0xD7);

        // ---- 文字 ----
        /// <summary>主文字：深紫灰（不是纯黑，柔和）</summary>
        public static readonly Color Text = Color.FromArgb(0x3B, 0x32, 0x27);

        /// <summary>次文字</summary>
        public static readonly Color Text2 = Color.FromArgb(0x5F, 0x54, 0x46);

        /// <summary>弱文字 / 说明</summary>
        public static readonly Color Text3 = Color.FromArgb(0x91, 0x85, 0x6F);

        // ---- 线条 ----
        /// <summary>分隔线、边框：淡紫灰</summary>
        public static readonly Color Line = Color.FromArgb(0xDE, 0xD0, 0xB5);

        // ---- 强调色 ----
        /// <summary>主强调：葡萄紫</summary>
        public static readonly Color Accent = Color.FromArgb(0x7A, 0x5E, 0xA8);

        /// <summary>次强调：青绿（她的眼睛）</summary>
        /// <summary>浅紫：导航选中底、标签底</summary>
        public static readonly Color AccentSoft = Color.FromArgb(0xED, 0xE7, 0xF2);

        public static readonly Color Teal = Color.FromArgb(0x63, 0x49, 0x8D);

        /// <summary>成功 / 通过：葡萄叶绿</summary>
        public static readonly Color Green = Color.FromArgb(0x3F, 0x7D, 0x4F);

        /// <summary>警示 / 危险：柔粉红</summary>
        public static readonly Color Red = Color.FromArgb(0xB3, 0x40, 0x2F);

        /// <summary>提醒 / 中间态：暖橙</summary>
        public static readonly Color Orange = Color.FromArgb(0xB3, 0x85, 0x3F);

        /// <summary>紫：用于曲线等区分</summary>
        public static readonly Color Purple = Color.FromArgb(0x63, 0x49, 0x8D);

        /// <summary>选中行底色</summary>
        public static readonly Color Selection = Color.FromArgb(0xED, 0xE3, 0xD0);
    }
}
