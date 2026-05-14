# 更新日志 | Changelog

所有重要更改均记录在此文件中。

## [1.0.0] - 2026-05-14

### 🎉 首次发布

#### 核心框架
- 统一 `IReadWriteNet` 接口，所有协议共享同一 API
- `NetworkDeviceBase` 抽象基类，内置线程安全、指数退避重试、超时控制
- `IReadWriteNetExtensions` 类型化扩展：Int16/UInt16/Int32/UInt32/Int64/UInt64/Float/Double/String/Bool
- `OperateResult` 操作结果模式，通信失败不抛异常
- `IByteTransform` 字节序抽象，自动适配大端/小端
- `LogManager` 全局诊断跟踪系统

#### 协议支持
- **Modbus TCP** — MBAP 头 + 功能码 0x03/0x10
- **Modbus RTU over TCP** — RTU 帧 + CRC16 校验
- **Modbus ASCII over TCP** — ASCII 帧 + LRC 校验
- **西门子 S7** — ISO-on-TCP + COTP 握手 + PDU 协商 + S7 读写
- **三菱 MC** — 3E 二进制帧 + 标准设备代码
- **欧姆龙 FINS/TCP** — FINS/TCP 握手 + 内存区域读写
- **欧姆龙 HostLink** — HostLink 命令帧
- **罗克韦尔 CIP** — EIP 封装 + 会话注册 + CIP 标签读写

#### WinForms 调试工具
- 📝 读写操作 — 11种数据类型，Bool 位操作
- 📊 数据监视 — 表格化实时监控，定时轮询
- 📋 批量读写 — 多地址批量读取
- ⭐ 地址书签 — 保存/加载常用地址
- 📜 日志输出 — 彩色日志，导出功能
- 💾 配置保存 — 自动记忆连接参数

#### 目标框架
- .NET Standard 2.0（最大兼容性）
- .NET 8.0（最新性能优化）

#### 作者
- **ljh** (QQ: 3010812967)
