# ruri-bot 开发与使用流程记录

> 本文档记录从零搭建 ruri-bot 开发环境到成功接收 QQ 消息的完整流程，包含所有踩坑记录和解决方案。

---

## 1. 项目架构

```
E:\project\C#\
├── NapCatSharpLib\          # C# WebSocket 通信库
│   └── NapCatSharpLib\      # netstandard2.0 类库
│       ├── WebSocket\        # NapCatWebSocket.cs（连接管理）
│       ├── API\              # NapCatAPI.cs（50+ API 方法）
│       ├── Event\            # 事件解析与分发
│       ├── Data\             # 数据传输对象
│       └── Message\          # CQ 消息构造器
│
├── NapCat.Framework\         # NapCatQQ 运行环境（Node.js）
│   ├── napiLoader_debug.bat  # 调试启动脚本
│   ├── config\
│   │   ├── napcat.json               # 框架配置
│   │   ├── onebot11.json             # OneBot 默认配置
│   │   └── onebot11_<QQ号>.json      # 账号专属配置（优先级最高）
│   └── napcat.mjs                    # 主运行时（4.2MB bundled）
│
└── ruri-bot\                # C# 机器人框架
    ├── RuriBot.Runner\       # 控制台启动项目（exe）
    ├── RuriBot.Core\         # 核心引擎
    ├── RuriBot.Library\      # 共享库（模块基类）
    ├── RuriBot.DemoModule\   # 演示模块
    ├── modules\              # 模块 DLL 存放目录
    ├── data\                 # 运行时数据
    └── AI-DEVELOPMENT-GUIDE.md       # API/架构参考文档
```

**通信链路：** QQ → NapCatQQ (扫码登录) → WebSocket `ws://0.0.0.0:3001` → ruri-bot → DemoModule

---

## 2. 环境准备

### 2.1 必需软件

| 软件 | 用途 |
|------|------|
| **QQ**（完整版，非 TIM） | NapCat 注入目标 |
| **.NET 6.0 SDK+** | 编译 ruri-bot |
| **Node.js**（NapCat.Framework 自带） | 运行 NapCatQQ |

### 2.2 NapCat.Framework 目录修复

**关键问题：** 文件夹 `E:\project\C#\` 中的 `#` 是 URL 特殊字符。NapCat 加载脚本时使用 `'file://' + path` 拼接路径，`#` 被当作 URL fragment 导致路径截断（`E:\project\C`）。

**修复：** 在 `nativeLoader.cjs` 和 `napcat.cjs` 中，将：
```js
await import('file://' + path.join(currentPath, './napcat.mjs'))
```
改为：
```js
const url = require('url');
const napcatUrl = url.pathToFileURL(path.join(currentPath, './napcat.mjs')).href;
await import(napcatUrl)
```

---

## 3. NapCatQQ 配置

### 3.1 WebSocket 服务器配置

文件：`config/onebot11.json`（默认配置）或 `config/onebot11_<机器人QQ号>.json`（账号专属）

```json
{
    "network": {
        "websocketServers": [
            {
                "name": "ruri-bot-server",
                "enable": true,
                "host": "0.0.0.0",
                "port": 3001,
                "messagePostFormat": "string",
                "reportSelfMessage": false,
                "token": "",
                "enableForcePushEvent": true,
                "heartInterval": 15000
            }
        ]
    }
}
```

**关键配置项：**

| 配置 | 值 | 说明 |
|------|-----|------|
| `messagePostFormat` | **`string`** | 必须为 `string`！NapCatSharpLib 的 `message` 字段定义为 `string`，`array` 格式会导致 JSON 反序列化失败 |
| `host` | `0.0.0.0` | 允许本地连接 |
| `port` | `3001` | 与 ruri-bot 启动端口一致 |
| `reportSelfMessage` | `false` | 防止机器人消息触发事件循环 |

### 3.2 账号专属配置文件

NapCatQQ 登录后会自动创建 `onebot11_<机器人QQ>.json`，该文件优先级高于 `onebot11.json`。如果自动生成的文件使用了默认配置（如 `messagePostFormat: "array"`），需要手动修改。

