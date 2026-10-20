; AZ 驱动懒人安装 - Inno Setup 打包脚本
; 编译：ISCC.exe setup.iss
; 产物：..\Dist\Installer\AZ-DriverSetup-Setup-<版本>.exe

#define MyAppName "AZ 驱动懒人安装"
#define MyAppVersion "1.1.2.0"
#define MyAppPublisher "AZ"
#define MyAppExeName "DriverSetup.exe"
#define MyAppId "{{8F3A2B1C-4D5E-4F6A-9B7C-1D2E3F4A5B6C}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\AZ驱动懒人安装
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\Release
OutputBaseFilename=AZ-DriverSetup-Setup-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

; 必须是管理员：装驱动、读传感器都要提权
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; 外观
SetupIconFile=..\Src\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

; Windows 10 1809 (17763) 及以上：更早的版本 pnputil 参数与 DPI 支持不确定
MinVersion=10.0.17763

[Languages]
; 中文语言包（ChineseSimplified.isl）需要单独下载，国内各镜像均不可达，
; 因此使用英文向导界面；AppName / 任务描述仍是中文，安装过程不影响理解。
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："; Flags: unchecked

[Files]
; 自包含发布目录（Dist\App），包含 .NET 运行时，目标机器无需另装
Source: "..\Dist\App\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; shellexec：目标 exe 的 manifest 要求管理员，CreateProcess 会返回 740，
; 改用 ShellExecuteEx 才能正常弹 UAC 启动。
Filename: "{app}\{#MyAppExeName}"; Description: "立即运行 {#MyAppName}"; Flags: nowait postinstall skipifsilent shellexec

[UninstallDelete]
; 卸载时清掉运行时产生的数据（驱动包与备份体积可能很大）
Type: filesandordirs; Name: "{app}\Downloads"
Type: filesandordirs; Name: "{app}\Backup"
Type: files; Name: "{app}\debug.log"
Type: files; Name: "{app}\install-history.json"
Type: files; Name: "{app}\keyboard-layout.txt"
Type: files; Name: "{app}\restore-errors.txt"
