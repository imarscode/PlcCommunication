# 贡献指南 | Contributing Guide

感谢您对 PlcCommunication 项目的关注！欢迎贡献代码、报告问题或提出建议。

## 🐛 问题反馈

- 使用 [GitHub Issues](../../issues) 提交 Bug 报告或功能建议
- 请提供：PLC 型号、协议、地址格式、复现步骤、期望结果

## 🔧 代码贡献

1. Fork 本仓库
2. 创建功能分支：`git checkout -b feature/your-feature`
3. 提交更改：`git commit -m 'Add some feature'`
4. 推送分支：`git push origin feature/your-feature`
5. 提交 Pull Request

### 代码规范

- 遵循现有代码风格
- 所有公共 API 必须有 XML 文档注释
- 新增协议实现必须继承 `NetworkDeviceBase`
- 确保编译通过（0 错误，0 警告）

### 新增协议检查清单

- [ ] 继承 `NetworkDeviceBase`
- [ ] 实现 `BuildReadCommand` / `BuildWriteCommand` / `CheckResponse` / `GetResponseLength`
- [ ] 如需特殊连接握手，重写 `ConnectAsync`
- [ ] 如需特殊接收逻辑，重写 `ReceiveAsync`
- [ ] 地址解析异常使用 `PlcCommunicationException`
- [ ] 通信错误返回 `OperateResult.Fail`，不抛异常
- [ ] 添加详细的错误码描述
- [ ] 编写测试用例

## 📞 联系方式

- **作者**：ljh
- **QQ**：3010812967
- **GitHub Issues**：[提交问题](../../issues)

## 📜 许可

贡献的代码将遵循 MIT 协议发布。