**判断机器人 QQ 号：** 在 ruri-bot 收到的 WebSocket 原始数据中查找 `"self_id"` 字段。

---

## 4. 编译步骤

```powershell
# 第一步：编译 NapCatSharpLib（有修改时必须先编译）
dotnet build E:\project\C#\NapCatSharpLib\NapCatSharpLib.sln -c Debug

# 第二步：编译 ruri-bot 全部项目
cd E:\project\C#\ruri-bot
dotnet build RuriBot.sln -c Debug

# DemoModule.dll 会由 MSBuild 自动复制到 modules/ 目录
```

**注意：** NapCatSharpLib 不在 RuriBot.sln 中，是独立 solution，必须单独编译。ruri-bot 通过 DLL HintPath 引用（路径指向 `bin\Debug\`）。

---

## 5. 运行前准备

### 5.1 创建数据目录首次运行需要的文件

`data/permission/permission.json`：
```json
{
    "superuser": [管理员QQ号],
    "admin": [],
    "blacklist": []
}
```

### 5.2 确保 modules 目录存在

首次运行前 `modules/` 目录必须存在并包含至少一个模块 DLL。

---

## 6. 启动流程

### 6.1 启动 NapCatQQ

```
双击 napiLoader_debug.bat
→ 自动从注册表查找 QQ 路径
→ 注入 NapCat 并启动 QQ
→ 扫码登录
→ 控制台出现 "登录成功" 类日志
→ WebSocket 服务自动在 3001 端口启动
```

### 6.2 启动 ruri-bot

```powershell
cd E:\project\C#\ruri-bot\RuriBot.Runner\bin\Debug
RuriBot.Runner.exe [ip] [port]
# 默认: RuriBot.Runner.exe 127.0.0.1 3001
```

### 6.3 预期输出

```
========================================
  ruri-bot QQ 机器人框架
========================================
  连接目标: ws://127.0.0.1:3001
========================================
[12:00:01] [Ruri-Bot] 功能初始化完成
[12:00:01] [Ruri-Bot] 数据初始化完成
[12:00:01] [Ruri-Bot] 模块初始化完成
[12:00:01] [Ruri-Bot] WS Connecting to ws://127.0.0.1:3001...
[12:00:01] [Ruri-Bot] WS Connected, entering main loop
[信息] WebSocket 连接成功！
========================================
  登录账号: 机器人昵称
  QQ 号码: 2081035049
========================================
Bot 已启动！等待消息中...
```

---

## 7. 群聊模块启用

DemoModule 默认群聊权限为 **whitelist**（白名单），需要手动启用：

```
在目标群里发送: /rrbot enable demo
```

验证：
```
/ping → 回复 pong!
/echo 你好 → 你好
/about → 模块信息
/help → 帮助
```

私聊不需要 `/rrbot enable`，直接可用。

---

## 8. 模块开发快速参考

### 8.1 模块必须实现的方法

```csharp
public class MyModule : RRBotModuleBase
{
    protected override void SetModuleInfo()           // 模块ID、名称、版本
    protected override void RegisterPrivateCommands() // 私聊命令
    protected override void UnregisterPrivateCommands()
    protected override void RegisterGroupCommands()   // 群聊命令
    protected override void UnregisterGroupCommands()
    protected override void RegisterNapCatEvents()    // QQ事件监听
    protected override void UnregisterNapCatEvents()
}
```

### 8.2 可用的注入依赖

| 字段 | 类型 | 用途 |
|------|------|------|
| `api` | `NapCatAPI` | 发送消息 |
| `cmdRegister` | `IRRBotCommandRegistry` | 注册命令 |
| `evtRegister` | `IRRBotEventRegistry` | 注册事件 |
| `ModulePermissionInterface` | `IRRBotModulePermissionOperation` | 权限对象（注册时传入） |
| `coreLogger` | `IRRBotLogger` | 日志输出 |

### 8.3 消息构造

```csharp
var chain = new CqMessageChain();
chain.Builder.AddReply(msg.message_id);  // 引用回复
chain.Builder.AddText("你好");
chain.Builder.AddAt(qq);                 // @某人
chain.Builder.AddImage("http://...");    // 图片
await api.SendGroupMessage(groupId, chain);
```

### 8.4 注册事件监听

```csharp
protected override void RegisterNapCatEvents()
{
    evtRegister.Register<NapCatMessagePrivate>(ModulePermissionInterface, OnPrivateMsg);
    evtRegister.Register<NapCatNoticeGroupIncrease>(ModulePermissionInterface, OnMemberJoin);
}
```

---

## 9. 踩坑记录

### 9.1 文件夹名含 `#` 导致 NapCat 初始化失败

