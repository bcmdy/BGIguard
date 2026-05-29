# BGIguard 优化方案与修改简介

## 项目现状

BGIguard 是一个基于 .NET 8 的 Windows 控制台守护工具，核心能力集中在 `Program.cs`：检测并重启 BetterGI、监控游戏进程、按用户隔离终止进程、监控系统和 BetterGI 进程内存、写入按日轮转日志。

当前项目可以正常构建，但代码集中度较高，运行时配置、进程启动、日志清理、发布文档一致性等方面还有优化空间。建议优先处理影响真实运行可靠性的问题，再推进结构拆分和测试补齐。

## 优先级一：建议优先修复

### 1. 配置文件“修改后立即生效”与实现不一致

README 写明配置文件修改后无需重启即可生效，但 `LoadConfig()` 使用 `_configCache` 缓存，守护循环中没有重新加载配置。实际运行时，手动修改 `BGIguard_config.json` 后大概率不会影响当前进程。

建议修改：

- 增加配置文件 `LastWriteTimeUtc` 检测，文件变更后重新加载。
- 或使用 `FileSystemWatcher` 监听配置文件变化。
- 守护循环每轮使用当前配置快照，避免只在启动时读取 `_monitorIntervalMs`、`_memoryPercent` 等静态变量。

影响范围：

- `Program.cs` 的 `LoadConfig()`、`RunGuardLoop()` 和配置字段初始化逻辑。

### 2. 命令行帮助和配置命令会被路径检测阻塞

`Main()` 在处理 `args` 前先执行 `DetectBetterGiPath()` / `PromptForBetterGiPath()`。因此首次运行 `BGIguard.exe help`、`BGIguard.exe set show`、`BGIguard.exe reset` 时，可能先要求输入 BetterGI 路径，命令行体验不符合预期。

建议修改：

- 先处理不依赖 BetterGI 路径的命令：`help`、`reset`、`set show`。
- `set path` 只校验用户传入的新路径。
- 只有进入守护模式或需要启动 BetterGI 时，才检测并提示路径。

影响范围：

- `Program.cs` 的 `Main()`、`HandleCommandLine()`。

### 3. 保留 `cmd.exe /c start` 启动方式，并加强参数过滤

当前启动 BetterGI 使用 `cmd.exe /c start "" "BetterGI.exe" {args}`。这能让 BetterGI 独立运行，但参数包含特殊字符时可能出现转义问题，也增加了 shell 注入面。

建议修改：

- 保留 `cmd.exe /c start` 启动方式，继续保证 BetterGI 独立于守护进程运行。
- 在拼接参数前增加字符过滤或白名单校验，重点处理 `&`、`|`、`<`、`>`、`^`、`%`、换行等 cmd 特殊字符。
- 对过滤后的参数记录日志，便于排查被清理的异常启动参数。
- 暂不改动启动架构，先以低风险过滤策略降低转义问题。

影响范围：

- `StartBetterGiProcess()`、`ExtractArgs()`、`CleanCommandArgs()`。

### 4. 多用户隔离建议改为 SID 级别

当前按 `Environment.UserName` 和 `LookupAccountSid` 返回的用户名比较进程归属。用户名在域用户、本地用户、同名账户场景下可能不够精确。

建议修改：

- 获取并缓存当前用户 SID。
- 进程归属比较使用 SID 字符串，而不是用户名。
- 日志同时输出友好的 `domain\user` 或用户名，以及对应 SID，便于排查多用户环境下的归属问题。

影响范围：

- `GetProcessOwner()`、`IsProcessOwnedByCurrentUser()`、`TerminateProcessesByUser()`。

## 优先级二：提升长期运行稳定性

### 5. 日志清理不应每次写日志都扫描目录

`Log()` 每次写入后都会调用 `CleanOldLogs()`。守护循环默认 5 秒一轮，长期运行会频繁扫描日志目录。

建议修改：

- 启动时清理一次。
- 日期变化时清理一次。
- 或记录上次清理时间，最多每天清理一次。

影响范围：

- `Log()`、`CleanOldLogs()`。

### 6. 保留日志在 exe 目录，并改进写入失败提示

日志继续写到 `_exeDirectory`，便于和可执行文件、发布包放在一起排查问题。需要注意的是，如果用户把程序放到 `Program Files` 等受保护目录，普通权限可能无法写入日志。

建议修改：

