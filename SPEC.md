# BetterGI 守护程序需求规格文档

## 1. 项目概述

**项目名称**: BGIguard (BetterGI 守护程序)

**项目类型**: Windows 桌面工具 / 游戏辅助启动器

**核心功能**: 实现 BetterGI 启动器的"无痛"守护，提供崩溃自启、掉线重连、单实例保护、游戏进程联动等守护功能。

**目标用户**: 使用 BetterGI 启动器的《原神》玩家，需要稳定挂机辅助的玩家群体。

---

## 2. 功能需求

### 2.1 核心功能列表

| 功能 | 描述 |
|------|------|
| 崩溃自启 | 监控 BetterGI.exe 进程，异常退出时自动重启 |
| 掉线重连 | 检测游戏进程状态，断线后自动重连 |
| 单实例保护 | 防止多开，确保只有一个守护实例运行 |
| 游戏进程联动 | 监控原神客户端进程状态 |
| 命令行设置 | 支持命令行参数配置各项参数 |
| 路径引号支持 | 支持带引号的路径输入，自动去除首尾引号 |
| 跳过设置 | 可配置为跳过设置界面直接启动守护 |
| 日志记录 | 实时写入日志文件，按日自动轮转，UTF8 编码 |
| 内存检测 | 监控系统整体内存（物理+虚拟），超阈值自动重启 |

### 2.2 启动命令获取机制

**获取方式**: 使用 NtQueryInformationProcess + ReadProcessMemory 读取目标进程 PEB 获取启动命令

**目标进程**:
- `BetterGI.exe`（用户启动的入口程序，守护目标）

**获取逻辑**:
1. 程序启动时通过 NtQueryInformationProcess 查询 `BetterGI.exe` 进程
2. 获取其 PEB 中的 ProcessParameters 指针
3. 读取 ProcessParameters 中的 CommandLine UNICODE_STRING
4. 解析并提取命令行参数

**优势**:
- 无需替换原始 BetterGI.exe 文件
- 保持原文件完整性
- 用户可正常更新 BetterGI
- 纯本地 API，性能开销极小

### 2.3 进程监控与守护

**监控间隔**: 默认 5 秒 (可调整 1-999 秒)

**监控逻辑**: 每次循环先等待监控间隔，再执行检测

1. **进程丢失计数**:
   - 记录 BetterGI.exe 进程丢失的连续次数
   - 连续丢失达到阈值（默认 2 次）才触发重启
   - 进程恢复后重置计数

2. **游戏进程检测**:
   - 检测 `YuanShen.exe` 是否存在
   - 检测 `GenshinImpact.exe` 是否存在
   - 任一存在则视为游戏正在运行

3. **BetterGI.exe 未运行处理**:
   - 实时获取当前 BetterGI.exe 进程的启动命令行（通过 PEB 读取）
   - 连续检测丢失次数达到阈值时使用获取到的启动命令重启
   - 未达到阈值时只记录警告

4. **游戏退出处理**:
   - 当 `YuanShen.exe` 和 `GenshinImpact.exe` 均不存在时
   - 通过路径匹配终止 `BetterGI.exe` 进程（防止误终止同名进程）
   - 立即自动重启 `BetterGI.exe`

5. **BetterGI.exe 独立运行**:
   - 启动 BetterGI.exe 时使用 `cmd /c start` 使其完全独立于守护进程
   - 即使守护进程退出，BetterGI.exe 也会继续运行

### 2.4 防多开功能

**检测机制**:
1. 使用命名互斥体 (Mutex) 确保单实例
2. 程序启动时检查同名进程是否已存在

**处理逻辑**:
1. 若检测到已存在守护进程，终止旧进程并重新创建互斥体
2. 若不存在，创建互斥体，正常启动守护

### 2.5 内存检测功能

**检测方式**: 使用 GlobalMemoryStatusEx API 获取系统内存（物理+虚拟）

**默认阈值**:
| 类型 | 默认值 | 描述 |
|------|--------|------|
| 内存阈值 | 95% | 系统总内存（物理+虚拟）占用百分比 |

