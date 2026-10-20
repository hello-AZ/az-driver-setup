using System;
using System.Collections.Generic;
using System.Linq;

namespace DriverSetup
{
    /// <summary>
    /// 驱动目录来源的统一接口。
    ///
    /// 两种能力：
    ///   CanQueryDrivers = true   → 能按序列号从官网接口拿驱动清单（目前只有联想）
    ///   CanQueryDrivers = false  → 官网没有开放接口，只能显示机型 + 引导用户去官网自取
    /// </summary>
    public interface IDriverCatalog
    {
        /// <summary>厂商标识，如 LENOVO / ASUS</summary>
        string Vendor { get; }

        /// <summary>界面显示用的厂商名，如「华硕」</summary>
        string DisplayName { get; }

        /// <summary>能否按序列号查询驱动清单</summary>
        bool CanQueryDrivers { get; }

        /// <summary>能否处理该厂商（按 Win32_ComputerSystemProduct.Vendor 判断）</summary>
        bool Supports(string vendorName);

        /// <summary>按序列号取机型档案（拿不到返回 null）</summary>
        LenovoMachine GetMachine(string sn);

        /// <summary>按序列号取官方驱动清单</summary>
        List<LenovoDriver> GetDrivers(string sn);

        /// <summary>厂商官方驱动下载页</summary>
        string OfficialPage { get; }
    }

    /// <summary>Lenovo 实现：官方序列号接口，能查驱动清单</summary>
    public class LenovoCatalog : IDriverCatalog
    {
        public string Vendor { get { return "Lenovo"; } }
        public string DisplayName { get { return "联想"; } }
        public string OfficialPage { get { return "https://newsupport.lenovo.com.cn/driveDownloads_index.html"; } }
        public bool CanQueryDrivers { get { return true; } }

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

    /// <summary>
    /// 通用厂商：官网没有对外开放的驱动查询接口。
    /// 程序能读出本机机型和序列号，但拿不到驱动清单，只能引导用户去官网自己找。
    /// </summary>
    public class GenericCatalog : IDriverCatalog
    {
        private readonly string _vendor;
        private readonly string _displayName;
        private readonly string _officialPage;
        private readonly string[] _keys;

        public GenericCatalog(string vendor, string displayName, string officialPage, params string[] keys)
        {
            _vendor = vendor;
            _displayName = displayName;
            _officialPage = officialPage;
            _keys = keys ?? new string[0];
        }

        public string Vendor { get { return _vendor; } }
        public string DisplayName { get { return _displayName; } }
        public string OfficialPage { get { return _officialPage; } }
        public bool CanQueryDrivers { get { return false; } }

        public bool Supports(string vendorName)
        {
            if (string.IsNullOrEmpty(vendorName)) return false;
            foreach (var k in _keys)
                if (!string.IsNullOrEmpty(k) &&
                    vendorName.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        // 没有接口，机型与驱动都拿不到；界面改用本地 WMI 读到的机型名兜底
        public LenovoMachine GetMachine(string sn) { return null; }
        public List<LenovoDriver> GetDrivers(string sn) { return new List<LenovoDriver>(); }
    }

    public static class CatalogFactory
    {
        // 顺序有讲究：联想放最前（唯一能查驱动的），其余按常见度排
        private static readonly IDriverCatalog[] Providers = new IDriverCatalog[]
        {
            new LenovoCatalog(),

            // 华硕：Vendor 通常写 ASUSTeK COMPUTER INC.；天选是华硕子品牌，同一套官网
            new GenericCatalog("ASUS", "华硕", "https://www.asus.com.cn/support/",
                "ASUSTeK", "ASUS", "TUF"),

            // 惠普：官网 403 挡爬虫，连页面都抓不到，更别说接口
            new GenericCatalog("HP", "惠普", "https://support.hp.com/cn-zh/drivers",
                "Hewlett-Packard", "HP"),

            // 荣耀：原华为系，官网驱动页需按型号手动查
            new GenericCatalog("HONOR", "荣耀", "https://www.honor.com/cn/support/",
                "HONOR", "荣耀"),

            new GenericCatalog("HUAWEI", "华为", "https://consumer.huawei.com/cn/support/",
                "HUAWEI", "华为"),

            // 机械革命：小厂，官网只有按型号下载页
            new GenericCatalog("MECHREVO", "机械革命", "https://www.mechrevo.com/",
                "MECHREVO", "机械革命"),
        };

        /// <summary>按本机厂商挑选目录来源；不认识则返回 null</summary>
        public static IDriverCatalog Create(string vendorName)
        {
            foreach (var p in Providers)
                if (p.Supports(vendorName)) return p;
            return null;
        }

        /// <summary>已支持的厂商清单（界面上提示用）</summary>
        public static string SupportedVendors()
        {
            return "联想（可自动查驱动）、华硕/天选、惠普、荣耀、华为、机械革命（仅引导）";
        }

        /// <summary>能自动查驱动清单的厂商（用于文案区分）</summary>
        public static string QueryableVendors()
        {
            var names = Providers.Where(p => p.CanQueryDrivers).Select(p => p.DisplayName);
            return string.Join("、", names);
        }
    }
}
