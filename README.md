# EMG 电容 静电计多通道采集软件

Windows x64 多通道信号采集软件，支持 EMG、电容和静电计三种 Wi-Fi 采集板。

本项目已经开源，包含 WinForms 主程序、协议解析、CSV 导出、Wi-Fi 连接、
防火墙配置工具和自动化测试。厂家原始协议 PDF、Word、Excel 文件不在仓库中
重新发布，已确认的协议参数整理在项目文档和代码中。

## 下载

请从 [Releases](https://github.com/xf959211192/emg-capacitance-electrometer-collector/releases/latest) 下载：

`EMG-Capacitance-Electrometer-Collector-v17-win-x64.zip`

普通版已经包含 .NET 运行环境。下载后必须完整解压，再双击
`EMG电容静电计采集软件.exe`。不要只复制或单独发送 EXE，目录中的 DLL
也是程序运行所需文件。

## 软件界面

![静电计八通道采集界面](docs/software-interface.png)

## 支持的板卡

| 模式 | 通道 | Wi-Fi | 默认密码 | 默认采样率 | UDP 端口 | 单位 |
| --- | ---: | --- | --- | ---: | ---: | --- |
| EMG | 8 | `WNZL_Emg` | `Eemg1234` | 1000 Hz | 8060 | V |
| 电容 | 35 个测量通道加 5 个参考通道 | `ESP32_Pcap35` | `pcap1234` | 每通道 100 Hz | 8080 | pF |
| 静电计 | 8 | `WNZL_Emeter8` | `emeter1234` | 1000 Hz | 8060 或 8080 | μA nC V |

板卡协议要求电脑无线网卡地址为 `192.168.4.2`。

## 主要功能

- 三种采集模式在主页切换，连接板卡热点后自动匹配模式。
- 软件启动时不会主动连接板卡；点击连接或开始真实采集后才连接。
- 当前模式连接失败后依次尝试其余板卡，连接过程中可以暂停。
- 分通道波形与全部通道叠加图。
- 通道全选、反选、全不选以及独立显示和导出开关。
- 数据包、丢包、乱序、无效包和设备状态监测。
- CSV 导出接收时间、相对时间和所选通道数据。
- 一次配置 UDP 8060 和 8080 的专用及公用网络防火墙规则。
- 静电计 CH1 支持 nA、μA、mA 档位，必须与板上拨码一致。

## 首次使用

1. 完整解压下载的 ZIP。
2. 运行 `EMG电容静电计采集软件.exe`。
3. 选择板卡模式并点击“连接板卡 Wi-Fi”。
4. 等待软件获得 `192.168.4.2`，最长可能需要约 30 秒。
5. 首次真实采集时同意防火墙授权，并在管理员确认窗口点击“是”。
6. 核对采样率、UDP 端口和静电计 CH1 档位，然后开始采集。
7. 详细操作和故障排查请查看 ZIP 内的 Word 使用说明书。

连接板卡热点后电脑暂时没有互联网属于正常现象。采集结束后可在软件中点击
“恢复原 Wi-Fi”。

## 文件校验

v17 含详细说明压缩包 SHA-256：

```text
BEDDEAFCA0D0724A557AF3078B47FD1D56DA949D2CA9C1D57CC5B412B8A3481B
```

当前程序没有数字签名，Windows 可能显示 SmartScreen 提示。请只从本仓库的
Releases 下载，并在运行前核对上述 SHA-256。

## 从源代码构建

需要安装 .NET 8 SDK 和 Windows x64 环境。

```powershell
dotnet restore "EmgCollector.sln"
dotnet build "EmgCollector.sln" -c Release
dotnet run --project "tests/EmgCollector.ProtocolTests/EmgCollector.ProtocolTests.csproj" -c Release
dotnet run --project "tests/EmgCollector.UiSmoke/EmgCollector.UiSmoke.csproj" -c Release
```

生成包含 .NET 运行环境的普通版：

```powershell
dotnet publish "src/EmgCollector/EmgCollector.csproj" `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true
```

真实 Wi-Fi 测试会切换电脑当前网络，不属于默认测试流程。执行前请先阅读
`tests/EmgCollector.WifiSmoke` 中的测试入口。

## 项目结构

- `src/EmgCollector`：Windows 主程序。
- `tests/EmgCollector.ProtocolTests`：协议解析、UDP 回环和 CSV 测试。
- `tests/EmgCollector.UiSmoke`：三种模式的界面冒烟测试。
- `tests/EmgCollector.WifiSmoke`：真实板卡 Wi-Fi 测试入口。
- `tools/EmgFirewallConfigurator`：管理员防火墙配置工具。

## 开源许可

代码使用 [MIT License](LICENSE)。第三方库和厂家硬件资料仍遵循其各自许可。

## 问题反馈

如果板卡已连接但没有数据，请在反馈中提供：

- 板卡模式和 Wi-Fi 名称；
- 本机 IPv4 地址；
- UDP 端口；
- 软件设备状态、数据包数和无效包数；
- 是否已经点击“配置防火墙”。