**配置范围**: 1-100%

**处理逻辑**:
1. 在监控循环中调用 GlobalMemoryStatusEx API 获取内存信息
2. 计算系统总内存 = 物理内存 + 虚拟内存
3. 若内存超出阈值，通过路径匹配终止并重启 BetterGI.exe
4. 记录警告日志和重启日志

**内存警告**:
- 内存警告阈值 = 配置值 - 5%
- 例如配置值为 95% 时，内存达到 90% 会发出警告
- 警告不影响自动重启逻辑，仅作为提醒

### 2.6 日志系统

**日志文件**:
- 命名格式: `BGI_guard{yyyyMMdd}.log`
- 存放位置: 与可执行文件同目录
- **按日追加**，每天生成新的日志文件
- **UTF8 编码**

**日志内容**:
- 时间戳 (精确到毫秒)
- 版本号: `[BGIguard_v{x.x}]`
- 日志级别: `[INFO]` / `[WARN]` / `[ERROR]`
- 日志消息

**自动清理**:
- 日志文件数量超过 7 个时自动删除旧日志

**日志示例**:
```
[2026-04-15 14:30:00.123] [BGIguard_v1.0] [INFO] BGIguard 启动成功
[2026-04-15 14:30:00.456] [BGIguard_v1.0] [INFO] BetterGI路径: D:\Games\BetterGI\BetterGI.exe
[2026-04-15 14:30:05.789] [BGIguard_v1.0] [INFO] 检测 14:30:05 | 内存: 45% | BetterGI: 运行 | 游戏: YuanShen
[2026-04-15 14:35:00.001] [BGIguard_v1.0] [WARN] BetterGI.exe 丢失 (第 1 次)
[2026-04-15 14:35:10.123] [BGIguard_v1.0] [INFO] 连续丢失达到阈值，正在重启...
```

---

## 3. 技术规格

### 3.1 开发环境

- **语言**: C# (.NET 8.0)
- **目标框架**: net8.0-windows
- **发布模式**: 自包含单文件发布 (win-x64)

### 3.2 项目结构

```
BGIguard/
├── BGIguard.csproj
├── Program.cs
└── Assets/icon.ico (可选)
```

### 3.3 依赖项

- .NET 8.0 内置库
- P/Invoke API:
  - NtQueryInformationProcess (ntdll.dll)
  - ReadProcessMemory (kernel32.dll)
  - GlobalMemoryStatusEx (kernel32.dll)
  - OpenProcess / CloseHandle (kernel32.dll)

### 3.4 发布命令

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
```

---

## 4. 行为流程图

```
┌─────────────────┐
│  程序启动       │
└────────┬────────┘
         │
         ▼
