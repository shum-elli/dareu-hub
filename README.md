# Dareu Local

<p><img src="App/Assets/DareuLocal.png" width="88" alt="Dareu Local icon"></p>

**达尔优鼠标 / 键盘的非官方 WinUI 3 本地设置工具。**

C# / .NET 8 · Windows x64 · MIT · 1.0.0

目标是让支持型号通过 USB 或 2.4G 接收器在本地完成设置，无需打开 Web 驱动。项目与 DAREU 无隶属或背书关系。

## 功能

- 设备首页：选择设备后才展开功能栏，按型号隐藏不适用功能；有线优先，显示电量与充电状态，支持热插拔事件监听。
- 桌面驻留与自动化：开机自启、`--minimized` 静默启动到托盘、关闭最小化到托盘、托盘电量与快捷切换配置、低电量提醒、前台应用自动切换配置与 DPI / 配置切换系统通知。
- 鼠标：可视化按键映射与按键录入（含左键连发、狙击键临时降 DPI、横向滚轮、回报率切换）、多种触发模式宏、DPI 档位与快捷预设及实时试用、125–8000 Hz 回报率、休眠、传感器 / 引擎与无线远距离抗干扰模式（统一应用与回读校验），以及接收器固件版本查看。
- 标准 TG 键盘：标准 / Fn 层 7× 提速批量键位读取与循环模式宏、色块式灯光设置、画笔/橡皮擦/吸管/分区/一键 5 套主题预设逐键 RGB 与实时同步预览、5 种模式系统音频 FFT 频谱律动（含中央对称律动）、支持一键“按下+松开”与批量统一延时的键鼠双录宏编辑器、板载配置别名，以及支持型号的 SOCD 后按优先与 Win/Mac 状态查看。
- 本地事务型配置备份与回滚恢复、浅色 / 深色主题、脱敏诊断包导出、手动检查更新。
- 默认完全离线，本次运行使用的配置和日志不上传服务器（仅在手动点击“检查新版本”时访问 GitHub Releases）；音乐律动不使用麦克风、不保存音频。

公开版采用项目自绘设备示意图，**不附带官方产品照片、官方程序、DLL、固件或反编译源码**。键位图使用硬件事实数据，详见 [来源说明](THIRD_PARTY_NOTICES.md)。

## 支持范围与验证状态

当前重点设备是 **AE6 V2（按 1330 参数适配）** 和 **普通机械轴 EK75**。保留实际有线 / 接收器 PID 进行路由。

设备库收录 218 条记录，其中 37 条接入基础设置适配。这不代表 218 款均已完整支持。其他型号未逐台实测；磁轴 RT / 键程、LCD、蓝牙配置与固件刷写未实现。详见 [设备列表](SUPPORTED-DEVICES.md)。

已通过：Release 构建、DPI / 宏编解码、报文校验、USB 优先、设备能力过滤等离线检查。开发中已验证 AE6 V2 有线部分参数原值写回、无线状态读取，及 EK75 有线 / 无线基础读写、临时 RGB 帧和音频律动恢复。

**仍需验证：** 最新版桌面驻留、托盘快捷切档、前台应用自动切换、实时 DPI 连续改值与不同固件兼容性。EK75 无线固件不支持已保存 RGB 颜色的读回，应用会说明未能验证颜色；颜色持久化尚未完成实机验证。备份恢复支持失败自动回滚，但如果设备在回滚期间断开仍需通过本地自动备份恢复。

## 使用

从 [Releases](https://github.com/shum-elli/dareu-local/releases) 下载 ZIP，完整解压后运行 `DareuLocal.App.exe`。请先关闭其他达尔优设置程序或 Web 驱动，避免同时访问设备。

| 版本 | 运行要求 |
| --- | --- |
| Slim 精简版 | .NET 8 x64 Runtime + Windows App Runtime 1.8 x64 |
| Portable 便携版 | 自带上述运行库，体积较大 |

.NET 运行库可从 [微软 .NET 8 页面](https://dotnet.microsoft.com/download/dotnet/8.0) 获取；Windows App Runtime 见 [微软下载页](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads)。

DPI 页的“实时试用”默认开启，调节后会直接修改板载值，不会在离开页面后自动恢复；可在“档位数量与恢复”中还原进入页面时的值。最小化收进托盘；可在“设置与诊断”中启用关闭按钮隐藏到托盘、开机自启与 `--minimized` 静默启动。点击托盘图标恢复，右键可查看设备电量、快速切换配置或退出。

本地数据目录：`%LOCALAPPDATA%/DareuLocal`。本仓库不含个人备份、宏、设备日志或凭据。

## 构建

需要 Windows、.NET 8 SDK 和 Windows SDK（首次 NuGet 还原需联网）。

```powershell
dotnet test Tests/DareuLocal.Tests.csproj -c Release
dotnet run --project Probe/Probe.csproj -c Release -- --self-test
# 精简版
dotnet publish App/DareuLocal.App.csproj -c Release -p:Platform=x64 -p:PublishProfile=Slim -o publish/slim
# 自带运行库
dotnet publish App/DareuLocal.App.csproj -c Release -p:Platform=x64 -p:PublishProfile=Portable -o publish/portable
```

GitHub Actions 在 Windows 上执行单元测试与离线自检并构建两种运行包。CI **不会连接真实硬件或运行设备写入检查**。

```text
App/        WinUI 3 界面、音频回环、托盘、OSD、前台监听
Core/       HID、设备目录、串行队列、FFT、配置 / 宏 / RGB 协议
Profiles/   精简后的设备事实与键位数据
Tests/      xUnit 离线单元测试
Probe/      离线自检及显式硬件诊断
AudioProbe/ 会临时改变灯光的开发用音频检查
```

开发检查 `--roundtrip`、`--verify-same-value`、`--rgb-preview`、`--rgb-roundtrip` 及 AudioProbe 会写设备或临时改变灯效，请阅读代码后再使用；日常构建无需执行。

## 反馈与贡献

提交 Issue 时请提供型号、硬件 PID、连接方式、应用版本、固件版本和复现步骤。分享日志前删除设备序列号等个人信息。协议或新型号适配请同时提供只读识别结果，明确哪些操作经过实机验证。

项目代码与自绘图标 / 示意图采用 [MIT](LICENSE)。第三方库遵循各自许可，品牌与商标属于其权利人。详见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
