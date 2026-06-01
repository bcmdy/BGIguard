# BGIguard 优化与精简建议

本文档记录对当前项目结构、代码职责、仓库产物和测试状态的检查结论，作为后续优化的执行清单。

检查日期：2026-06-01

## 当前状态

- `dotnet build` 通过，0 个警告，0 个错误。
- `dotnet test` 通过，26 个测试全部成功。
- 项目整体规模较小，服务拆分比较清晰，核心守护逻辑已有单元测试保护。
- 主要优化空间集中在：删除薄封装、减少配置保存重复参数、清理自动生成文件、收拢日志实现。

## 优先级 P0：仓库卫生

### 1. [x] 移除 `BGIguard.csproj.lscache`

完成状态：已从 Git 索引移除 `BGIguard.csproj.lscache`，并在 `.gitignore` 中加入 `*.lscache`。

`BGIguard.csproj.lscache` 是自动生成的本地缓存文件，文件内容也标明会自动重新生成。它不应进入版本库。

建议操作：

```powershell
git rm --cached BGIguard.csproj.lscache
```

同时在 `.gitignore` 中补充：

```gitignore
*.lscache
```

收益：

- 避免本地 IDE/MSBuild 缓存污染提交。
- 减少无意义 diff。
- 保持仓库只包含源码、配置、文档和必要资源。

风险：

- 很低。该文件可自动再生成。

## 优先级 P1：安全精简

### 2. [x] 删除 `GuardService`

完成状态：已删除 `GuardService.cs`，`GuardRunner.cs` 改为直接调用 `GuardDecision`，并同步更新 README 的项目结构说明。

`GuardService.cs` 目前只是把方法原样转发给 `GuardDecision`：

- `GuardService.ShouldRestartForProcessMemory` -> `GuardDecision.ShouldRestartForProcessMemory`
- `GuardService.ShouldRestartForMissingProcess` -> `GuardDecision.ShouldRestartForMissingProcess`
- `GuardService.ShouldRestartForSystemMemory` -> `GuardDecision.ShouldRestartForSystemMemory`
- `GuardService.ShouldRestartForGameExit` -> `GuardDecision.ShouldRestartForGameExit`

建议：

- 在 `GuardRunner.cs` 中直接调用 `GuardDecision`。
- 删除 `GuardService.cs`。
- 更新 `README.md` 中的项目结构说明，去掉 `GuardService.cs`。

收益：

- 删除一层没有业务增量的封装。
- 守护判断逻辑的真实入口更明确。
- 测试仍然可以继续覆盖 `GuardDecision`。

风险：

- 很低。属于机械替换。

验证：

```powershell
dotnet test
```

### 3. 简化 `ConfigService.SaveSettings`

当前 `SaveSettings` 接口如下：

```csharp
SaveSettings(
    int memoryPercent,
    int monitorIntervalSeconds,
    int missingCountThreshold,
    bool skipSetup,
    int betterGiMemoryLimitMB)
```

这导致 `CommandLineConfigService` 每修改一个字段，都需要先读取整份配置，再把其他字段原样传回去。例子：

```csharp
RuntimeConfig config = configStore.Load();
configStore.SaveSettings(
    memoryPercent,
    config.MonitorIntervalSeconds,
    config.MissingCountThreshold,
    config.SkipSetup,
    config.BetterGiMemoryLimitMB);
```

建议改为保存完整 `RuntimeConfig`，例如：

```csharp
public void SaveSettings(RuntimeConfig config)
{
    RuntimeConfig existing = Load();
    Save(existing with
    {
        MemoryPercent = config.MemoryPercent,
        MonitorIntervalSeconds = config.MonitorIntervalSeconds,
        MissingCountThreshold = config.MissingCountThreshold,
        SkipSetup = config.SkipSetup,
        BetterGiMemoryLimitMB = config.BetterGiMemoryLimitMB
    });
}
```

或者更直接地提供内部保存方法：

```csharp
public void SaveConfig(RuntimeConfig config)
{
    Save(Normalize(config));
}
```

然后调用处使用：

```csharp
RuntimeConfig config = configStore.Load();
configStore.SaveConfig(config with { MemoryPercent = memoryPercent });
```

收益：

- 减少重复参数传递。
- 降低字段顺序传错的风险。
- 新增配置项时，调用点改动更少。

风险：

- 中低。需要同步调整 `ConfigServiceTests` 和 `CommandLineConfigServiceTests`。

