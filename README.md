# AZ 驱动懒人安装

Windows 驱动检测与安装工具。读本机序列号 → 查厂商官网清单 → 与本机已装版本逐条比对 → 下载并静默安装；另带配置核对、固件提示、驱动备份回滚。

## 目录

| 目录 | 内容 |
|---|---|
| `App\` | **运行件**。双击 `DriverSetup.exe` 即用（.NET Framework 4.8，系统自带，无需安装运行时） |
| `Src\` | 源码（C# WinForms / net48）。改完执行 `dotnet build DriverSetup.csproj -c Release` |
| `Preview\` | 界面设计稿与实拍截图 |
| `Docs\` | 说明文档 |

首次运行后，`App\` 下会自动出现：

- `Downloads\` —— 从官网下载的驱动包
- `Backup\` —— 「备份驱动」导出的 pnputil 备份（体积可能数 GB）

驱动包仓库在 `D:\Driver`（按机型分目录），本工具不读写那里。

## 五个分区

| 分区 | 内容 |
|---|---|
| 配置核对 | CPU / 显卡 / 内存 / 硬盘 / 屏幕（EDID 含色域）/ 电池 / 网卡，全部只读 |
| 驱动安装 | 序列号查官网清单 → 与本机版本比对 → 下载 + 静默安装 + 装后校验 |
| 固件提示 | BIOS / ME 只给版本对比，不下载不执行 |
| 备份回滚 | 确认框 → 进度窗口 → pnputil 导出/回灌 |
| 硬件测试 | 104 键可视化键盘（按过变绿、可重置）+ 屏幕坏点全屏纯色检测 |

调试用环境变量：`DRV_START_PAGE=0..4` 可直接打开指定分区。

## 编译

```
cd Src
dotnet build DriverSetup.csproj -c Release -v:minimal
```

产物：`Src\bin\Release\net48\DriverSetup.exe`，复制到 `App\` 覆盖即可。

## 红线

- BIOS / ME 固件**只提示、不下载、不执行**（刷写失败机器变砖）
- 配置核对**全部只读**（WMI / 注册表 EDID / powercfg）
- 安装前会确认，装完校验版本真变了才报成功
