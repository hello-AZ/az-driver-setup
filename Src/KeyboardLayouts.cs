using System.Collections.Generic;
using System.Windows.Forms;

namespace DriverSetup
{
    /// <summary>键盘布局模板：104 全尺寸 / 102 联想式 / 14 寸无小键盘</summary>
    public static class KeyboardLayouts
    {
        public static readonly string[] Names =
        {
            "104 全尺寸",
            "102 联想式（编辑键上移）",
            "14 寸笔记本（无小键盘）",
        };

        public static List<KeyDef> Build(int id)
        {
            switch (id)
            {
                case 1: return Lenovo102();
                case 2: return Laptop14();
                default: return Full104();
            }
        }

        private static KeyDef K(string label, Keys vk, float x, float y, float w, float h = 1)
        {
            return new KeyDef { Label = label, Vk = vk, X = x, Y = y, W = w, H = h };
        }

        // ================= 104 全尺寸（标准 ANSI） =================
        // 主区 0-15u ｜ 编辑区 15.5-18u ｜ 数字小键盘 18.5-22.5u
        public static List<KeyDef> Full104()
        {
            var l = new List<KeyDef>();

            // 行 0：Esc + F1..F12 + PrtSc/ScrLk/Pause + 小键盘顶
            l.Add(K("Esc", Keys.Escape, 0, 0, 1));
            for (int i = 1; i <= 12; i++)
                l.Add(K("F" + i, (Keys)(0x70 + i - 1), 1.25f + (i - 1), 0, 1));
            l.Add(K("PrtSc", Keys.PrintScreen, 14.5f, 0, 1));
            l.Add(K("ScrLk", Keys.Scroll, 15.5f, 0, 1));
            l.Add(K("Pause", Keys.Pause, 16.5f, 0, 1));
            l.Add(K("Num", Keys.NumLock, 18.5f, 0, 1));
            l.Add(K("/", Keys.Divide, 19.5f, 0, 1));
            l.Add(K("*", Keys.Multiply, 20.5f, 0, 1));
            l.Add(K("-", Keys.Subtract, 21.5f, 0, 1));

            // 行 1：数字行 + Backspace + Ins/Home/PgUp + 7 8 9 +
            string[] numRow = { "`", "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=" };
            Keys[] numKeys = { Keys.Oemtilde, Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5,
                               Keys.D6, Keys.D7, Keys.D8, Keys.D9, Keys.D0, Keys.OemMinus, Keys.Oemplus };
            for (int i = 0; i < numRow.Length; i++)
                l.Add(K(numRow[i], numKeys[i], i, 1.25f, 1));
            l.Add(K("Back", Keys.Back, 13f, 1.25f, 2f));
            l.Add(K("Ins", Keys.Insert, 15.5f, 1.25f, 1));
            l.Add(K("Home", Keys.Home, 16.5f, 1.25f, 1));
            l.Add(K("PgUp", Keys.PageUp, 17.5f, 1.25f, 1));
            l.Add(K("7", Keys.NumPad7, 18.5f, 1.25f, 1));
            l.Add(K("8", Keys.NumPad8, 19.5f, 1.25f, 1));
            l.Add(K("9", Keys.NumPad9, 20.5f, 1.25f, 1));
            l.Add(K("+", Keys.Add, 21.5f, 1.25f, 1, 2));

            // 行 2：Tab + Q..\ + Del/End/PgDn + 4 5 6
            l.Add(K("Tab", Keys.Tab, 0, 2.25f, 1.5f));
            string[] qRow = { "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "[", "]", "\\" };
            Keys[] qKeys = { Keys.Q, Keys.W, Keys.E, Keys.R, Keys.T, Keys.Y, Keys.U, Keys.I,
                             Keys.O, Keys.P, Keys.OemOpenBrackets, Keys.OemCloseBrackets, Keys.OemPipe };
            for (int i = 0; i < qRow.Length; i++)
                l.Add(K(qRow[i], qKeys[i], 1.5f + i, 2.25f, 1));
            l.Add(K("Del", Keys.Delete, 15.5f, 2.25f, 1));
            l.Add(K("End", Keys.End, 16.5f, 2.25f, 1));
            l.Add(K("PgDn", Keys.PageDown, 17.5f, 2.25f, 1));
            l.Add(K("4", Keys.NumPad4, 18.5f, 2.25f, 1));
            l.Add(K("5", Keys.NumPad5, 19.5f, 2.25f, 1));
            l.Add(K("6", Keys.NumPad6, 20.5f, 2.25f, 1));

            // 行 3：Caps + A..' + Enter + 1 2 3 Ent
            l.Add(K("Caps", Keys.CapsLock, 0, 3.25f, 1.75f));
            string[] aRow = { "A", "S", "D", "F", "G", "H", "J", "K", "L", ";", "'" };
            Keys[] aKeys = { Keys.A, Keys.S, Keys.D, Keys.F, Keys.G, Keys.H, Keys.J, Keys.K,
                             Keys.L, Keys.OemSemicolon, Keys.OemQuotes };
            for (int i = 0; i < aRow.Length; i++)
                l.Add(K(aRow[i], aKeys[i], 1.75f + i, 3.25f, 1));
            l.Add(K("Enter", Keys.Enter, 12.75f, 3.25f, 2.25f));
            l.Add(K("1", Keys.NumPad1, 18.5f, 3.25f, 1));
            l.Add(K("2", Keys.NumPad2, 19.5f, 3.25f, 1));
            l.Add(K("3", Keys.NumPad3, 20.5f, 3.25f, 1));
            l.Add(K("Ent", Keys.Enter, 21.5f, 3.25f, 1, 2));

            // 行 4：Shift + Z../ + RShift + ↑ + 0 .
            l.Add(K("Shift", Keys.LShiftKey, 0, 4.25f, 2.25f));
            string[] zRow = { "Z", "X", "C", "V", "B", "N", "M", ",", ".", "/" };
            Keys[] zKeys = { Keys.Z, Keys.X, Keys.C, Keys.V, Keys.B, Keys.N, Keys.M,
                             Keys.Oemcomma, Keys.OemPeriod, Keys.OemQuestion };
            for (int i = 0; i < zRow.Length; i++)
                l.Add(K(zRow[i], zKeys[i], 2.25f + i, 4.25f, 1));
            l.Add(K("Shift", Keys.RShiftKey, 12.25f, 4.25f, 1.75f));
            l.Add(K("↑", Keys.Up, 16.5f, 4.25f, 1));
            l.Add(K("0", Keys.NumPad0, 18.5f, 4.25f, 2));
            l.Add(K(".", Keys.Decimal, 20.5f, 4.25f, 1));

            // 行 5：Ctrl Win Alt Space Alt Win Menu Ctrl + 方向键
            l.Add(K("Ctrl", Keys.LControlKey, 0, 5.25f, 1.25f));
            l.Add(K("⊞", Keys.LWin, 1.25f, 5.25f, 1.25f));
            l.Add(K("Alt", Keys.LMenu, 2.5f, 5.25f, 1.25f));
            l.Add(K("", Keys.Space, 3.75f, 5.25f, 6.25f));
            l.Add(K("Alt", Keys.RMenu, 10f, 5.25f, 1.25f));
            l.Add(K("⊞", Keys.RWin, 11.25f, 5.25f, 1.25f));
            l.Add(K("Menu", Keys.Apps, 12.5f, 5.25f, 1.25f));
            l.Add(K("Ctrl", Keys.RControlKey, 13.75f, 5.25f, 1.25f));
            l.Add(K("←", Keys.Left, 15.5f, 5.25f, 1));
            l.Add(K("↓", Keys.Down, 16.5f, 5.25f, 1));
            l.Add(K("→", Keys.Right, 17.5f, 5.25f, 1));

            return l;
        }

        // ================= 102 联想式（编辑键搬到功能键行） =================
        public static List<KeyDef> Lenovo102()
        {
            var l = new List<KeyDef>();

            // 行 1：Esc + F1..F12 + 编辑键
            l.Add(K("Esc", Keys.Escape, 0, 0, 1));
            for (int i = 1; i <= 12; i++)
                l.Add(K("F" + i, (Keys)(0x70 + i - 1), 1.25f + (i - 1), 0, 1));
            l.Add(K("Ins", Keys.Insert, 14f, 0, 1));
            l.Add(K("PrtSc", Keys.PrintScreen, 15f, 0, 1));
            l.Add(K("Del", Keys.Delete, 16f, 0, 1));
            l.Add(K("Home", Keys.Home, 17f, 0, 1));
            l.Add(K("End", Keys.End, 18f, 0, 1));
            l.Add(K("PgUp", Keys.PageUp, 19f, 0, 1));
            l.Add(K("PgDn", Keys.PageDown, 20f, 0, 1));

            // 行 2：数字行 + Backspace + 小键盘上排
            string[] numRow = { "`", "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=" };
            Keys[] numKeys = { Keys.Oemtilde, Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5,
                               Keys.D6, Keys.D7, Keys.D8, Keys.D9, Keys.D0, Keys.OemMinus, Keys.Oemplus };
            for (int i = 0; i < numRow.Length; i++)
                l.Add(K(numRow[i], numKeys[i], i, 1.25f, 1));
            l.Add(K("Back", Keys.Back, 13f, 1.25f, 2.5f));
            l.Add(K("Num", Keys.NumLock, 17.5f, 1.25f, 1));
            l.Add(K("/", Keys.Divide, 18.5f, 1.25f, 1));
            l.Add(K("*", Keys.Multiply, 19.5f, 1.25f, 1));
            l.Add(K("-", Keys.Subtract, 20.5f, 1.25f, 1));

            // 行 3：Tab + Q..\ + 7 8 9 +
            l.Add(K("Tab", Keys.Tab, 0, 2.25f, 1.5f));
            string[] qRow = { "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "[", "]", "\\" };
            Keys[] qKeys = { Keys.Q, Keys.W, Keys.E, Keys.R, Keys.T, Keys.Y, Keys.U, Keys.I,
                             Keys.O, Keys.P, Keys.OemOpenBrackets, Keys.OemCloseBrackets, Keys.OemPipe };
            for (int i = 0; i < qRow.Length; i++)
                l.Add(K(qRow[i], qKeys[i], 1.5f + i, 2.25f, 1));
            l.Add(K("7", Keys.NumPad7, 17.5f, 2.25f, 1));
            l.Add(K("8", Keys.NumPad8, 18.5f, 2.25f, 1));
            l.Add(K("9", Keys.NumPad9, 19.5f, 2.25f, 1));
            l.Add(K("+", Keys.Add, 20.5f, 2.25f, 1, 2));

            // 行 4：Caps + A..' + Enter + 4 5 6
            l.Add(K("Caps", Keys.CapsLock, 0, 3.25f, 1.75f));
            string[] aRow = { "A", "S", "D", "F", "G", "H", "J", "K", "L", ";", "'" };
            Keys[] aKeys = { Keys.A, Keys.S, Keys.D, Keys.F, Keys.G, Keys.H, Keys.J, Keys.K,
                             Keys.L, Keys.OemSemicolon, Keys.OemQuotes };
            for (int i = 0; i < aRow.Length; i++)
                l.Add(K(aRow[i], aKeys[i], 1.75f + i, 3.25f, 1));
            l.Add(K("Enter", Keys.Enter, 12.75f, 3.25f, 2.25f));
            l.Add(K("4", Keys.NumPad4, 17.5f, 3.25f, 1));
            l.Add(K("5", Keys.NumPad5, 18.5f, 3.25f, 1));
            l.Add(K("6", Keys.NumPad6, 19.5f, 3.25f, 1));

            // 行 5：Shift + Z../ + Shift + ↑ + 1 2 3 Ent
            l.Add(K("Shift", Keys.LShiftKey, 0, 4.25f, 2.25f));
            string[] zRow = { "Z", "X", "C", "V", "B", "N", "M", ",", ".", "/" };
            Keys[] zKeys = { Keys.Z, Keys.X, Keys.C, Keys.V, Keys.B, Keys.N, Keys.M,
                             Keys.Oemcomma, Keys.OemPeriod, Keys.OemQuestion };
            for (int i = 0; i < zRow.Length; i++)
                l.Add(K(zRow[i], zKeys[i], 2.25f + i, 4.25f, 1));
            l.Add(K("Shift", Keys.RShiftKey, 12.25f, 4.25f, 2.25f));
            l.Add(K("↑", Keys.Up, 15.5f, 4.25f, 1));
            l.Add(K("1", Keys.NumPad1, 17.5f, 4.25f, 1));
            l.Add(K("2", Keys.NumPad2, 18.5f, 4.25f, 1));
            l.Add(K("3", Keys.NumPad3, 19.5f, 4.25f, 1));
            l.Add(K("Ent", Keys.Enter, 20.5f, 4.25f, 1, 2));

            // 行 6：Ctrl Fn Win Alt ☀ Space Alt Copilot + 方向 + 0 Del
            l.Add(K("Ctrl", Keys.LControlKey, 0, 5.25f, 1.25f));
            l.Add(K("Fn", Keys.None, 1.25f, 5.25f, 1.25f));
            l.Add(K("⊞", Keys.LWin, 2.5f, 5.25f, 1.25f));
            l.Add(K("Alt", Keys.LMenu, 3.75f, 5.25f, 1.25f));
            l.Add(K("☀", Keys.None, 5f, 5.25f, 1f));
            l.Add(K("", Keys.Space, 6f, 5.25f, 5.75f));
            l.Add(K("Alt", Keys.RMenu, 11.75f, 5.25f, 1.25f));
            l.Add(K("Copi", Keys.None, 13f, 5.25f, 1.25f));
            l.Add(K("←", Keys.Left, 14.5f, 5.25f, 1));
            l.Add(K("↓", Keys.Down, 15.5f, 5.25f, 1));
            l.Add(K("→", Keys.Right, 16.5f, 5.25f, 1));
            l.Add(K("0", Keys.NumPad0, 17.5f, 5.25f, 2));
            l.Add(K("Del", Keys.Decimal, 19.5f, 5.25f, 1));

            return l;
        }

        // ================= 14 寸笔记本（无小键盘） =================
        public static List<KeyDef> Laptop14()
        {
            // 联想布局去掉右侧小键盘（x >= 17.4 的都是数字键区）
            var src = Lenovo102();
            var l = new List<KeyDef>();
            foreach (var k in src)
            {
                if (k.X >= 17.4f) continue;
                l.Add(k);
            }
            return l;
        }
    }
}
