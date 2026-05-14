# PLC 通信库诊断指南

## 问题：读取 D100 时 "Index was outside the bounds of the array" 错误

### 已知修复

1. ✅ **西门子 S7 地址解析**：已修复 `DBW0` 简写格式支持

### 待诊断

1. ❓ **三菱 MC 读取错误**：需要更多信息

### 诊断步骤

1. **检查 PLC 模拟器**：
   - 确认模拟器类型（MX Component、Modbus Slave、自定义等）
   - 确认模拟器端口配置（三菱 MC 默认 5006）
   - 确认模拟器数据区配置

2. **检查日志输出**：
   - 启用详细日志：`dev.EnableTrace = true`
   - 观察 `[Send]` 和 `[Receive]` 日志
   - 检查发送的数据格式

3. **测试其他地址**：
   - 尝试 `D0`、`M0`、`D200` 等地址
   - 观察是否只有特定地址出错

### 调试建议

在 `MitsubishiMcNet.ReceiveAsync` 方法中添加详细日志：

```csharp
Trace(TraceLevel.Verbose, $"[Receive] _lastReadWordCount={_lastReadWordCount}, expectedDataLen={expectedDataLen}");
```

### 已知限制

1. 三菱 MC 3E 协议响应没有长度字段，需要根据请求推断响应长度
2. 如果 PLC 返回错误响应（完成码非 0），响应可能只有 9 字节

### 测试环境

- 服务器：Windows Server
- .NET 版本：8.0
- 项目路径：`C:\Users\Administrator\Desktop\plc`
- GitHub：https://github.com/imarscode/PlcCommunication

---

**作者**：ljh | QQ：3010812967
