using System;
using System.IO;

namespace DriverSetup
{
    /// <summary>
    /// 轻量日志。用来替代裸的 catch { }：
    /// 有些失败可以忽略（设备没有某个属性、文件还没生成），但不能一声不吭。
    /// </summary>
    public static class Log
    {
        private static readonly object _lock = new object();

        private static string FilePath
        {
            get
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.log");
            }
        }

        public static void Write(string msg)
        {
            lock (_lock)
            {
                try
                {
                    File.AppendAllText(FilePath,
                        DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg + Environment.NewLine);
                }
                catch { }
            }
        }

        /// <summary>可忽略的失败：记一笔，继续跑</summary>
        public static void Swallow(Exception ex, string where)
        {
            Write(ex == null
                ? "[忽略] " + where
                : "[忽略] " + where + " :: " + ex.GetType().Name + ": " + ex.Message);
        }

        /// <summary>可忽略的失败 + 上下文</summary>
        public static void Swallow(Exception ex, string where, string detail)
        {
            Write(ex == null
                ? "[忽略] " + where + " :: " + detail
                : "[忽略] " + where + " :: " + detail + " :: " + ex.GetType().Name + ": " + ex.Message);
        }
    }
}