┌─────────────────────────────────────────────────┐
│ 1. 加载配置 (JSON 格式)                         │
│ 2. 检测 BetterGI.exe 路径                       │
│ 3. 处理命令行参数                               │
│ 4. 创建互斥体 (单实例保护)                      │
│ 5. 启动 BetterGI.exe                            │
│ 6. 进入监控循环:                                │
│    - 等待监控间隔                               │
│    - 获取 BetterGI 进程信息                     │
│    - 检查游戏进程                               │
│    - 检查系统内存                               │
│    - 判断是否需要重启                          │
└─────────────────────────────────────────────────┘
```

---

## 5. 配置说明

**配置文件**: `BGIguard_config.json` (自动生成，UTF8 编码)

**配置结构**:
```json
{
  "BetterGiPath": "D:\\Games\\BetterGI\\BetterGI.exe",
  "MemoryPercent": 95,
  "MonitorInterval": 5,
  "MissingCount": 2,
  "SkipSetup": false
}
```

**可配置项**:
| 配置项 | 默认值 | 范围 | 描述 |
|--------|--------|------|------|
| BetterGiPath | 自动检测 | 有效路径 | BetterGI.exe 完整路径 |
| MemoryPercent | 95% | 1-100% | 系统整体内存占用百分比 |
| MonitorInterval | 5秒 | 1-999 | 守护循环检测间隔 |
| MissingCount | 2次 | 1-10 | 连续检测丢失进程次数才触发重启 |
| SkipSetup | false | true/false | 每次启动是否跳过设置界面 |

**启动检测逻辑**:
1. 程序启动时先检测自身目录下是否存在 `BetterGI.exe`
2. 若存在则使用该路径
3. 若不存在则检查配置文件中的 BetterGiPath
4. 若配置文件也无路径，则显示设置界面要求用户设置

**配置值验证**:
- MemoryPercent: 超出范围时恢复默认值 95
- MonitorInterval: <= 0 时恢复默认值 5
- MissingCount: 超出范围时恢复默认值 2

**命令行用法**:
```
BGIguard.exe                    启动守护进程（无参数）
BGIguard.exe set path <路径>    设置 BetterGI.exe 路径
BGIguard.exe set memory <值>    设置内存阈值 (1-100)
BGIguard.exe set interval <值>  设置监控间隔 (秒)
BGIguard.exe set count <值>     设置丢失计数阈值 (1-10)
BGIguard.exe set skip           设置/取消跳过设置界面
BGIguard.exe set show           显示当前配置
BGIguard.exe reset              重置配置为默认值
BGIguard.exe help               显示帮助
```

**交互式菜单**:
无参数启动时显示交互式菜单，可选择：
1. 修改 BetterGI 路径
2. 修改内存阈值
3. 修改监控间隔
4. 修改丢失计数阈值
5. 启动守护进程
6. 跳过设置直接启动
7. 重置配置
8. 退出

---

## 6. 代码优化说明

本项目已进行以下代码优化：

1. **字符串比较优化**: 使用 `StringComparison.OrdinalIgnoreCase` 替代 `ToLower()`，避免字符串分配
2. **进程遍历合并**: 新增 `GetBetterGiInfo()` 方法，一次遍历同时获取进程状态和命令行
3. **内存 API 优化**: 使用 `GlobalMemoryStatusEx` 替代 WMI 查询，提升性能
4. **配置缓存**: `LoadConfig()` 增加配置缓存，减少文件 IO
5. **配置值验证**: 加载配置时验证并修正不合理值
6. **日志编码**: 明确指定 UTF8 编码，避免编码问题

---

## 7. 注意事项

1. **管理员权限**: 若目录无写权限，可能需要以管理员身份运行
2. **API 权限**: 需要 PROCESS_VM_READ 权限读取目标进程内存
3. **杀毒软件**: 可能被部分杀毒软件误报，请添加信任
4. **游戏兼容性**: 仅支持检测 `YuanShen.exe` 和 `GenshinImpact.exe` 两个进程名

---

## 8. 常见问题

| 现象 | 解决 |
|------|------|
| 启动后未启动 BetterGI | 检查同目录是否存在 `BetterGI.exe`；查看日志报错 |
| 重复启动多个窗口 | 守护器自带单实例保护，只会运行一个实例 |
| 日志暴涨 | 手动删除 `BGI_guard*.log` 即可，后续版本会自动轮转 |
| 关闭游戏后 BetterGI 还在运行 | **正常现象**：守护器检测到游戏进程消失后会自动终止 BetterGI，待下次启动游戏时恢复运行 |
| 配置文件格式错误 | 运行 `BGIguard.exe reset` 重置配置文件 |

---

## 9. 免责声明

本守护器仅做**进程守护**，不对 `BetterGI.exe` 内部行为负责。  
使用即视为同意：因 BetterGI 本身或其插件导致的任何封号、掉线、损坏，与本项目无关。

---

## 10. 验收标准

- [x] 通过 NtQueryInformationProcess 获取 BetterGI.exe 启动命令
- [x] 启动 BetterGI.exe 时正确传递参数
- [x] 游戏进程退出后能自动重启 BetterGI.exe
- [x] BetterGI.exe 崩溃后能自动重启
- [x] 内存超限时能自动重启 BetterGI.exe
- [x] 防止多开守护程序（单实例保护）
- [x] 日志文件正常写入（UTF8 编码）
- [x] 配置文件使用 JSON 格式
- [x] 单文件发布后可在目标机器运行