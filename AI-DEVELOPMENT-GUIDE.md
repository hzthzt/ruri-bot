# ruri-bot AI 开发指南

## 目录

1. [项目概述](#项目概述)
2. [整体架构](#整体架构)
3. [启动流程](#启动流程)
4. [事件系统](#事件系统)
5. [命令系统](#命令系统)
6. [模块系统](#模块系统)
7. [权限系统](#权限系统)
8. [数据持久化（IO 系统）](#数据持久化io-系统)
9. [ModuleManager 模块管理](#modulemanager-模块管理)
10. [编写一个模块（完整示例）](#编写一个模块完整示例)
11. [启动机器人（完整示例）](#启动机器人完整示例)
12. [核心数据流图](#核心数据流图)
13. [已知问题与注意事项](#已知问题与注意事项)

---

## 项目概述

**ruri-bot** 是一个基于 C#（.NET Standard 2.0）的模块化 QQ 机器人框架，通过 **NapCatSharpLib** 与 NapCatQQ/go-cqhttp 进行 WebSocket 通信。

核心特性：
- **模块化架构**：功能以独立 DLL 形式作为"模块"加载，支持热加载（需重启）
- **命令系统**：类似命令行的 `/命令 子命令 参数` 解析与分发
- **两级权限**：bot 级全局权限 + 模块级独立权限，支持黑白名单模式和 admin 管理
- **事件驱动**：基于 NapCatSharpLib 的 20+ 种 QQ 事件，带权限过滤的二次分发
- **JSON 持久化**：模块配置、权限数据以 JSON 文件存储

**解决方案结构：**

```
ruri-bot/
├── RuriBot.sln
├── RuriBot.Library/          # 共享库（模块开发依赖）
│   └── RuriBot.Library.csproj
│       - RRBotModuleEntry    # 模块基类（抽象类）
│       - RRBotModuleBase     # 无配置的模块基类
│       - RRBotModuleBase<T>  # 带类型化配置的模块基类
│       - 权限接口和数据结构
│       - IO、命令、事件注册接口
│       - RRBotCommand 数据模型
└── RuriBot/                  # 核心引擎（运行时）
    └── RuriBot.Core.csproj
        - RRBotCore           # 核心编排器（启动入口）
        - CommandLexer        # 命令解析器
        - CommandManager      # 命令注册与分发
        - EventManager        # 事件二次分发（带权限）
        - ModuleManager       # 模块加载与管理
        - PermissionManager   # 全局权限管理
        - CoreIO / ModuleIO   # 数据持久化
```

**依赖：**
- .NET Standard 2.0
- Newtonsoft.Json 13.0.1
- NapCatSharpLib.dll（本地 DLL 引用：`../../NapCatSharpLib/NapCatSharpLib/bin/Release/netstandard2.0/NapCatSharpLib.dll`）

---

## 整体架构

### 分层通信图

```
┌───────────────────────────────────────────────────────────────┐
│                         ruri-bot                               │
│                                                               │
│  ┌─────────────┐    ┌──────────────┐    ┌────────────────┐   │
│  │ Module A.dll │    │ Module B.dll │    │ Module C.dll   │   │
│  │ (业务功能)    │    │ (业务功能)    │    │ (业务功能)      │   │
│  └──────┬───────┘    └──────┬───────┘    └──────┬─────────┘   │
│         │                   │                    │             │
│  ┌──────┴───────────────────┴────────────────────┴────────┐   │
│  │              RuriBot.Library 接口层                      │   │
│  │  IRRBotCommandRegistry  IRRBotEventRegistry              │   │
│  │  IRRBotModuleIO         IRRBotModulePermissionOperation  │   │
│  └──────────────────────┬──────────────────────────────────┘   │
│                         │                                      │
│  ┌──────────────────────┴──────────────────────────────────┐   │
│  │            RuriBot 核心引擎                              │   │
│  │                                                         │   │
│  │  CommandManager       EventManager        ModuleManager │   │
│  │  (命令注册/分发)       (事件注册/分发)      (模块加载)     │   │
│  │         │                   │                  │         │   │
│  │  CommandLexer         PermissionManager    ModuleIO     │   │
│  │  (命令解析)            (全局权限)           (数据持久化)   │   │
│  └──────────────────────┬──────────────────────────────────┘   │
│                         │                                      │
│  ┌──────────────────────┴──────────────────────────────────┐   │
│  │              NapCatSharpLib 通信层                        │   │
│  │  NapCatWebSocket      NapCatAPI     CqMessageChain       │   │
│  └──────────────────────┬──────────────────────────────────┘   │
│                         │                                      │
└─────────────────────────┼──────────────────────────────────────┘
                          │ WebSocket
                   ┌──────▼──────┐
                   │  NapCatQQ   │
                   └─────────────┘
```

### 消息到命令的转换流程

```
QQ 消息（CQ 码字符串）
  │
  ▼
NapCatWebSocket (接收原始 JSON)
  │
  ▼
NapCatEventAnalyzer (解析 JSON → NapCatMessageGroup/NapCatMessagePrivate)
  │
  ▼
NapCatEventManager.OnEventMessageGroup (触发原生事件)
  │
  ▼
ruri-bot EventManager.React<NapCatMessageGroup>() (二次分发)
  │  ├─ 检查模块权限 (IsGroupPermission)
  │  └─ 调用核心的 ProcessGroupMessage
  │
  ▼
CommandLexer.MessageLexer(message) (解析命令字符串)
  │  ├─ 只有以 / 开头的消息被解析为命令
  │  └─ 返回 RRBotCommand (或 null)
  │
  ▼
CommandManager.ReactGroup(cmd, msg) (命令分发)
  │  ├─ 查找 registry[cmdType][subCmd]
  │  ├─ 检查模块级权限
  │  └─ 调用对应模块的回调
  │
  ▼
模块命令回调 (Action<RRBotCommand, NapCatMessageGroup>)
  │
  ▼
模块通过 api.SendGroupMessage() / api.SendPrivateMessage() 回复
```

---

## 启动流程

`RRBotCore` 的构造函数完整执行初始化。只需创建实例并调用 `Start()`：

```csharp
// 创建核心实例（内部完成所有初始化）
var core = new RRBotCore("127.0.0.1", 3001, logger);

// 连接到 NapCatQQ
core.Start();
```

### 初始化五个阶段

**构造函数内部分阶段执行：**

```
Phase 1: 创建通信层
    webSocket = new NapCatWebSocket(ip, port)
    api = new NapCatAPI(webSocket)
    apiAdvanced = new NapCatAPIAdvanced(webSocket)

Phase 2: Init() — 创建核心管理器
    CommandManager()
    EventManager(webSocket.EventManager)
    CommandLexer()
    CoreIO("data/")
    ModuleIO("data/modules/")

Phase 3: InitData() — 加载全局数据
    PermissionManager(coreIO, cmdManager)
      → 读取 data/permission/permission.json
      → 加载 superuser/admin/blacklist 列表

Phase 4: InitModule() — 加载模块
    ModuleManager(...)
      → 注册内置命令：/rrbot modules, /rrbot enable, /rrbot disable
      → LoadModules(): 扫描 modules/ 目录
        → 加载每个 DLL
        → 扫描 RRBotModuleEntry 子类
        → 实例化并调用 ModuleEntryInit()

Phase 5: EventRegistry() — 注册核心事件
    EventManager.Register<NapCatMessagePrivate>(dummyPerm, ProcessPrivateMessage)
    EventManager.Register<NapCatMessageGroup>(dummyPerm, ProcessGroupMessage)
```

### 目录结构（运行时）

```
程序运行目录/
├── modules/                         # 模块 DLL 存放目录
│   ├── ModuleA.dll
│   └── ModuleB.dll
├── dependencies/                    # 模块依赖库存放目录
│   └── SomeLib.dll
└── data/                           # 运行时数据
    ├── permission/
    │   └── permission.json          # 全局权限（superuser/admin/blacklist）
    └── modules/
        ├── ModuleA_Main/            # 模块A的数据目录（ModuleFullID）
        │   ├── config.json          # 模块配置（如有）
        │   └── permission.json      # 模块权限
        └── ModuleB_Main/
            ├── config.json
            └── permission.json
```

---

## 事件系统

### 架构：两层事件分发

**第一层：NapCatSharpLib 原生事件**
- `NapCatEventManager`：持有 20+ 个公开委托字段
- `NapCatEventAnalyzer`：解析 WebSocket JSON 并触发对应委托

**第二层：ruri-bot EventManager（带权限过滤）**
- 包装 `NapCatEventManager`，订阅所有事件
- 在每个事件触发时执行权限检查
- 向模块分发

### EventManager 实现细节

文件：`RuriBot/Manager/EventManager.cs`

**内部数据结构：**
```csharp
Dictionary<string, HashSet<(IRRBotModulePermissionOperation, Delegate)>> handlers
```
- Key：事件类型名称（如 `"NapCatMessageGroup"`）
- Value：`(权限对象, 回调委托)` 的去重集合

**支持的所有事件类型（20+ 种）：**

| 事件类型 | 分类 | 权限检查 ID |
|---------|------|-----------|
| `NapCatMessagePrivate` | 私聊 | user_id |
| `NapCatMessageGroup` | 群聊 | group_id |
| `NapCatNoticePrivateRecall` | 私聊 | user_id |
| `NapCatNoticePrivatePoke` | 私聊 | user_id |
| `NapCatRequestFriend` | 私聊 | user_id |
| `NapCatNoticeAddFriend` | 私聊 | user_id |
| `NapCatNoticeOfflineFileReceive` | 私聊 | user_id |
| `NapCatNoticeGroupFileUpload` | 群聊 | group_id |
| `NapCatNoticeGroupAdminChange` | 群聊 | group_id |
| `NapCatNoticeGroupDecrease` | 群聊 | group_id |
| `NapCatNoticeGroupIncrease` | 群聊 | group_id |
| `NapCatNoticeGroupBan` | 群聊 | group_id |
| `NapCatNoticeGroupRecall` | 群聊 | group_id |
| `NapCatNoticeGroupPoke` | 群聊 | group_id |
| `NapCatNoticeGroupLuckyKing` | 群聊 | group_id |
| `NapCatNoticeGroupHonor` | 群聊 | group_id |
| `NapCatNoticeGroupCardChange` | 群聊 | group_id |
| `NapCatNoticeGroupEssence` | 群聊 | group_id |
| `NapCatNoticeClientStatusChange` | 群聊 | group_id |
| `NapCatRequestGroup` | 群聊 | group_id |

**事件分类逻辑（`IsPrivateEvent<T>()`）：**
- 私聊事件：检查 `user_id` 是否在模块权限白名单/黑名单中
- 群聊事件：检查 `group_id` 是否在模块权限白名单/黑名单中

**注册与注销：**
```csharp
void Register<T>(IRRBotModulePermissionOperation perm, Action<T> callback)
void Unregister<T>(IRRBotModulePermissionOperation perm, Action<T> callback)
```

**分发流程（`React<T>`）：**
```
1. 确定事件类型是 private 或 group
2. 提取对应 ID（user_id 或 group_id）
3. 遍历 handlers[typeof(T).Name]
4. 对每个处理器：
   a. 权限检查：perm.IsPrivatePermission(id) 或 perm.IsGroupPermission(id)
   b. 通过则调用 Action<T> callback
```

---

## 命令系统

### 核心类关系

```
RRBotCommand          ←── 命令数据模型
CommandLexer          ←── 命令解析器（字符串 → RRBotCommand）
CommandManager        ←── 命令注册与分发
IRRBotCommandRegistry ←── 模块看到的注册接口
```

### RRBotCommand 数据模型

文件：`RuriBot.Library/Data/RRBotCommand.cs`

```csharp
public class RRBotCommand
{
    public string CommandType { get; }       // 命令类型（"/"后的第一个词），已小写
    public string CommandSubType { get; }    // 子命令（第二个词），已小写
    public ushort CommandArgsCount { get; }  // 参数个数
    public List<string> CommandArgs { get; } // 参数列表
}
```

### CommandLexer 解析规则

文件：`RuriBot/Lexer/CommandLexer.cs`

**解析算法：**
1. 从消息链中提取文本消息段（忽略图片、表情等非文本段）
2. 跳过前导空白和换行
3. 必须检测到 `/` 前缀，否则返回 `null`（非命令消息）
4. `/` 后第一个空格分隔的 token → `CommandType`（小写）
5. 第二个空格分隔的 token → `CommandSubType`（小写）
6. 剩余空格分隔的 token → `CommandArgs`（通过 `AddArgument` 逐项添加）
7. 换行符在参数中被静默去除
8. 连续空格被折叠

**解析示例：**

| 输入 | CommandType | CommandSubType | CommandArgs |
|------|------------|----------------|-------------|
| `/help` | help | "" | [] |
| `/rrbot modules` | rrbot | modules | [] |
| `/echo hello world` | echo | hello | ["world"] |
| `/ban 123456 600 刷屏` | ban | 123456 | ["600", "刷屏"] |
| `/music play 晴天 周杰伦` | music | play | ["晴天", "周杰伦"] |
| `你好` | — | — | (返回 null) |

**非 `/` 开头的消息不会被当作命令处理。**

### CommandManager 命令注册与分发

文件：`RuriBot/Manager/CommandManager.cs`

**内部数据结构（两层嵌套字典）：**

```csharp
// 私聊命令注册表
Dictionary<string, Dictionary<string, HashSet<(IRRBotModulePermissionOperation, callback)>>>
    m_privateCommandRegistry
    // cmdType  →  subCmd  →  {(permission, callback)}

// 群聊命令注册表（结构相同）
Dictionary<string, Dictionary<string, HashSet<(IRRBotModulePermissionOperation, callback)>>>
    m_groupCommandRegistry
```

**注册方法：**

```csharp
// 注册主命令（不指定子命令，subCmd 默认为 ""）
cmdReg.RegisterGroup("echo", permission, OnEchoCommand);

// 注册带子命令
cmdReg.RegisterGroup("music", "play", permission, OnMusicPlay);
cmdReg.RegisterGroup("music", "pause", permission, OnMusicPause);

// 对应的注销方法
cmdReg.UnregisterGroup("echo", permission, OnEchoCommand);
```

**子命令回退机制（重要设计）：**

当收到如 `/music play 晴天` 的命令时：
1. 先查找 `registry["music"]["play"]`
2. 若找到 → 直接调用
3. 若**找不到** → 查找 `registry["music"][""]`（主命令处理器）
4. 若主命令处理器存在 → 创建一个**新的** `RRBotCommand`：
   - `CommandSubType` 设为 `""`
   - 将原本的 `"play"` 插入为 `CommandArgs[0]`
   - 原参数顺延
5. 若主命令处理器也不存在 → 忽略

这意味着模块只需注册 `/music` 主命令，`/music play 晴天` 和 `/music pause` 都会被路由到主处理器，其中 `play`/`pause` 会作为第一个参数传入。

**分发时的权限检查：**
- `ReactPrivate`：检查 `permission.IsPrivatePermission(msg.user_id)`
- `ReactGroup`：检查 `permission.IsGroupPermission(msg.group_id)`

---

## 模块系统

### 模块基类继承层次

```
RRBotModuleEntry (抽象基类)
├── RRBotModuleBase (无配置变体)
│   └── 继承用于不需要配置文件的模块
└── RRBotModuleBase<TConfig, TConfigData> (泛型变体)
    └── 继承用于需要类型化配置文件的模块
```

### RRBotModuleEntry — 模块必须实现的抽象方法

文件：`RuriBot.Library/Module/RRBotModuleEntry.cs`

**必须实现的抽象方法（8 个）：**

```csharp
// 1. 设定模块标识
protected abstract void SetModuleInfo();
// 在此设置：
//   module_id       (必填，如 "music")
//   module_subid    (可选，如 "netease"，则 ModuleFullID = "music_netease")
//   module_name     (显示名称)
//   module_version  (版本号)
//   module_author   (作者)

// 2. 初始化配置和权限
protected abstract void ConfigurationInit();
// 初始化 Permission 和 Config（如有）

// 3. 注册私聊命令
protected abstract void RegisterPrivateCommands();

// 4. 注销私聊命令
protected abstract void UnregisterPrivateCommands();

// 5. 注册群聊命令
protected abstract void RegisterGroupCommands();

// 6. 注销群聊命令
protected abstract void UnregisterGroupCommands();

// 7. 注册事件监听
protected abstract void RegisterNapCatEvents();

// 8. 注销事件监听
protected abstract void UnregisterNapCatEvents();
```

**可选的虚方法：**

```csharp
protected virtual void ExtendedInit() { }
// 在 ConfigurationInit() 之后、命令注册之前调用
// 可用于额外的初始化逻辑
```

**模块可用的注入依赖（字段）：**

| 字段 | 类型 | 用途 |
|------|------|------|
| `cmdRegister` | `IRRBotCommandRegistry` | 注册/注销命令 |
| `evtRegister` | `IRRBotEventRegistry` | 注册/注销事件 |
| `api` | `NapCatAPI` | 发送消息、调用 QQ API |
| `fileIO` | `IRRBotModuleIO` | 模块数据持久化 |
| `coreLogger` | `IRRBotLogger` | 日志输出 |
| `moduleDataPath` | `string` | 模块数据目录（如 `data/modules/sample_Main/`） |
| `Permission` | `RRBotModulePermission` | 模块级权限管理 |
| `BotPermission` | `IRRBotPermission` | Bot 级全局权限检查 |

**模块生命周期：**

```
构造函数（空）
  ｜
ModuleEntryInit(...)           ← ModuleManager 调用
  ├── 存储所有依赖
  ├── SetModuleInfo()          ← 模块设置身份标识
  ├── ConfigurationInit()      ← 初始化权限和配置
  ├── ExtendedInit()           ← 可选扩展初始化
  ├── RegisterPrivateCommands()← 注册私聊命令
  ├── RegisterGroupCommands()  ← 注册群聊命令
  └── RegisterNapCatEvents()   ← 注册事件监听
      ...
      (模块运行中)
      ...
~RRBotModuleEntry() (析构)
  ├── UnregisterPrivateCommands()
  ├── UnregisterGroupCommands()
  └── UnregisterNapCatEvents()
```

### RRBotModuleBase（无配置版）

文件：`RuriBot.Library/Module/RRBotModuleBase.NoConfig.cs`

```csharp
public abstract class RRBotModuleBase : RRBotModuleEntry
{
    protected override void ConfigurationInit()
    {
        Permission = new RRBotModulePermission();
        Permission.Init(ModuleFullID, fileIO);
    }
}
```

只初始化权限对象，不加载配置文件。适用于简单模块。

### RRBotModuleBase<TConfig, TConfigData>（带配置版）

文件：`RuriBot.Library/Module/RRBotModuleBase.Generic.cs`

```csharp
public abstract class RRBotModuleBase<TConfig, TConfigData> : RRBotModuleEntry
    where TConfig : RRBotModuleConfigBase<TConfigData>
{
    public TConfig Config { get; protected set; }

    protected override void ConfigurationInit()
    {
        Permission = new RRBotModulePermission();
        Permission.Init(ModuleFullID, fileIO);

        Config = Activator.CreateInstance<TConfig>();
        Config.Init(ModuleFullID, fileIO);
    }
}
```

同时初始化权限和配置。当模块需要持久化配置时使用。

---

## 权限系统

### 两级权限架构

```
┌─────────────────────────────┐
│   Bot 级权限（全局）          │
│   IRRBotPermission          │
│   - IsDeveloper(id)         │  ← 检查 superuser 列表
│   - IsAdmin(id)             │  ← 检查 admin + superuser 列表
│   存储: data/permission/     │
│         permission.json     │
└─────────────┬───────────────┘
              │
┌─────────────▼───────────────┐
│   模块级权限（每个模块独立）    │
│   IRRBotModulePermissionOperation │
│   - IsPrivatePermission(id) │  ← 检查用户是否被允许
│   - IsGroupPermission(id)   │  ← 检查群是否被允许
│   - IsAdmin(id)             │  ← 检查模块管理员
│   - SetPrivatePermission(...)│
│   - SetGroupPermission(...) │
│   存储: data/modules/        │
│         {moduleId}/          │
│         permission.json     │
└─────────────────────────────┘
```

### Bot 级权限（全局）

文件：`RuriBot/Permission/PermissionManager.cs`
接口：`RuriBot.Library/Permission/IRRBotPermission.cs`

```csharp
public interface IRRBotPermission
{
    bool IsDeveloper(long id);  // 检查是否在 superuser 列表中
    bool IsAdmin(long id);      // 检查是否在 admin 或 superuser 列表中
}
```

数据文件：`data/permission/permission.json`

```json
{
    "superuser": [123456789],
    "admin": [987654321, 111222333],
    "blacklist": []
}
```

**用途：**
- `ModuleManager` 的内置命令（`/rrbot modules`、`/rrbot enable`、`/rrbot disable`）只允许 developer 执行
- 模块可通过 `BotPermission.IsAdmin(id)` 做自己的 admin 级检查

### 模块级权限

接口：`RuriBot.Library/Module/Interface/IRRBotModulePermissionOperation.cs`

```csharp
public interface IRRBotModulePermissionOperation
{
    // 设置权限模式
    void SetPrivatePermissionType(RRBotModulePermissionType type, bool clearPermission = true);
    void SetGroupPermissionType(RRBotModulePermissionType type, bool clearPermission = true);

    // 添加/移除权限对象
    void SetPrivatePermission(RRBotModulePermissionOperationType operation, long obj);
    void SetPrivatePermission(RRBotModulePermissionOperationType operation, List<long> objList);
    void SetGroupPermission(RRBotModulePermissionOperationType operation, long obj);
    void SetGroupPermission(RRBotModulePermissionOperationType operation, List<long> objList);

    // 管理员管理
    void SetAdmin(RRBotModulePermissionOperationType operation, long obj);
    void SetAdmin(RRBotModulePermissionOperationType operation, List<long> objList);

    // 权限检查
    bool IsPrivatePermission(long id);  // 用户是否有权限
    bool IsGroupPermission(long id);    // 群是否有权限
    bool IsAdmin(long id);              // 是否是模块管理员
}
```

**权限模式（RRBotModulePermissionType）：**
- `blacklist`：黑名单模式 — 名单中的人被**禁止**，其余人允许
- `whitelist`：白名单模式 — 只有名单中的人**允许**，其余人禁止

**默认值：**
- 私聊权限默认 **blacklist（允许所有人）**
- 群聊权限默认 **whitelist（禁止所有群）**

**操作类型（RRBotModulePermissionOperationType）：**
- `add`：添加到权限列表
- `remove`：从权限列表移除

**权限检查逻辑：**

```
IsPrivatePermission(id):
├── id 在 private_permission 列表中？
│   ├── 模式 = blacklist → 拒绝（return false）
│   └── 模式 = whitelist  → 允许（return true）
└── id 不在列表中？
    ├── 模式 = blacklist → 允许（return true）[默认：允许]
    └── 模式 = whitelist  → 拒绝（return false）[默认：拒绝]

IsGroupPermission(id): 同理，使用 group_permission 列表和 group_type
```

**数据文件：**`data/modules/{ModuleFullID}/permission.json`

```json
{
    "private_type": "blacklist",
    "group_type": "whitelist",
    "private_permission": [111222333],
    "group_permission": [444555666, 777888999],
    "admin": [123456789],
    "blacklist_user": []
}
```

注意：`blacklist_user` 字段定义了但未在代码中使用。

### 权限检查在分发中的位置

**命令分发时：**
- `CommandManager.ReactGroup` → 调用 `permission.IsGroupPermission(msg.group_id)`

**事件分发时：**
- `EventManager.React<NapCatMessageGroup>` → 调用 `permission.IsGroupPermission(data.group_id)`
- `EventManager.React<NapCatMessagePrivate>` → 调用 `permission.IsPrivatePermission(data.user_id)`

每个模块注册时附带自己的 `IRRBotModulePermissionOperation` 对象，同一事件类型的不同模块可以有不同的权限策略。

---

## 数据持久化（IO 系统）

### 两层 IO 架构

```
IRRBotCoreIO (核心数据 IO)
├── 实现: CoreIO
├── 基路径: data/
├── 使用者: PermissionManager
└── 使用基类: BotCoreDataBase<T>

IRRBotModuleIO (模块数据 IO)
├── 实现: ModuleIO
├── 基路径: data/modules/
├── 使用者: 模块内部的权限和配置基类
└── 使用基类:
    ├── RRBotModulePermissionBase<T>  (permission.json)
    ├── RRBotModuleConfigBase<T>      (config.json)
    └── RRBotModuleDataBase<T>        (自定义数据文件)
```

### CoreIO

文件：`RuriBot/IO/CoreIO.cs`

```csharp
public class CoreIO : IRRBotCoreIO
{
    T ReadJson<T>(string path, string fileName);
    bool SaveJson(string path, string fileName, object serializableObject);

    // 实际文件路径: {basePath}/{path}/{fileName}.json
    // 或 {basePath}/{fileName}.json (path 为空时)
}
```

### ModuleIO

文件：`RuriBot/IO/ModuleIO.cs`

```csharp
public class ModuleIO : IRRBotModuleIO
{
    T ReadJson<T>(string moduleId, string path, string fileName);
    bool SaveJson(string moduleId, string path, string fileName, object serializableObject);

    // 实际文件路径: data/modules/{moduleId}/{path}/{fileName}.json
    // 自动创建目录
}
```

### 持久化基类的设计模式

所有持久化基类遵循相同模式：

```csharp
public abstract class SomeBase<T>
{
    public T Data { get; set; }  // setter 自动调用 SaveData()

    // 构造函数逻辑：
    // 1. 尝试 ReadData() 从文件加载
    // 2. 如果文件不存在：
    //    a. SetDefaultValue() → 创建 T 默认实例
    //    b. SaveData() → 保存到文件
}
```

**可用的三个基类：**

| 基类 | 文件 | 默认文件名 | 用途 |
|------|------|-----------|------|
| `RRBotModuleConfigBase<T>` | `Module/Config/` | `config.json` | 模块配置 |
| `RRBotModulePermissionBase<T>` | `Module/Permission/` | `permission.json` | 模块权限 |
| `RRBotModuleDataBase<T>` | `Module/Data/` | 自定义 | 模块自定义数据 |

**在模块中使用自定义数据：**

```csharp
// 1. 定义数据结构
public class MyDataStruct
{
    public int counter;
    public string lastMessage;
}

// 2. 创建数据访问类
public class MyModuleData : RRBotModuleDataBase<MyDataStruct>
{
    public MyModuleData(string moduleId, IRRBotModuleIO io)
        : base(moduleId, io, "storage", "mydata")
    {
    }

    protected override void SetDefaultValue()
    {
        data = new MyDataStruct { counter = 0, lastMessage = "" };
    }
}

// 3. 在模块中使用
var myData = new MyModuleData(ModuleFullID, fileIO);
myData.Data.counter++;
myData.Data.lastMessage = msg.raw_message;
myData.Data = myData.Data;  // 触发保存
```

---

## ModuleManager 模块管理

文件：`RuriBot/Manager/ModuleManager.cs`

### 模块加载流程

```
LoadModules()
  │
  ├── 创建 modules/ 目录（如不存在）
  │
  ├── 注册 AssemblyResolve 事件处理器
  │   └── 当 DLL 依赖无法解析时，从 dependencies/ 目录搜索
  │
  ├── 遍历 modules/ 目录中的所有文件
  │   │
  │   ├── Assembly.LoadFrom(dll)         ← 加载 DLL
  │   │
  │   ├── 扫描所有类型，查找 RRBotModuleEntry 子类
  │   │
  │   └── 对每个找到的类型：
  │       ├── Activator.CreateInstance(type)  ← 实例化
  │       ├── ModuleEntryInit(...)            ← 初始化
  │       └── modules[ModuleFullID] = instance ← 注册到字典
  │
  └── 容错：单个 DLL 加载失败不中断其他模块的加载
```

**模块标识冲突处理：** 如果 `ModuleFullID` 已存在，跳过并记录警告。

### 内置命令

三个 developer-only 命令（通过 `BotPermission.IsDeveloper` 控制）：

**`/rrbot modules`**
- 列出所有已加载模块的 ID 列表
- 回复到当前群聊

**`/rrbot enable <moduleId> [groupId]`**
- 启用模块的群权限
- 未指定 groupId 时默认使用发送消息的群
- 内部逻辑：
  - 群权限模式 = blacklist → 从黑名单**移除**该群
  - 群权限模式 = whitelist → 添加到白名单

**`/rrbot disable <moduleId> [groupId]`**
- 禁用模块的群权限
- 内部逻辑：
  - 群权限模式 = whitelist → 从白名单**移除**该群
  - 群权限模式 = blacklist → 添加到黑名单

---

## 编写一个模块（完整示例）

### 示例：Echo 模块（无配置）

```csharp
using NapCatSharpLib.API;
using NapCatSharpLib.Data;
using NapCatSharpLib.Message;
using RuriBot.Library.Data;
using RuriBot.Library.Module;

namespace MyBot.Modules
{
    public class EchoModule : RRBotModuleBase
    {
        // ===== 1. 设置模块身份 =====
        protected override void SetModuleInfo()
        {
            module_id = "echo";
            module_name = "Echo 模块";
            module_version = "1.0.0";
            module_author = "MyName";
        }

        // ===== 2. 注册私聊命令 =====
        protected override void RegisterPrivateCommands()
        {
            cmdRegister.RegisterPrivate(
                "echo",
                Permission.ModulePermissionInterface,
                OnEchoCommand
            );
        }

        // ===== 3. 注销私聊命令 =====
        protected override void UnregisterPrivateCommands()
        {
            cmdRegister.UnregisterPrivate(
                "echo",
                Permission.ModulePermissionInterface,
                OnEchoCommand
            );
        }

        // ===== 4. 注册群聊命令 =====
        protected override void RegisterGroupCommands()
        {
            cmdRegister.RegisterGroup(
                "echo",
                Permission.ModulePermissionInterface,
                OnEchoGroupCommand
            );
        }

        // ===== 5. 注销群聊命令 =====
        protected override void UnregisterGroupCommands()
        {
            cmdRegister.UnregisterGroup(
                "echo",
                Permission.ModulePermissionInterface,
                OnEchoGroupCommand
            );
        }

        // ===== 6. 注册事件 =====
        protected override void RegisterNapCatEvents()
        {
            // 本模块不监听额外事件
        }

        // ===== 7. 注销事件 =====
        protected override void UnregisterNapCatEvents()
        {
            // 本模块不监听额外事件
        }

        // ===== 8. 命令处理器 =====
        private async void OnEchoCommand(RRBotCommand cmd, NapCatMessagePrivate msg)
        {
            // 构建回复消息
            var chain = new CqMessageChain();
            chain.Builder.AddReply(msg.message_id);
            chain.Builder.AddText("你说了: ");

            // 拼接所有参数
            if (cmd.CommandArgsCount > 0)
            {
                chain.Builder.AddText(string.Join(" ", cmd.CommandArgs));
            }
            else
            {
                chain.Builder.AddText("(空)");
            }

            await api.SendPrivateMessage(msg.user_id, chain);
        }

        private async void OnEchoGroupCommand(RRBotCommand cmd, NapCatMessageGroup msg)
        {
            var chain = new CqMessageChain();
            chain.Builder.AddReply(msg.message_id);
            chain.Builder.AddText($"[群聊 Echo] {msg.sender.nickname} 说了: ");

            if (cmd.CommandArgsCount > 0)
            {
                chain.Builder.AddText(string.Join(" ", cmd.CommandArgs));
            }
            else
            {
                chain.Builder.AddText("(空)");
            }

            await api.SendGroupMessage(msg.group_id, chain);
        }
    }
}
```

### 示例：带配置的 Music 模块

```csharp
using NapCatSharpLib.API;
using NapCatSharpLib.Data;
using NapCatSharpLib.Message;
using RuriBot.Library.Data;
using RuriBot.Library.Module;
using RuriBot.Library.Module.Config;

namespace MyBot.Modules
{
    // ===== 1. 定义配置数据结构 =====
    public class MusicConfigData
    {
        public string defaultPlatform = "qq";
        public bool autoPlay = false;
    }

    // ===== 2. 定义配置类 =====
    public class MusicModuleConfig : RRBotModuleConfigBase<MusicConfigData>
    {
        public MusicModuleConfig(string moduleId, IRRBotModuleIO io)
            : base(moduleId, io)
        {
        }

        protected override void SetDefaultValue()
        {
            data = new MusicConfigData
            {
                defaultPlatform = "qq",
                autoPlay = false
            };
        }
    }

    // ===== 3. 模块主类 =====
    public class MusicModule : RRBotModuleBase<MusicModuleConfig, MusicConfigData>
    {
        protected override void SetModuleInfo()
        {
            module_id = "music";
            module_name = "音乐模块";
            module_version = "1.0.0";
            module_author = "MyName";
        }

        // ===== 使用子命令注册 =====
        protected override void RegisterGroupCommands()
        {
            var perm = Permission.ModulePermissionInterface;

            // 注册主命令（处理 /music 后面所有子命令）
            cmdRegister.RegisterGroup("music", perm, OnMusicCommand);

            // 也可以精确注册特定子命令
            cmdRegister.RegisterGroup("music", "play", perm, OnMusicPlay);
            cmdRegister.RegisterGroup("music", "stop", perm, OnMusicStop);
        }

        protected override void UnregisterGroupCommands()
        {
            var perm = Permission.ModulePermissionInterface;
            cmdRegister.UnregisterGroup("music", perm, OnMusicCommand);
            cmdRegister.UnregisterGroup("music", "play", perm, OnMusicPlay);
            cmdRegister.UnregisterGroup("music", "stop", perm, OnMusicStop);
        }

        protected override void RegisterPrivateCommands() { }
        protected override void UnregisterPrivateCommands() { }
        protected override void RegisterNapCatEvents() { }
        protected override void UnregisterNapCatEvents() { }

        // ===== 命令处理器 =====
        private async void OnMusicCommand(RRBotCommand cmd, NapCatMessageGroup msg)
        {
            var chain = new CqMessageChain();
            chain.Builder.AddText("Music 模块支持的命令:\n");
            chain.Builder.AddText("/music play <歌名>\n");
            chain.Builder.AddText("/music stop\n");
            chain.Builder.AddText("默认平台: " + Config.Data.defaultPlatform);
            await api.SendGroupMessage(msg.group_id, chain);
        }

        private async void OnMusicPlay(RRBotCommand cmd, NapCatMessageGroup msg)
        {
            var songName = string.Join(" ", cmd.CommandArgs);
            var platform = Config.Data.defaultPlatform;

            var chain = new CqMessageChain();
            chain.Builder.AddReply(msg.message_id);
            chain.Builder.AddText($"正在 {platform} 平台搜索: {songName}...");
            await api.SendGroupMessage(msg.group_id, chain);

            // TODO: 实际搜索和播放逻辑
        }

        private async void OnMusicStop(RRBotCommand cmd, NapCatMessageGroup msg)
        {
            var chain = new CqMessageChain();
            chain.Builder.AddText("音乐已停止");
            await api.SendGroupMessage(msg.group_id, chain);
        }
    }
}
```

### 示例：监听群成员加入事件

```csharp
using NapCatSharpLib.Data;
using NapCatSharpLib.Message;
using RuriBot.Library.Module;

namespace MyBot.Modules
{
    public class WelcomeModule : RRBotModuleBase
    {
        protected override void SetModuleInfo()
        {
            module_id = "welcome";
            module_name = "欢迎模块";
            module_version = "1.0.0";
            module_author = "MyName";
        }

        protected override void ConfigurationInit()
        {
            // 默认群聊白名单模式 —— 只有手动 /rrbot enable 后的群才生效
            base.ConfigurationInit();
        }

        // 注册群成员增加事件
        protected override void RegisterNapCatEvents()
        {
            evtRegister.Register<NapCatNoticeGroupIncrease>(
                Permission.ModulePermissionInterface,
                OnGroupMemberJoin
            );
        }

        protected override void UnregisterNapCatEvents()
        {
            evtRegister.Unregister<NapCatNoticeGroupIncrease>(
                Permission.ModulePermissionInterface,
                OnGroupMemberJoin
            );
        }

        protected override void RegisterGroupCommands() { }
        protected override void UnregisterGroupCommands() { }
        protected override void RegisterPrivateCommands() { }
        protected override void UnregisterPrivateCommands() { }

        private async void OnGroupMemberJoin(NapCatNoticeGroupIncrease notice)
        {
            var chain = new CqMessageChain();
            chain.Builder.AddText($"欢迎新成员 {notice.user_id} 加入本群！");
            await api.SendGroupMessage(notice.group_id, chain);
        }
    }
}
```

---

## 启动机器人（完整示例）

```csharp
using NapCatSharpLib.Message;
using RuriBot.Core.Core;
using RuriBot.Library.Log;

namespace MyBot
{
    class Program
    {
        static void Main(string[] args)
        {
            // 1. 创建日志
            var logger = new ConsoleLogger();

            // 2. 创建并初始化核心（构造函数内自动完成所有初始化）
            var core = new RRBotCore("127.0.0.1", 3001, logger);

            // 3. 连接到 NapCatQQ
            core.Start();

            logger.Log("ruri-bot 已启动！");

            // 4. 保持运行（按任意键退出）
            Console.WriteLine("按任意键退出...");
            Console.ReadKey();
        }
    }

    // 简单的控制台日志实现
    class ConsoleLogger : IRRBotLogger
    {
        public void Log(string message)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        }

        public void Log(string message, CqMessageChain chain)
        {
            Log($"{message} | CQ: {chain.ToCqQuery()}");
        }
    }
}
```

---

## 核心数据流图

### 消息处理完整流程

```
                      QQ 消息到达
                          │
                          ▼
              ┌─────────────────────┐
              │   NapCatWebSocket    │
              │   ReceiveAsync 循环   │
              └──────────┬──────────┘
                         │ JSON 字符串
                         ▼
              ┌─────────────────────┐
              │ NapCatEventAnalyzer  │
              │ AnalyzeEvent(data)   │
              │ ┌─────────────────┐  │
              │ │ JObject.Parse   │  │
              │ │ 读取 post_type   │  │
              │ │ → message/notice │  │
              │ │   /request/meta │  │
              │ └────────┬────────┘  │
              └──────────┼──────────┘
                         │
              ┌──────────▼──────────┐
              │ NapCatEventManager   │
              │ (20+ 委托字段)        │
              │ OnEventMessageGroup  │
              │ OnEventMessagePrivate│
              │ OnEventNoticeXxx ... │
              └──────────┬──────────┘
                         │ NapCatMessageGroup 对象
                         ▼
              ┌─────────────────────┐
              │ ruri-bot EventManager│
              │ React<T>(data)       │
              │ ┌─────────────────┐  │
              │ │ 确定事件类型     │  │
              │ │ (private/group) │  │
              │ │ 提取检查 ID     │  │
              │ └────────┬────────┘  │
              └──────────┼──────────┘
                         │
              ┌──────────▼──────────┐
              │  遍历 handlers        │
              │  对每个回调:          │
              │  ┌────────────────┐  │
              │  │ perm.IsGroup   │  │ ← 权限检查
              │  │ Permission(id) │  │
              │  └───────┬────────┘  │
              │     允许  │  拒绝     │
              │      ▼   │  ✗ skip   │
              │  callback(data)      │
              └──────────┬──────────┘
                         │
              ┌──────────▼──────────┐
              │ ProcessGroupMessage  │ (核心处理器)
              │                      │
              │ commandLexer         │
              │   .MessageLexer(     │
              │     msg.message)     │
              └──────────┬──────────┘
                         │
              ┌──────────▼──────────┐
              │  CommandLexer 解析    │
              │  ┌────────────────┐  │
              │  │ /help → cmd    │  │
              │  │ 你好吗 → null  │  │
              │  └───────┬────────┘  │
              └──────────┼──────────┘
                         │ 非命令? → 忽略
                         ▼
              ┌─────────────────────┐
              │  CommandManager      │
              │  ReactGroup(cmd,msg) │
              │                      │
              │  查找 registry       │
              │  [cmdType][subCmd]   │
              │  ┌────────────────┐  │
              │  │ 找到 → 直接调用│  │
              │  │ 未找到 → 回退  │  │
              │  │ 到 subCmd=""   │  │
              │  └───────┬────────┘  │
              │          ▼           │
              │  perm.IsGroupPerm    │ ← 权限检查
              │  允许 → callback()   │
              └─────────────────────┘
                         │
                         ▼
              模块命令回调被执行
              (async void 模式)
                         │
                         ▼
              ┌─────────────────────┐
              │ api.SendGroupMessage │
              │ api.SendPrivateMsg  │
              └─────────────────────┘
```

### 模块加载流程

```
RRBotCore 构造函数
  │
  ▼
InitModule()
  │
  ▼
ModuleManager 构造函数
  │
  ├── 注册内置命令 (/rrbot modules, enable, disable)
  │
  ▼
LoadModules()
  │
  ├── 创建 modules/ 目录
  │
  ├── 注册 AssemblyResolve (dependencies/ 目录解析)
  │
  ├── 遍历 modules/*.dll
  │   │
  │   ├── Assembly.LoadFrom(dll)
  │   │
  │   ├── 扫描类型 → 找 RRBotModuleEntry 子类
  │   │
  │   └── 对每个模块类：
  │       ├── new ModuleClass()
  │       └── ModuleEntryInit(
  │               cmdRegister,    ← CommandManager
  │               evtRegister,    ← EventManager
  │               api,            ← NapCatAPI
  │               moduleIO,       ← ModuleIO
  │               botPermission,  ← PermissionManager
  │               logger,
  │               moduleDataPath
  │             )
  │             │
  │             ├── 模块.SetModuleInfo()
  │             ├── 模块.ConfigurationInit()
  │             │   ├── 权限加载 (permission.json)
  │             │   └── 配置加载 (config.json) [如有]
  │             ├── 模块.ExtendedInit()
  │             ├── 模块.RegisterPrivateCommands()
  │             ├── 模块.RegisterGroupCommands()
  │             └── 模块.RegisterNapCatEvents()
  │
  └── 完成：所有模块已就绪
```

---

## 已知问题与注意事项

### 1. async void 命令处理器

模块命令处理器使用 `async void` 签名，而非 `async Task`。这意味着：
- **未捕获的异常会导致进程崩溃**
- 调用方（CommandManager/EventManager）无法等待异步操作完成
- 多个命令可能并发执行，无顺序保证

**建议：** 在命令处理器中使用 try-catch 包裹所有逻辑，防止异常导致崩溃；对于需要顺序保证的场景，自行实现排队机制。

### 2. 非线程安全的数据结构

CommandManager、EventManager 内部使用的 `Dictionary` 和 `HashSet` 未使用锁或并发集合。在当前架构下（事件和命令由 WebSocket 接收线程串行处理）这通常不是问题，但如果将来引入多线程调度，将导致竞态条件。

### 3. 模块 DLL 依赖解析

`AssemblyResolve` 事件处理器只在 `dependencies/` 目录搜索，不会回退到 GAC 或 NuGet 缓存。如果模块依赖的第三方库不在 `dependencies/` 中，加载会失败。

### 4. 模块注册不可撤销（部分）

模块析构函数中会调用 Unregister 方法，但已注册到 `CommandManager` 和 `EventManager` 的回调在模块卸载前不会被移除。如果模块在运行时被卸载（通过 `ModuleManager` 移除），回调可能成为僵尸引用。

### 5. blacklist_user 字段未使用

`RRBotModulePermissionStruct` 中定义了 `blacklist_user` 字段，但在 `RRBotModulePermissionBase` 的权限检查逻辑中从未被读取。如果需要用户级黑名单功能，需要在权限检查代码中添加对应逻辑。

### 6. 子命令回退可能造成混淆

CommandManager 的子命令回退机制（找不到精确子命令时回退到主命令，将子命令作为第一个参数）是一个隐式行为。如果模块同时注册了 `/cmd` 和 `/cmd sub`，`/cmd sub arg` 会精确匹配到 `/cmd sub`；但如果用户发送 `/cmd undefined`，它会被路由到 `/cmd` 主处理器，而不是报错。

### 7. 模块加载顺序不确定

`Directory.GetFiles("modules/")` 返回的文件顺序取决于操作系统，模块的加载顺序不可预测。如果有模块间的初始化依赖，需要自行处理。

### 8. 配置/权限文件写入时机

持久化基类在每次 `Data` 属性的 setter 中被调用时都会写入文件（通过 `SaveJson`）。高频更新场景下应注意 IO 性能。

---

## 附录：开发一个新的模块 checklist

1. 创建新的 .NET Standard 2.0 类库项目
2. 添加 `RuriBot.Library` 和 `NapCatSharpLib` 的 DLL 引用
3. 创建模块类，继承 `RRBotModuleBase` 或 `RRBotModuleBase<TConfig, TConfigData>`
4. 实现 `SetModuleInfo()` — 设置 `module_id`（必须唯一）
5. 实现 `ConfigurationInit()` — 使用默认实现或自定义
6. 实现 `RegisterGroupCommands()` 和 `RegisterPrivateCommands()` — 注册命令
7. 实现对应的 Unregister 方法 — 注销命令
8. 实现 `RegisterNapCatEvents()` 和 Unregister — 如需要监听事件
9. 编译为 DLL，放入 `modules/` 目录
10. 如有第三方依赖，放入 `dependencies/` 目录
11. 重启 ruri-bot，使用 `/rrbot modules` 验证加载
12. 使用 `/rrbot enable <moduleId>` 在目标群启用模块