验证：

```powershell
dotnet test --filter ConfigServiceTests
dotnet test --filter CommandLineConfigServiceTests
dotnet test
```

## 优先级 P2：结构收拢

### 4. 合并 `AppLogger` 与 `Logger`

`AppLogger` 当前保存日志所需状态，实际写入逻辑在静态 `Logger.Write` 中。`Logger.Write` 因此需要接收很多状态参数：

- 日志目录
- 日志前缀
- 最大日志文件数
- 显示版本
- 锁对象
- 上次清理日期
- 日志级别
- 日志内容

建议：

- 将 `Logger.Write` 和 `Logger.CleanOldLogs` 的实现移动到 `AppLogger`。
- 删除静态 `Logger`，或者仅保留非常小的纯函数工具。

收益：

- 日志状态和日志行为放在同一个类里。
- 减少长参数列表。
- `Program.LoggerStore` 的语义更自然。

风险：

- 中低。日志写入涉及文件系统，建议补一个小测试或至少保留手动验证。

验证：

```powershell
dotnet test
dotnet run
```

手动检查：

- 控制台有日志输出。
- 程序目录生成 `BGI_guardYYYYMMDD.log`。
- 旧日志清理逻辑仍生效。

### 5. 减少 `RuntimeConfigProvider` 重复加载

`RuntimeConfigProvider.Reload()` 已经调用 `_configStore.Load()` 并更新 `Current`，但 `DetectBetterGiPath()` 内部又调用了一次 `_configStore.Load()`。

建议：

- 让 `DetectBetterGiPath` 使用 `Current`。
- 或者改为 `DetectBetterGiPath(RuntimeConfig config)`，由调用者传入刚加载的配置。

收益：

- 减少状态读取路径。
- `RuntimeConfigProvider` 的当前配置语义更清晰。

风险：

- 低。注意保留运行时热加载行为。

验证：

```powershell
dotnet test
```

同时手动验证：

- 修改 `BGIguard_config.json` 后，守护循环下一轮能读取新配置。
- BetterGI 路径丢失时仍会提示用户输入。

## 优先级 P3：长期结构优化

### 6. 调整源码目录结构

当前主项目文件位于仓库根目录，测试项目位于 `BGIguard.Tests/`。由于 SDK 项目默认递归包含源码，主项目需要显式排除测试源码：

```xml
<Compile Remove="BGIguard.Tests\**\*.cs" />
<None Remove="BGIguard.Tests\**\*" />
```

长期建议：

```text
src/
  BGIguard/
    BGIguard.csproj
tests/
  BGIguard.Tests/
    BGIguard.Tests.csproj
```

收益：

- 主项目不再需要排除测试源码。
- 目录职责更清楚。
- 后续增加工具、文档、发布脚本时更容易维护。

风险：

- 中。会影响解决方案、项目引用、脚本、CI、文档路径。

建议时机：

- 不作为当前小优化的一部分。
- 等下一次版本整理或发布流程调整时一起做。

## 暂不建议立即修改

### `ProcessService`

`ProcessService` 较长，但它集中处理 Windows 进程、Token、SID、命令行读取、进程终止等平台相关细节。虽然可以拆分，但拆分会增加跨类协作成本。

建议暂时保持现状，除非后续出现以下情况：

- 需要支持非 Windows 平台。
- 需要独立测试 P/Invoke 相关边界。
- 进程启动、进程归属、命令行读取继续扩张。

### `GuardRunner`

`GuardRunner` 是核心循环，已经通过 `GuardRunnerOptions` 注入外部依赖，便于单测。虽然它有一定长度，但职责仍集中在“单轮守护决策和状态推进”。

建议只做局部精简，不急于拆成更多小类。

## 建议执行顺序

1. 清理 `BGIguard.csproj.lscache` 并更新 `.gitignore`。
2. 删除 `GuardService`，调用点改为 `GuardDecision`。
3. 重构 `ConfigService.SaveSettings` 为基于 `RuntimeConfig` 的保存接口。
4. 合并 `AppLogger` 和 `Logger`。
5. 简化 `RuntimeConfigProvider` 的重复配置加载。
6. 在后续版本整理时再考虑 `src/` + `tests/` 目录迁移。

每完成一步后建议运行：

```powershell
dotnet test
```

涉及发布脚本或项目结构时，再补充：

```powershell
dotnet build
.\build.ps1 -Version 5.0.0
```
