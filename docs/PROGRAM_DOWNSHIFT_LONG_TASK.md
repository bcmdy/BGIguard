# Program 代码下沉长任务

## 目标

把 `partial Program` 从“实现细节容器”继续收敛为应用入口和流程编排层。完成时，`Program` 应只负责：

- 组装运行时依赖和全局状态。
- 处理启动顺序和顶层模式选择。
- 调用 service 完成配置、日志、进程、内存、命令行和守护决策。

不再让 `Program` 直接承担可复用业务规则、Windows API 细节、文件读写细节、命令解析细节或可单测的判断逻辑。

## 当前基线

截至本文档创建时，已经完成：

- `ProcessService` 承担进程枚举、进程归属、终止、启动、BetterGI 快照、命令行读取、单实例守护。
- `ConfigService` 承担配置文件加载、缓存热更新、保存、默认值规范化。
- `MemoryMonitor` 承担系统内存读取。
- `Logger` 承担日志写入和旧日志清理。
- `CommandLine` / `CommandLineArguments` 承担命令行拆分、参数提取和启动参数清理。
- `GuardDecision` / `GuardService` 承担重启条件判断。

仍保留在 `Program` 中的主要职责：

- `Program.cs`：入口流程、全局状态、当前用户信息、初始启动。
- `Program.Config.cs`：配置快照应用、BetterGI 路径检测、交互式路径提示、配置保存包装。
- `Program.CommandLineUi.cs`：命令行命令处理、帮助输出、配置展示、交互式设置菜单。
- `Program.Guard.cs`：守护循环编排和运行时计数状态。
- `Program.Logging.cs`：对 `Logger` 的静态包装和日志状态。
- `Program.Memory.cs`：对 `MemoryMonitor` 的静态包装。
- `Program.Process.cs`：少量进程编排包装。

## 完成标准

满足以下条件时，本长任务可视为完成：

- `Program.Process.cs` 只保留启动/终止/重启等顶层编排，或被合并回更小的入口文件。
- `Program.Memory.cs` 没有单纯转发包装，调用点直接使用 `MemoryMonitor` 或更高层 guard runner。
- `Program.Config.cs` 不再直接实现路径检测和配置保存规则；这些规则进入 `ConfigService`、`PathService` 或新的应用服务。
- `Program.CommandLineUi.cs` 的命令解析和配置修改规则下沉到专门的 command/service；UI 层只做输出和输入。
- `Program.Guard.cs` 的循环状态和每轮执行逻辑被抽到 `GuardRunner` 或等价服务，`Program` 只启动 runner。
- `Program.Logging.cs` 只保留必要的日志委托适配，或改为实例化 logger 对象后传入各服务。
- 所有改动通过 `dotnet build BGIguard.sln -c Release` 和 `dotnet test BGIguard.sln -c Release --no-build`。
- 每个阶段提交独立 git commit，摘要和描述使用中文。

## 执行顺序

### 1. 清理薄包装

优先删除没有业务价值的 `Program` 包装方法，让调用点直接使用现有 service。

候选项：

- `Program.Memory.cs:GetSystemMemory()`。
- `Program.Process.cs:IsBetterGiRunningByUser()`。
- `Program.Process.cs:RestartBetterGiProcess()` 是否保留为编排语义方法，视调用点数量决定。
- `Program.Logging.cs:CleanOldLogs()` 若无调用则删除。

验证：

- 搜索删除方法名，确认无残留调用。
- 构建和测试。

### 2. 下沉 BetterGI 路径解析

把路径检测和提示前的规范化规则收敛到 service。

候选改动：

- 在 `PathService` 或 `ConfigService` 增加 `ResolveBetterGiPath(baseDirectory, exeName, configuredPath)`。
- `Program.Config.cs:DetectBetterGiPath()` 改为调用 service。
- `PromptForBetterGiPath()` 中用户输入的 trim/validate/save 复用 `ConfigService.SavePath()` 的结果。

边界：

- 控制台输入输出暂时可继续留在 `Program.CommandLineUi.cs` / `Program.Config.cs`。
- 文件是否存在、是否 exe、路径规范化不应散落在 UI 中。

### 3. 下沉命令行命令处理

把 `HandleCommandLine` 中的命令解析、参数校验和配置更新规则移出 `Program`。

候选设计：

- 新增 `CommandLineService` 或扩展 `CommandLine`。
- 输入：`string[] args`、当前 `RuntimeConfig`、配置保存接口。
- 输出：命令执行结果，例如 `ContinueToGuard`、`Exit`、`StartGuardWithDefaultMode`。

边界：

- `Console.WriteLine` 可先保留在 UI 层，避免一次性改动过大。
- 纯规则如 set memory/interval/count/memlimit 的校验应可单测。

### 4. 下沉交互式设置菜单

把 `ShowCommandLineSetup()` 的菜单项处理拆出，使 `Program` 不直接修改配置。

候选设计：

- `SetupMenuService` 负责根据用户选择调用配置服务。
- UI 层负责读取输入和显示文本。
- 配置写入统一走 `ConfigService`。

### 5. 抽出守护循环 runner

把 `RunGuardLoop()` 从静态 `Program` 中抽为可组合服务。

候选设计：

- 新增 `GuardRunner`。
- 状态对象：`GuardRuntimeState`，包含 cached command、missing count、game exit count。
- 依赖对象：配置加载、进程服务、内存监控、日志、启动/终止动作、sleep。

边界：

- 第一阶段只移动结构，不改变无限循环行为。
- 第二阶段再引入一轮执行方法，例如 `RunOnce()`，方便测试。

### 6. 日志实例化

把 `Logger.Write(...)` 的多参数静态调用收敛为实例。

候选设计：

- `Logger` 改为 `sealed class AppLogger`，保存 exe 目录、前缀、最大文件数、版本和 cleanup 状态。
- `Program` 持有一个 logger 实例，向各 service 传 `logger.Write` 委托。

边界：

- 先保持现有日志路径和输出格式不变。
- 旧日志清理策略不变。

### 7. 测试补齐

每完成一个下沉阶段，按风险补测试：

- 路径解析：本地 exe 优先、配置路径回退、无效路径。
- 命令行 set 校验：memory/interval/count/memlimit 边界值。
- 守护 runner：进程丢失、游戏退出、系统内存超限、进程内存超限的状态重置。

## 每轮工作协议

每轮下沉遵循：

1. `git status --short` 确认工作区。
2. 只选择一类职责移动，避免混合大改。
3. 修改后运行：

```powershell
dotnet build BGIguard.sln -c Release
dotnet test BGIguard.sln -c Release --no-build
```

4. 若通过，提交中文 commit：

```powershell
git add <files>
git commit -m "<中文摘要>" -m "<中文描述>"
```

5. 更新本文档中对应阶段状态。

## 状态

- [x] 1. 清理薄包装
- [x] 2. 下沉 BetterGI 路径解析
- [x] 3. 下沉命令行命令处理
- [x] 4. 下沉交互式设置菜单
- [x] 5. 抽出守护循环 runner
- [x] 6. 日志实例化
- [x] 7. 测试补齐
