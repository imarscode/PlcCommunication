# GitHub 仓库配置指南

## 仓库设置

### 1. 创建仓库
- 名称：`PlcCommunication`
- 描述：`⚡ 工业物联网通信基础设施 | Siemens/三菱/欧姆龙/罗克韦尔/Modbus 全协议 | MIT License | 作者：ljh QQ：3010812967 Email：usmars@qq.com`
- 可见性：Public
- ✅ Add a README file（不要勾，已有）
- .gitignore：Visual Studio（已有自定义）
- License：MIT（已有）

### 2. 初始化 Git
```bash
cd C:\Users\Administrator\Desktop\plc
git init
git add .
git commit -m "🎉 v1.0.0 - 首次发布：7种PLC协议，工业级通信库"
git branch -M main
git remote add origin https://github.com/usmars/PlcCommunication.git
git push -u origin main
```

### 3. GitHub 设置
- **About** 部分填写：
  - Description: `⚡ 工业物联网通信基础设施 | Siemens/三菱/欧姆龙/罗克韦尔/Modbus 全协议 | MIT License`
  - Website: 留空或填文档站
  - Topics: `plc`, `modbus`, `siemens`, `mitsubishi`, `omron`, `allen-bradley`, `industrial-automation`, `dotnet`, `scada`, `hmi`, `iiot`

### 4. 发布 NuGet 包
```bash
cd C:\Users\Administrator\Desktop\plc\src\PlcCommunication
dotnet pack -c Release
dotnet nuget push bin\Release\PlcCommunication.1.0.0.nupkg --api-key 你的KEY --source https://api.nuget.org/v3/index.json
```

### 5. 创建 Release
- Tag: `v1.0.0`
- Title: `v1.0.0 - 首次发布`
- 说明：从 CHANGELOG.md 复制

## 账户信息

- **GitHub 用户名**：usmars
- **Email**：usmars@qq.com
- **作者**：ljh
- **QQ**：3010812967