- 保留日志文件位置为 exe 同目录。
- 日志写入失败时，在控制台明确提示当前日志目录和权限建议。
- README 明确说明日志保存在 exe 同目录，并提醒不要放在无写入权限的受保护目录。
- 配置文件是否继续放在 exe 同目录可保持现状，避免破坏现有用户习惯。

影响范围：

- `ConfigFilePath`、日志路径、README/SPEC。

### 7. 进程对象需要及时释放

多处 `Process.GetProcessesByName()` 返回的 `Process` 对象未显式释放。问题不一定立刻显现，但守护程序长期运行时建议用 `using` 或集中枚举后释放。

建议修改：

- 封装 `EnumerateProcessesByName()`，统一处理 `Process` 生命周期。
- 避免在多个函数中重复遍历 BetterGI 进程。

影响范围：

- `GetBetterGiInfo()`、`GetBetterGiMemoryMB()`、`IsBetterGiRunningByUser()`、`GetRunningGameProcesses()`、`TerminateProcessesByUser()`。

## 优先级三：可维护性与测试

### 8. 拆分 `Program.cs`

当前 `Program.cs` 承担入口、配置、日志、P/Invoke、进程操作、守护循环、命令行菜单等职责，后续修改容易互相影响。

建议拆分：

- `ConfigService.cs`：配置加载、校验、保存、热更新。
- `Logger.cs`：日志写入与清理策略。
- `ProcessService.cs`：进程枚举、所有者识别、启动/终止。
- `MemoryMonitor.cs`：系统内存与进程内存读取。
- `GuardService.cs`：守护循环和重启决策。
- `CommandLine.cs`：命令解析和交互菜单。

### 9. 增加单元测试和可测试接口

当前没有测试项目。建议先覆盖最容易回归的纯逻辑：

- 配置默认值、非法值修正、保存后读取。
- 路径规范化。
- 命令行参数提取和清理。
- 内存阈值判断。
- 守护循环决策逻辑可抽成纯函数测试。

建议新增：

- `BGIguard.Tests` 测试项目。
- 使用 xUnit 或 MSTest。
- 对 Windows API 相关逻辑做接口抽象，单测使用假实现。

### 10. 发布脚本和文档需统一

README/SPEC 中的版本示例已统一到当前版本，并已区分默认依赖 .NET Runtime 的发布方式和自包含发布方式。后续还可以继续增强 `build.ps1` 的参数化能力。

建议修改：

- 统一 README、SPEC、CHANGELOG、`build.ps1` 中的版本示例。
- 明确提供两种发布方式：
  - framework-dependent：体积小，要求用户安装 .NET 8 Runtime。
  - self-contained win-x64：体积大，无需用户安装 Runtime。
- `build.ps1` 增加参数：`-SelfContained`、`-Runtime win-x64`、`-OutputDir`。

影响范围：

- `README.md`、`SPEC.md`、`CHANGELOG.md`、`build.ps1`。

## 建议修改简介

第一阶段建议只做低风险稳定性修复：

1. 调整启动流程，让 `help/reset/set show` 不再要求 BetterGI 路径。
2. 修复配置热更新，让 README 承诺与实际行为一致。
3. 优化日志清理频率，避免每次写日志都扫描目录。
4. 统一 README/SPEC/build 脚本中的版本号和发布模式说明。

第二阶段处理运行可靠性（已完成）：

1. 用 SID 替换用户名比较，完善多用户隔离。
2. 保留 `cmd.exe /c start` 启动方式，增加启动参数字符过滤。
3. 保留日志在 exe 目录，增强日志写入失败提示和文档说明。
4. 释放 `Process` 对象并减少重复进程枚举。

第三阶段做结构化重构：

1. 将 `Program.cs` 拆分为配置、日志、进程、内存、守护循环、命令行模块。
2. 新增测试项目，先覆盖配置、路径、命令行参数和守护决策。
3. 在 CI 中加入 `dotnet build` 和 `dotnet test`。

## 验证建议

每阶段完成后至少执行：

```powershell
dotnet build
```

有测试项目后执行：

```powershell
dotnet test
```

发布前执行：

```powershell
.\build.ps1 -Version 4.2.1
```

并手动验证以下场景：

- 首次运行 `BGIguard.exe help` 不提示 BetterGI 路径。
- 修改 `BGIguard_config.json` 后，监控间隔和阈值在运行中生效。
- BetterGI 路径包含空格时能正常启动。
- 当前用户只终止自己的 BetterGI 进程。
- 日志保留数量符合预期。
