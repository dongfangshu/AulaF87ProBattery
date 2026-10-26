# 狼蛛 F87 Pro 电量托盘

Windows 10/11 的 2.4G 键盘电量小工具。后台通过接收器查询电量，在电脑右下角的通知区域直接显示数字。

## 使用

双击 `dist\AulaBattery\AulaBattery.exe`。这是包含运行时的 Windows x64 单文件程序，无需安装 .NET，也无需管理员权限。

- 数字：键盘报告的电量百分比，绿色为正常、棕色为 21–40%、红色为 1–20%。
- 蓝色闪电：外部供电。此时固件可能始终返回 100，因此不显示百分比。
- 灰底数字：上次有效电量。键盘休眠、接收器拔出或查询暂时失败时保留数字，菜单和状态窗口显示原始读数时间，不会把旧读数当作实时电量。
- 灰色问号：尚未取得过有效读数。首次读到电量之前每 5 秒重试，成功后默认每分钟查询一次；右键可立即刷新，或调整为 30 秒、1 分钟、5 分钟。
- 左键查看状态，右键可选择开机启动、导出诊断、退出。开机启动默认关闭。
- 收到设备新上报的电量低于或等于 **10%** 时，通过 Windows 原生托盘通知提醒“电量不足”。每次运行中同一低电量阶段仅提醒一次；接上电源或电量恢复到 15% 后重新允许提醒。缓存的旧读数不会触发通知。右键“测试低电量通知”可预览提醒。

如果图标在右下角的 `^` 隐藏区域，把它拖到任务栏通知区域即可保持可见。Windows 自己决定图标是否折叠，程序不会修改该系统设置。

接收器需为 `VID 3554 / PID FA09`，键盘切至 2.4G 模式。其他硬件版本（包括 F87 Pro V2）尚未适配。休眠后若暂时无法读取，按键唤醒并点击“立即刷新”。发生占用错误时，退出狼蛛官方驱动后重试。

移动 EXE 后，若之前开启了开机启动，请在新位置运行并重新勾选。卸载时先取消开机启动并退出，然后删除 EXE 即可。刷新间隔保存在 `HKCU\Software\AulaBattery`；开机启动使用当前用户的 `Run\AulaBatteryF87Pro`。上次有效电量保存在 `%LOCALAPPDATA%\AulaBattery\last-reading.json`，下次启动时会以灰底读数恢复。

## 构建与验证

需要 Windows 和 .NET 10 SDK。Visual Studio 可打开根目录的 `AulaBattery.sln`，将 `AulaBattery.Windows` 设为启动项目。解决方案包括协议核心、Windows 托盘程序和检查程序三个项目。也可以直接通过各自的 `.csproj` 构建。

命令行构建，在项目目录运行：

```powershell
.\scripts\build.ps1
```

脚本构建程序、运行 55 项无外部测试框架依赖的检查，并将独立 EXE 发布至 `dist\AulaBattery`。可选参数 `-PackagesPath` 和 `-Source` 用于指定已有 NuGet 缓存及离线源。运行时只使用 Windows/.NET 自带 API。

单独运行检查：

```powershell
dotnet run --project tests\AulaBattery.Tests -c Release
```

导出实机通信记录前，先退出托盘程序，避免两个实例同时访问接收器：

```powershell
Start-Process .\dist\AulaBattery\AulaBattery.exe -ArgumentList '--diagnose', '.\diagnostics.json' -Wait
```

诊断退出码：`0` 收到电量回复，`1` 发生异常，`2` 没有回复或不支持设备，`3` 程序已在运行。诊断文件包含本机 HID 设备路径，请自行决定是否分享。

## 协议与实测

百分比是接收器返回的设备估计值，本程序没有读取原始电池电压、电流或剩余 mAh，也没有对百分比做换算或平滑。因此数字不代表已验证到 1% 精度的真实剩余容量。充放电切换后的数值变化，需要区分设备估计值变化和灰底缓存切换为新读数；详见协议文档的准确度说明。

参考 [RadianceADM 的协议记录](https://github.com/RadianceADM/aula-f87-control/blob/main/docs/PROTOCOL.md) 与 [marcoslor 的 F87 协议记录](https://github.com/marcoslor/Aula-F87-Controller/blob/main/docs/PROTOCOL.md)。实现细节见 [docs/PROTOCOL.md](docs/PROTOCOL.md)。

2026-10-26 在本机 Windows 10 x64、`3554:FA09` 接收器上测试：插线时正确识别外部供电，拔线后使用打包 EXE 读取到 **95%**；修复版在用户交互桌面运行后读取到 **92%** 并保存读数缓存。构建零警告、42 项检查通过，包括两位/三位数字完整性和查询超时保留上次读数。原始诊断保存在本地 `artifacts` 中。

托盘程序应在用户的交互桌面启动。Codex 隔离桌面中运行的进程不会把图标显示在用户托盘中，不能仅凭进程存在判断显示成功。
