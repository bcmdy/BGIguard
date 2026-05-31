# BGIguard 优化方案与完成记录

## 项目现状

BGIguard 是一个基于 .NET 8 的 Windows 控制台守护工具，用于监控和重启 BetterGI，并提供多用户隔离、内存阈值保护、游戏进程联动、配置热加载和日志轮转能力。

当前项目已经完成主要结构化拆分：

- 入口编排保留在 `Program.cs`。
- 进程操作集中到 `ProcessService`。
- 配置读写集中到 `ConfigService`。
- 路径校验集中到 `PathService`。
- 内存读取集中到 `MemoryMonitor`。
- 守护循环集中到 `GuardRunner`。
- 重启条件集中到 `GuardDecision` / `GuardService`。
- 命令行配置修改集中到 `CommandLineConfigService`。
- 日志状态集中到 `AppLogger`。

## 已完成的关键优化

### 1. 启动流程

- `help`、`reset`、`set show` 等命令不再被 BetterGI 路径检测阻塞。
- 无参数启动时才进入路径检测和设置菜单。

### 2. 配置热更新

- `ConfigService` 根据配置文件 `LastWriteTimeUtc` 缓存和重新加载配置。
- 守护循环每轮都会重新读取运行时配置。

### 3. 进程启动参数安全

- 保留 `cmd.exe /c start "" "BetterGI.exe"` 启动方式。
- 启动前清理命令行参数，并过滤 `& | < > ^ %` 和换行等 cmd 高风险字符。

### 4. 多用户隔离

- 当前用户使用 SID 识别。
- 只终止当前用户拥有的 BetterGI 或旧守护进程。
- 日志中保留用户显示名和 SID，便于排查。

### 5. 日志策略

- 日志保存在程序同目录。
- 日志按日期命名，并保留最近 7 个文件。
- 日志写入失败时在控制台提示目录和权限建议。

### 6. 进程对象生命周期

- `ProcessService.ForEachProcessByName` 统一释放 `Process` 对象。
- BetterGI 快照读取合并为单次枚举，减少重复遍历。

### 7. 结构化拆分

- 已移除所有 `Program.*.cs` partial 文件。
- 无法继续自然下沉的入口编排和控制台输入输出已回到 `Program.cs`。
- 可复用逻辑已下沉至真实 service/module。

### 8. 测试

已建立 `BGIguard.Tests`，覆盖：

- 配置默认值、保存和读取。
- 路径校验和 BetterGI 路径解析。
- 命令行参数提取和清理。
- 命令行配置修改规则。
- 守护决策逻辑。
- `GuardRunner.RunOnce` 的关键重启分支。
- 进程启动参数拼接和用户匹配。

## 当前验证命令

每次改动后至少执行：

```powershell
dotnet build BGIguard.sln -c Release
dotnet test BGIguard.sln -c Release --no-build
```

发布前执行：

```powershell
.\build.ps1 -Version 5.0.0
```

## 后续建议

1. 将 `Program.cs` 中的控制台 UI 迁移到 `ConsoleUiService`，让入口文件更聚焦。
2. 明确 `GuardRunner.Run()` 的配置快照语义，避免同一轮等待和执行使用不同配置快照。
3. 将 `build.ps1` 的版本文件更新改为显式参数，避免普通发布后留下源码改动。
4. 为配置重置和交互式设置菜单增加更细的测试。
