using System;
using System.Collections.Generic;

namespace DriverSetup
{
    /// <summary>
    /// 驱动目录来源的统一接口。当前只有 Lenovo 实现；
    /// 以后加 Dell / HP / ASUS，只需再写一个实现并在 CatalogFactory 里登记。
    /// </summary>
    public interface IDriverCatalog
    {
        /// <summary>厂商标识，如 Lenovo</summary>
        string Vendor { get; }

        /// <summary>能否处理该厂商（按 Win32_ComputerSystemProduct.Vendor 判断）</summary>
        bool Supports(string vendorName);

        /// <summary>按序列号取机型档案（拿不到返回 null）</summary>
        LenovoMachine GetMachine(string sn);

        /// <summary>按序列号取官方驱动清单</summary>
        List<LenovoDriver> GetDrivers(string sn);

        /// <summary>厂商官方驱动下载页（用于「打开官网」）</summary>
        string OfficialPage { get; }
    }

    /// <summary>Lenovo 实现：官方序列号接口</summary>
    public class LenovoCatalog : IDriverCatalog
    {
        public string Vendor { get { return "Lenovo"; } }
        public string OfficialPage { get { return "https://newsupport.lenovo.com.cn/driveDownloads_index.html"; } }

        public bool Supports(string vendorName)
        {
            return !string.IsNullOrEmpty(vendorName) &&
                   vendorName.IndexOf("LENOVO", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public LenovoMachine GetMachine(string sn)
        {
            return LenovoApi.GetMachine(sn);
        }

        public List<LenovoDriver> GetDrivers(string sn)
        {
            return LenovoApi.GetDrivers(sn);
        }
    }

    public static class CatalogFactory
    {
        /// <summary>按本机厂商挑选目录来源；不支持则返回 null</summary>
        public static IDriverCatalog Create(string vendorName)
        {
            var providers = new IDriverCatalog[] { new LenovoCatalog() };
            foreach (var p in providers)
            {
                if (p.Supports(vendorName)) return p;
            }
            return null;
        }

        /// <summary>已支持的厂商清单（界面上提示用）</summary>
        public static string SupportedVendors()
        {
            return "Lenovo";
        }
    }
}
