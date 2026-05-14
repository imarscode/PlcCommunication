# PlcCommunication

<div align="center">

# ⚡ PlcCommunication

**工业物联网通信基础设施 · 全球唯一免费开源可商用**

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-Standard%202.0%20%7C%208.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![NuGet](https://img.shields.io/nuget/v/PlcCommunication.svg)](https://www.nuget.org/packages/PlcCommunication/)

**跨平台 · 全协议 · 零依赖 · 工业级**

[English](#english) · [中文](#中文)

</div>

---

## 中文

### 🎯 项目简介

PlcCommunication 是一个**完全免费、开源、可商业使用**的 .NET 工业通信底层框架，覆盖主流 PLC 与 Modbus 全协议读写，帮助开发者快速构建上位机、组态软件、SCADA 系统和 MES 平台，赋能工业 4.0 智能制造。

**核心优势：**
- 🆓 **完全免费** — MIT 协议，个人/企业均可商用，无需付费
- 🌍 **全球唯一** — 同时覆盖西门子/三菱/欧姆龙/罗克韦尔/Modbus 的开源库
- 📦 **零依赖** — 纯 C# 实现，不依赖任何第三方库
- 🔌 **即插即用** — 统一 API 接口，5 行代码完成 PLC 通信
- 🛡️ **工业级稳定** — 线程安全、指数退避重试、超时控制、诊断跟踪
- 🖥️ **跨平台** — 支持 .NET Standard 2.0 + .NET 8.0，Windows/Linux/macOS

### 🔌 支持协议

| 协议 | PLC 型号 | 默认端口 | 状态 |
|------|----------|---------|------|
| **Modbus TCP** | 所有 Modbus TCP 设备 | 502 | ✅ 稳定 |
| **Modbus RTU over TCP** | RTU 网关 | 502 | ✅ 稳定 |
| **Modbus ASCII over TCP** | ASCII 网关 | 502 | ✅ 稳定 |
| **西门子 S7** | S7-200/300/400/1200/1500 | 102 | ✅ 稳定 |
| **三菱 MC (Melsec 3E)** | Q/L/QnA/FX 系列 | 5006 | ✅ 稳定 |
| **欧姆龙 FINS/TCP** | CJ/CS/CP/NJ 系列 | 9600 | ✅ 稳定 |
| **欧姆龙 HostLink** | CJ/CS 系列 | 9600 | ✅ 稳定 |
| **罗克韦尔 CIP (EtherNet/IP)** | ControlLogix/CompactLogix | 44818 | ✅ 稳定 |

### 🚀 快速上手

#### 安装 NuGet 包

```bash
dotnet add package PlcCommunication
```

#### 5 行代码读写 PLC

```csharp
using PlcCommunication.Protocols.Siemens;
using PlcCommunication.Core;

// 1. 创建设备
var plc = new SiemensS7Net(SiemensPLCS.S1200, "192.168.1.1");

// 2. 连接
await plc.ConnectAsync();

// 3. 读取
var result = await plc.ReadAsync("DB1.DBW0", 4);
if (result.IsSuccess)
    Console.WriteLine($"数据: {BitConverter.ToString(result.Content)}");

// 4. 写入
var data = BitConverter.GetBytes((short)1234);
await plc.WriteAsync("DB1.DBW0", data);

// 5. 断开
await plc.DisconnectAsync();
```

#### 类型化读写

```csharp
// 读取特定类型
var temperature = await plc.ReadFloatAsync("DB1.DBD100");
var counter = await plc.ReadInt32Async("DB1.DBD200");
var status = await plc.ReadBoolAsync("DB1.DBX0.0");

// 写入特定类型
await plc.WriteAsync("DB1.DBD100", 25.5f);
await plc.WriteAsync("DB1.DBD200", 1000);
await plc.WriteAsync("DB1.DBX0.0", true);
```

#### Modbus TCP

```csharp
using PlcCommunication.Protocols.Modbus;

var modbus = new ModbusTcpNet("192.168.1.2", 502, stationId: 1);
await modbus.ConnectAsync();

// 读取保持寄存器
var data = await modbus.ReadInt16Async("100");

// 写入多个寄存器
await modbus.WriteAsync("100", (short)500);
```

#### 三菱 MC

```csharp
using PlcCommunication.Protocols.Mitsubishi;

var melsec = new MitsubishiMcNet("192.168.1.3", 5006);
await melsec.ConnectAsync();

var temp = await melsec.ReadFloatAsync("D100");
var relay = await melsec.ReadBoolAsync("M0");
```

#### 欧姆龙 FINS

```csharp
using PlcCommunication.Protocols.Omron;

var omron = new OmronFinsNet("192.168.1.4", 9600);
omron.DstNode = 1;
omron.SrcNode = 0;
await omron.ConnectAsync();

var pressure = await omron.ReadFloatAsync("D100");
var flag = await omron.ReadBoolAsync("CIO100.05");
```

#### 罗克韦尔 CIP

```csharp
using PlcCommunication.Protocols.AllenBradley;

var ab = new AllenBradleyCipNet("192.168.1.5", 44818);
ab.Slot = 0;
await ab.ConnectAsync();

var tagValue = await ab.ReadInt32Async("MyTag");
await ab.WriteAsync("MyTag", 42);
```

### 🏗️ 架构设计

```
PlcCommunication
├── Core/                    # 核心抽象层
│   ├── IReadWriteNet        # 读写契约接口
│   ├── IReadWriteNetExtensions # 类型化扩展方法
│   ├── NetworkDeviceBase    # 通信基础设施（重试/超时/锁/诊断）
│   └── OperateResult        # 操作结果模式
├── DataConvert/             # 字节序转换
│   ├── IByteTransform       # 转换接口
│   ├── RegularBytesTransform # 小端序（三菱/AB）
│   └── ReverseBytesTransform # 大端序（西门子/欧姆龙）
├── Diagnostics/             # 诊断与跟踪
│   ├── ITraceable           # 可跟踪接口
│   └── LogManager           # 全局日志路由
├── Protocols/               # 协议实现
│   ├── Modbus/              # Modbus TCP/RTU/ASCII
│   ├── Siemens/             # S7 协议 + 地址解析
│   ├── Mitsubishi/          # MC 3E 二进制帧
│   ├── Omron/               # FINS/TCP + HostLink
│   └── AllenBradley/        # CIP/EtherNet/IP
└── Utilities/               # 工具类
    ├── SoftBasic            # 字节/十六进制转换
    ├── SoftByte             # 字节数组操作
    └── SoftTimer            # 高精度计时器
```

### 🎨 WinForms 调试工具

自带专业级 WinForms 通信调试工具，开箱即用：

- **📝 读写操作** — 11种数据类型读写，快捷地址，Bool位操作
- **📊 数据监视** — 表格化实时监控，定时轮询（100ms~60s）
- **📋 批量读写** — 多地址批量读取，统计报告
- **⭐ 地址书签** — 保存常用地址，7种协议格式参考
- **📜 日志输出** — 彩色日志，导出功能
- **💾 配置保存** — 自动记忆连接参数

### 📋 地址格式参考

| 协议 | 格式 | 示例 |
|------|------|------|
| **Modbus** | 寄存器偏移 | `0`, `100`, `40001` |
| **西门子 S7** | DB块+偏移 | `DB1.DBW0`, `DB1.DBD0`, `DB1.DBX0.0`, `MW0`, `IW0` |
| **三菱 MC** | 设备+地址 | `D0`, `D100`, `M0`, `X0`, `Y0`, `M10.3` |
| **欧姆龙 FINS** | 区域+地址 | `D100`, `DM100`, `CIO100`, `W100`, `H100`, `D100.05` |
| **罗克韦尔 CIP** | 标签名 | `MyTag`, `MyTag[0]`, `MyTag.Member` |

### 🔧 高级特性

#### 线程安全
所有通信操作通过 `SemaphoreSlim` 串行化，多线程安全调用：
```csharp
// 多个线程可以安全地使用同一个设备实例
Task.WhenAll(
    plc.ReadInt16Async("DB1.DBW0"),
    plc.ReadFloatAsync("DB1.DBD100"),
    plc.ReadBoolAsync("DB1.DBX0.0")
);
```

#### 指数退避重试
通信失败自动重试，指数退避避免雪崩：
```csharp
var plc = new SiemensS7Net(SiemensPLCS.S1500, "192.168.1.1");
plc.RetryCount = 3;           // 最多重试3次
plc.RetryIntervalMs = 100;    // 基础间隔100ms，指数递增
```

#### 诊断跟踪
内置跟踪系统，方便调试：
```csharp
plc.EnableTrace = true;
plc.TraceMessage += (s, e) => 
{
    Console.WriteLine($"[{e.Level}] {e.Message} ({e.ElapsedMilliseconds}ms)");
};
```

### 🤝 技术支持

- **作者**：ljh
- **QQ**：3010812967
- **Email**：usmars@qq.com
- **GitHub**：[https://github.com/usmars/PlcCommunication](https://github.com/usmars/PlcCommunication)
- **问题反馈**：[GitHub Issues](../../issues)

欢迎加入技术交流，提供 PLC 远程调试环境可协助协议适配。

### 📜 开源协议

[MIT License](LICENSE) — 完全免费，个人/企业均可商用，无需授权。

---

## English

### 🎯 Overview

PlcCommunication is a **completely free, open-source, commercially usable** .NET industrial communication library covering all major PLC and Modbus protocols. Build HMI, SCADA, and MES systems in minutes.

**Key Features:**
- 🆓 **Free Forever** — MIT license, commercial use allowed
- 🌍 **Unique** — The only open-source library covering Siemens/Mitsubishi/Omron/Allen-Bradley/Modbus
- 📦 **Zero Dependencies** — Pure C# implementation
- 🔌 **Plug & Play** — Unified API, 5 lines of code to communicate
- 🛡️ **Industrial Grade** — Thread-safe, exponential backoff retry, timeout control, diagnostic tracing
- 🖥️ **Cross-Platform** — .NET Standard 2.0 + .NET 8.0, Windows/Linux/macOS

### 🚀 Quick Start

```bash
dotnet add package PlcCommunication
```

```csharp
using PlcCommunication.Protocols.Siemens;
using PlcCommunication.Core;

var plc = new SiemensS7Net(SiemensPLCS.S1200, "192.168.1.1");
await plc.ConnectAsync();

var result = await plc.ReadInt16Async("DB1.DBW0");
Console.WriteLine($"Value: {result.Content}");

await plc.WriteAsync("DB1.DBW0", (short)1234);
await plc.DisconnectAsync();
```

### 🤝 Support

- **Author**: ljh
- **QQ**: 3010812967
- **Email**: usmars@qq.com
- **GitHub**: [https://github.com/usmars/PlcCommunication](https://github.com/usmars/PlcCommunication)
- **Issues**: [GitHub Issues](../../issues)

### 📜 License

[MIT License](LICENSE) — Free for personal and commercial use.

---

<div align="center">

**Made with ❤️ by ljh (QQ: 3010812967, Email: usmars@qq.com)**

**如果这个项目帮助了您，请给个 ⭐ Star！**

</div>
