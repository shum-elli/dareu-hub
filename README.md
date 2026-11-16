# Dareu Local

<p><img src="App/Assets/DareuLocal.png" width="88" alt="Dareu Local icon"></p>

**达尔优鼠标 / 键盘的非官方 WinUI 3 本地设置工具。**

C# / .NET 8 · Windows x64 · MIT · 预览版 0.7.1

目标是让支持型号通过 USB 或 2.4G 接收器在本地完成设置，无需打开 Web 驱动。项目与 DAREU 无隶属或背书关系。

## 功能

- 设备首页：选择设备后才展开功能栏，按型号隐藏不适用功能；有线优先，显示电量与充电状态。
- 鼠标：按键映射、宏、DPI 档位与实时试用、回报率、休眠，以及支持型号的传感器 / 引擎选项。
- 标准 TG 键盘：标准 / Fn 层按键映射、灯效、逐键 RGB、系统音频律动、宏与板载配置。
- 本地配置备份与恢复、浅色 / 深色主题、顶部拖动、最小化到托盘。
- 本次运行使用的配置和日志不上传服务器；音乐律动不使用麦克风、不保存音频。

公开版采用项目自绘设备示意图，**不附带官方产品照片、官方程序、DLL、固件或反编译源码**。键位图使用硬件事实数据，详见 [来源说明](THIRD_PARTY_NOTICES.md)。

## 支持范围与验证状态

当前重点设备是 **AE6 V2（按 1330 参数适配）** 和 **普通机械轴 EK75**。保留实际有线 / 接收器 PID 进行路由。

设备库收录 218 条记录，其中 37 条接入基础设置适配。这不代表 218 款均已完整支持。其他型号未逐台实测；磁轴 RT / 键程、LCD、蓝牙配置与固件刷写未实现。详见 [设备列表](SUPPORTED-DEVICES.md)。

已通过：Release 构建、DPI / 宏编解码、报文校验、USB 优先、设备能力过滤等离线检查。开发中已验证 AE6 V2 有线部分参数原值写回、无线状态读取，及 EK75 有线 / 无线基础读写、临时 RGB 帧和音频律动恢复。

**仍需验证：** 最新版标题栏拖动与托盘完整交互、实时 DPI 连续改值、不同固件兼容性。EK75 无线固件不支持已保存 RGB 颜色的读回，应用会说明未能验证颜色；颜色持久化尚未完成实机验证。备份恢复不是事务，中断时可能仅写入部分设置。

## 使用

从 [Releases](https://github.com/shum-elli/dareu-local/releases) 下载 ZIP，完整解压后运行 `DareuLocal.App.exe`。请先关闭其他达尔优设置程序或 Web 驱动，避免同时访问设备。

| 版本 | 运行要求 |
| --- | --- |
| Slim 精简版 | .NET 8 x64 Runtime + Windows App Runtime 1.8 x64 |
| Portable 便携版 | 自带上述运行库，体积较大 |

.NET 运行库可从 [微软 .NET 8 页面](https://dotnet.microsoft.com/download/dotnet/8.0) 获取；Windows App Runtime 见 [微软下载页](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads)。

DPI 页的“实时试用”默认开启，调节后会直接修改板载值，不会在离开页面后自动恢复；可在“档位数量与恢复”中还原进入页面时的值。最小化收进托盘；点击托盘图标恢复，右键可退出。关闭按钮直接退出。

本地数据目录：`%LOCALAPPDATA%/DareuLocal`。本仓库不含个人备份、宏、设备日志或凭据。

## 构建

需要 Windows、.NET 8 SDK 和 Windows SDK（首次 NuGet 还原需联网）。

```powershell
dotnet run --project Probe/Probe.csproj -c Release -- --self-test
# 精简版
dotnet publish App/DareuLocal.App.csproj -c Release -p:Platform=x64 -p:PublishProfile=Slim -o publish/slim
# 自带运行库
dotnet publish App/DareuLocal.App.csproj -c Release -p:Platform=x64 -p:PublishProfile=Portable -o publish/portable
```

GitHub Actions 在 Windows 上执行离线自检并构建两种运行包。CI **不会连接真实硬件或运行设备写入检查**。

```text
App/        WinUI 3 界面、音频回环、托盘
Core/       HID、设备目录、配置 / 宏 / RGB 协议
Profiles/   精简后的设备事实与键位数据
Probe/      离线自检及显式硬件诊断
AudioProbe/ 会临时改变灯光的开发用音频检查
```

开发检查 `--roundtrip`、`--verify-same-value`、`--rgb-preview`、`--rgb-roundtrip` 及 AudioProbe 会写设备或临时改变灯效，请阅读代码后再使用；日常构建无需执行。

## 反馈与贡献

提交 Issue 时请提供型号、硬件 PID、连接方式、应用版本、固件版本和复现步骤。分享日志前删除设备序列号等个人信息。协议或新型号适配请同时提供只读识别结果，明确哪些操作经过实机验证。

项目代码与自绘图标 / 示意图采用 [MIT](LICENSE)。第三方库遵循各自许可，品牌与商标属于其权利人。详见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