- **现象：** `[NapCat] [Error] Cannot find module 'E:\project\C'`
- **原因：** `C#` 中的 `#` 被 URL 解析器当作 fragment
- **修复：** 用 `url.pathToFileURL()` 替代字符串拼接

### 9.2 NapCatWebSocket.Start() 异常被静默吞掉

- **现象：** 连接失败但无任何错误输出
- **原因：** `ContinueWith` 未观测异常，`MainLoop` 的 `IsConnected = true` 在连接成功前就设置
- **修复：** 在 `ContinueWith` callback 中 catch 异常并记录；`IsConnected` 在 `ConnectAsync` 成功后才置 true

### 9.3 DebugWSOutput 定义了但未启用

- **现象：** 看不到 WebSocket 原始数据
- **原因：** `RRBotCore` 创建 `NapCatWebSocket` 时未传入 `debugOutput` 参数
- **修复：** `new NapCatWebSocket(ip, port, new DebugWSOutput(logger))`

### 9.4 messagePostFormat 不兼容

- **现象：** `Error Analyzing Event: Unexpected character parsing value: [`
- **原因：** `messagePostFormat: "array"` 导致 `message` 字段是 JSON 数组，NapCatSharpLib 期望 string
- **修复：** 改为 `"string"`；注意修改**账号专属配置文件**而非仅修改 `onebot11.json`

### 9.5 账号配置文件优先级

- NapCatQQ 启动后自动创建 `onebot11_<机器人QQ>.json`
- 该文件优先级 > `onebot11.json`
- 修改配置时需要改**机器人 QQ 号对应的文件**

### 9.6 NapCatSharpLib 编译依赖

- ruri-bot 通过 DLL HintPath 引用 NapCatSharpLib（非 ProjectReference）
- 修改 NapCatSharpLib 源码后，必须**先单独编译** NapCatSharpLib.sln
- HintPath 指向 `bin\Debug\`（非 Release）

### 9.7 模块权限默认值

- 私聊默认 blacklist（允许所有人）
- 群聊默认 whitelist（禁止所有群）
- 群聊模块需要 `/rrbot enable <模块ID>` 启用

### 9.8 命令必须以 `/` 开头

- 非 `/` 开头的消息不会被 `CommandLexer` 解析为命令
- 命令格式：`/主命令 子命令 参数1 参数2 ...`
- 子命令回退：注册 `/cmd` 后，`/cmd sub arg` 会被路由到主处理器

---

## 10. 新增一个模块的步骤

```
1. 创建 .NET Standard 2.0 类库项目
2. 引用 RuriBot.Library 和 NapCatSharpLib
3. 创建模块类继承 RRBotModuleBase
4. 实现全部抽象方法
5. 编译：dotnet build
6. 将 DLL 放入 modules/ 目录
7. 重启 ruri-bot
8. 在群内 /rrbot enable <模块ID>
9. 测试功能
```

---

## 11. 调试技巧

1. **查看 WebSocket 原始数据：** `RRBotCore` 已启用 `DebugWSOutput`，所有 JSON 都在控制台
2. **查看命令解析结果：** 在 `ProcessGroupMessage` 中加断点查看 `cmd` 对象
3. **测试 API 连通性：** 启动后看 `GetLoginInfoAsync` 输出，确认 WS 双向通信正常
4. **模块加载验证：** 启动日志中的 "模块初始化完成" 会列出加载的模块 ID
