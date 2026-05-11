# ruri-bot 部署与运行指南

## 前提：架构理解

你的 QQ 号登录是由 **NapCatQQ**（位于 `E:\project\C#\NapCat.Framework\`）负责的，不是由 ruri-bot 负责的。它们的协作关系：

```
你的 QQ 号
    │
    ▼
NapCatQQ (E:\project\C#\NapCat.Framework\)
    │  注入到 QQ.exe，登录后启动 WebSocket 服务
    │  监听: ws://0.0.0.0:3001
    │
    ▼  WebSocket
ruri-bot (C# 机器人框架)
    │  连接: ws://127.0.0.1:3001
    │
    ▼
你的模块 (DemoModule: /ping /echo /about /help)
```

**步骤简述：**
1. 启动 NapCatQQ（双击 napiLoader.bat → 注入 QQ → 扫码登录）
2. 编译并运行 ruri-bot（连接 NapCatQQ 的 WebSocket）
3. 在 QQ 中发送 `/ping` 测试

---

## 第一步：启动 NapCatQQ（登录 QQ）

### 1.1 WebSocket 配置（已完成）

已在 `E:\project\C#\NapCat.Framework\config\onebot11.json` 中配置好 WebSocket 服务器：

```json
{
    "network": {
        "websocketServers": [
            {
                "name": "ruri-bot-server",
                "enable": true,
                "host": "0.0.0.0",
                "port": 3001,
                "messagePostFormat": "array",
                "reportSelfMessage": false,
                "token": "",
                "enableForcePushEvent": true,
                "heartInterval": 15000
            }
        ]
    }
}
```

**如需修改端口**：编辑 `port` 字段，同时同步修改 ruri-bot 启动参数。

### 1.2 启动 NapCat

**提示：** 使用机器人专用的 QQ 号，不要使用主号。

1. 进入 `E:\project\C#\NapCat.Framework\`
2. 双击 **`napiLoader.bat`**
3. 这会自动从注册表找到你安装的 QQ 路径，启动 QQ.exe 并注入 NapCat
4. 在弹出的 QQ 登录窗口中**扫码登录**
5. 登录成功后，控制台会显示 `[NapCat] [Info]` 相关日志
6. WebSocket 服务器自动在 3001 端口启动

### 1.3 验证

等 QQ 登录完成后，观察：

- 控制台出现类似 `[OneBot] [HTTP Server Adapter] Start On 0.0.0.0:3001` 的日志
- 用 `netstat -an | findstr 3001` 确认端口在监听

---

## 第二步：编译 ruri-bot

### 2.1 环境要求

- **Visual Studio 2019+** 或 **.NET 6.0 SDK**
- 如果没有 .NET 6.0 SDK：[下载地址](https://dotnet.microsoft.com/download/dotnet/6.0)

### 2.2 使用命令行编译（推荐）

```powershell
cd E:\project\C#\ruri-bot

# 确保 NapCatSharpLib Release 版本存在；只有 Debug 版本时先编译它
dotnet build ..\NapCatSharpLib\NapCatSharpLib.sln -c Release

# 编译 ruri-bot 全部项目
dotnet build RuriBot.sln -c Debug

# 将 DemoModule.dll 复制到 modules 目录
copy RuriBot.DemoModule\bin\Debug\RuriBot.DemoModule.dll modules\
```

### 2.3 编译后的目录结构

```
ruri-bot\
├── RuriBot.Runner\bin\Debug\
│   ├── RuriBot.Runner.exe           ← 启动入口
│   ├── RuriBot.Core.dll
│   ├── RuriBot.Library.dll
│   ├── Newtonsoft.Json.dll
│   └── NapCatSharpLib.dll
├── data\
│   └── permission\
│       └── permission.json          ← 全局权限
└── modules\
    └── RuriBot.DemoModule.dll       ← Demo 模块
```

### 2.4 如果只有 .NET Framework

将 `RuriBot.Runner\RuriBot.Runner.csproj` 中的：
```xml
<TargetFramework>net6.0</TargetFramework>
```
改为：
```xml
<TargetFramework>net48</TargetFramework>
```

---

## 第三步：配置权限

编辑 `data/permission/permission.json`，**将你的 QQ 号填入 superuser**：

```json
{
    "superuser": [123456789],
    "admin": [],
    "blacklist": []
}
```

---

## 第四步：运行 ruri-bot

### 4.1 启动

```powershell
cd E:\project\C#\ruri-bot\RuriBot.Runner\bin\Debug
RuriBot.Runner.exe
```

### 4.2 预期输出

```
========================================
  ruri-bot QQ 机器人框架
========================================
  连接目标: ws://127.0.0.1:3001
========================================
[12:00:01] 功能初始化完成
[12:00:01] 数据初始化完成
[12:00:01] 模块初始化完成
[12:00:01] 加载模块: demo (演示模块)

Bot 已启动！等待消息中...
按 Ctrl+C 或 Enter 退出
```

---

## 第五步：测试

在你的 QQ 上，对机器人私聊发送：

| 命令 | 预期响应 |
|------|----------|
| `/ping` | `pong!` |
| `/echo 你好` | `你好` |
| `/about` | 模块信息 |
| `/help` | 帮助菜单 |

**群聊测试：**

由于 DemoModule 群聊权限默认是 whitelist（白名单），需要先启用：

1. 拉机器人进群
2. 在群内发送 `/rrbot enable demo`
3. 然后发送 `/ping` 测试

---

## 常见问题

### Q1: napiLoader.bat 找不到 QQ

**现象：** `provided QQ path is invalid`

**解决：**
1. 确认电脑上已安装 QQ（不是 TIM）
2. 如果 QQ 装在非标准路径，直接创建 `napiLoader_manual.bat`：

```bat
@echo off
chcp 65001
set NAPCAT_INJECT_PATH=%cd%\napiloader.dll
set NAPCAT_LAUNCHER_PATH=%cd%\napimain.exe
set NAPCAT_MAIN_PATH=%cd%/nativeLoader.cjs
start "" "%NAPCAT_LAUNCHER_PATH%" "你的QQ安装路径\QQ.exe" "%NAPCAT_INJECT_PATH%" "%NAPCAT_MAIN_PATH%"
```

### Q2: 编译找不到 NapCatSharpLib

先单独编译 NapCatSharpLib：
```powershell
dotnet build E:\project\C#\NapCatSharpLib\NapCatSharpLib.sln -c Release
```

如果只有 Debug 版本，修改 ruri-bot 各 .csproj 中的 HintPath 从 `Release` 改为 `Debug`。

### Q3: 连接不上

1. 确认 NapCat QQ 已登录成功
2. 确认 config/onebot11.json 中 WebSocket 已 enable
3. 确认端口一致（默认 3001）

### Q4: 发命令没反应

1. 消息必须以 `/` 开头
2. 群聊需要先 `/rrbot enable demo`
3. 查看 ruri-bot 控制台是否有收到消息的日志

---

## 项目文件总览

```
E:\project\C#\
├── NapCat.Framework\                 ← NapCatQQ 运行环境
│   ├── napiLoader.bat                ← 双击启动
│   ├── config\
│   │   ├── napcat.json               ← 框架配置
│   │   └── onebot11.json             ← WebSocket 配置（已创建）
│   ├── napimain.exe
│   └── napcat.mjs
│
├── NapCatSharpLib\                   ← C# 通信库
│   └── NapCatSharpLib\
│
└── ruri-bot\                         ← C# 机器人框架
    ├── RuriBot.sln
    ├── SETUP-GUIDE.md                 ← 本文件
    ├── AI-DEVELOPMENT-GUIDE.md        ← 开发文档
    ├── RuriBot.Runner\                ← 启动项目
    ├── RuriBot.DemoModule\            ← 演示模块
    ├── RuriBot\                       ← 核心引擎
    ├── RuriBot.Library\               ← 共享库
    ├── data\
    │   └── permission\
    │       └── permission.json
    └── modules\
```

## 快速迭代流程

1. 修改 DemoModule.cs
2. 编译：`dotnet build RuriBot.sln -c Debug`
3. 复制：`copy RuriBot.DemoModule\bin\Debug\RuriBot.DemoModule.dll modules\`
4. 重启 RuriBot.Runner.exe（Ctrl+C 然后重新运行）
5. QQ 测试新功能

**注意：** ruri-bot 不支持模块热重载。
