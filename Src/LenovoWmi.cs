using System;
using System.Management;

namespace DriverSetup
{
    /// <summary>
    /// 联想官方 WMI 接口（root\WMI → LENOVO_GAMEZONE_DATA）。
    /// 纯查询方法，不加载任何内核驱动；需要管理员权限。
    /// </summary>
    public static class LenovoWmi
    {
        private const string Scope = @"root\WMI";
        private const string ClassName = "LENOVO_GAMEZONE_DATA";

        private static ManagementClass _cls;
        private static bool _tried;

        /// <summary>风扇转速（RPM）；不可用返回 -1</summary>
        public static int GetFanSpeed()
        {
            try
            {
                var mo = FirstInstance();
                if (mo == null) return -1;

                int v = InvokeInt(mo, "GetFan1Speed");
                if (v <= 0)
                {
                    int v2 = InvokeInt(mo, "GetFan2Speed");
                    if (v2 > v) v = v2;
                }
                return v;
            }
            catch { return -1; }
        }

        /// <summary>风扇最大转速；不可用返回 -1</summary>
        public static int GetFanMaxSpeed()
        {
            try
            {
                var mo = FirstInstance();
                return mo == null ? -1 : InvokeInt(mo, "GetFanMaxSpeed");
            }
            catch { return -1; }
        }

        /// <summary>本机是否支持这套接口</summary>
        public static bool Available
        {
            get { return FirstInstance() != null; }
        }

        public static void Dispose()
        {
            try { if (_cls != null) _cls.Dispose(); } catch { }
            _cls = null;
            _tried = false;
        }

        private static ManagementObject FirstInstance()
        {
            try
            {
                if (!_tried)
                {
                    _tried = true;
                    _cls = new ManagementClass(Scope, ClassName, null);
                }
                if (_cls == null) return null;

                using (var col = _cls.GetInstances())
                {
                    foreach (ManagementObject mo in col)
                    {
                        return mo;   // 调用方负责 Dispose
                    }
                }
            }
            catch
            {
                _cls = null;
            }
            return null;
        }

        private static int InvokeInt(ManagementObject mo, string method)
        {
            try
            {
                using (mo)
                using (var res = mo.InvokeMethod(method, null, null))
                {
                    if (res == null) return -1;
                    // 返回值的字段名各家版本不一，取第一个能转成整数的
                    foreach (var p in res.Properties)
                    {
                        try
                        {
                            object v = p.Value;
                            if (v == null) continue;
                            int n = Convert.ToInt32(v);
                            if (n > 0) return n;
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return -1;
        }
    }
}
