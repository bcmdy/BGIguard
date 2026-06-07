# BGIguard 优化建议文档

检查日期：2026-06-07  
检查范围：项目结构、核心守护循环、配置与日志、进程启动与终止、测试与发布脚本。

## 当前结论

BGIguard 当前已经完成了一轮比较有效的结构整理：主程序位于 `src/BGIguard`，测试位于 `tests/BGIguard.Tests`；核心判断逻辑集中在 `GuardDecision`；守护循环通过 `GuardRunnerOptions` 注入外部依赖，单元测试覆盖了配置、路径、命令行参数、守护判断和守护循环分支。

本次检查结果：

- `dotnet build BGIguard.sln -c Release` 通过，0 警告，0 错误。
- `dotnet test BGIguard.sln -c Release --no-build` 通过，26 个测试全部成功。
- 下一阶段优化重点不再是大规模拆分，而是提升运行稳定性、发布可控性、异常可观测性和 Windows 进程交互边界的可测试性。

## 优先级 P0：文档与编码卫生

### 1. 修复中文文档和源码注释的编码显示问题

现象：在 PowerShell 默认读取方式下，`README.md`、旧版 `docs/OPTIMIZATION.md`、部分源码注释和日志兜底提示会显示为乱码。文件很可能是 UTF-8 内容，但协作环境或终端读取方式不统一，会影响维护者阅读、发布说明复制和问题排查。

建议：

- 统一仓库文本文件为 UTF-8。
- 在 `.editorconfig` 中明确 `charset = utf-8`。
- 检查 `README.md`、`SPEC.md`、`CHANGELOG.md`、`docs/*.md` 和源码中的中文字符串是否可读。
- 文档中保留 PowerShell 读取建议，例如使用 `Get-Content -Encoding utf8`。

收益：

- 降低中文说明、日志提示和发布文案在不同编辑器中的乱码概率。
- 减少后续提交中“看似大面积改动、实际只是编码变化”的噪音。

验证：

```powershell
dotnet test BGIguard.sln -c Release
Get-Content -Encoding utf8 README.md
Get-Content -Encoding utf8 docs\OPTIMIZATION.md
```

## 优先级 P1：运行稳定性

### 2. 为守护循环增加可取消机制

`GuardRunner.Run()` 当前是无限循环，并通过注入的 `Sleep` 进行等待。对实际守护程序而言这可以工作，但在后续加入托盘、服务模式、集成测试或优雅退出时会比较吃力。

建议：

- 将 `Run()` 改为接收 `CancellationToken`。
- 将 `Action<int> Sleep` 替换为可取消等待，例如 `Func<int, CancellationToken, Task>`，或保留同步版本但在循环中检查 token。
- `Program.Main` 中注册 `Console.CancelKeyPress`，在用户按 Ctrl+C 时记录日志并退出。

收益：

- 支持优雅退出，避免只能强制关闭进程。
- 方便编写多轮守护循环测试。
- 为后续 Windows Service、托盘驻留或后台任务模式留出接口。

建议测试：

- token 取消后守护循环停止。
- Ctrl+C 触发退出日志。
- `RunOnce` 现有测试保持不变。

### 3. 避免日志失败信息再次乱码

`AppLogger.Write` 中日志写入失败时的控制台提示目前仍存在乱码风险。这个分支通常只在程序目录不可写时出现，正好是用户最需要清楚提示的时候。

建议：

- 修正 `AppLogger` 中 catch 分支的中文字符串。
- 增加一条更明确的解决建议：将程序移动到用户可写目录，或以管理员身份运行。
- 可选：当程序目录不可写时，回退写入 `%LOCALAPPDATA%\BGIguard\logs`。

收益：

- 用户在 `Program Files` 等受保护目录运行时更容易自助排查。
- 日志系统失败时仍能保留最低限度的可观测性。

建议测试：

- 对 `AppLogger` 增加临时目录写入与日志清理测试。
- 手动验证不可写目录下的提示是否可读。

### 4. 重启触发增加冷却时间

当前系统内存超限、进程内存超限、BetterGI 丢失、游戏退出都可能触发重启。若外部环境持续异常，程序可能频繁重启 BetterGI。

建议：

- 在 `GuardRuntimeState` 中增加 `LastRestartUtc`。
- 在 `GuardRunnerConfig` 中增加 `RestartCooldownSeconds`，默认可设为 30 到 60 秒。
- 所有重启入口统一经过一个 `TryRestartBetterGi` 方法，集中处理冷却、日志和计数重置。

收益：

- 避免异常状态下反复杀进程、起进程。
- 日志更容易看出“本轮跳过重启是因为冷却中”，便于定位。

建议测试：

- 冷却期内不重复调用 `RestartBetterGi`。
- 冷却期后恢复正常重启。
- 不同触发原因共用同一个冷却状态。

## 优先级 P1：进程启动安全

### 5. 评估替换 `cmd /c start` 启动方式

`ProcessService.StartDetachedWithCmdStart` 当前通过 `cmd.exe /c start "" "BetterGI.exe" args` 启动 BetterGI，并额外过滤 `& | < > ^ %` 等高风险字符。这个方案兼容性较好，但安全边界依赖字符串拼接和过滤。

建议分两步处理：

- 短期：继续保留 `cmd /c start`，但将所有启动参数处理集中在一个方法中，并扩大测试覆盖。
- 中期：评估直接使用 `ProcessStartInfo` 启动 BetterGI，使用 `ArgumentList` 传参；如果必须脱离父进程，再明确记录原因。

收益：

- 降低命令注入风险。
- 避免合法参数被过度过滤。
- 启动失败时可以获得更直接的异常和返回信息。

建议测试：

- 参数包含空格、引号、反斜杠时能正确保留。
- 参数包含高风险字符时被过滤并记录 WARN。
- `BuildCmdStartArguments` 对空参数和多参数的输出稳定。

### 6. 拆出 Windows 原生调用边界

`ProcessService` 集中处理枚举进程、读取 PEB 命令行、Token/SID 查询、终止和启动。这种集中实现目前可接受，但 P/Invoke 与业务逻辑混在一起后，边界场景会越来越难测。

建议：

- 暂不做大拆分，先引入薄边界接口，例如 `IProcessInspector` 或内部委托集合。
- 将 `GetProcessCommandLine`、`GetProcessOwner`、`OpenProcess/ReadProcessMemory` 相关代码视为 Windows 原生边界。
- 对纯逻辑部分继续留在 `ProcessService`，避免过度抽象。

收益：

- 后续可以模拟权限不足、进程退出、读取命令行失败等场景。
- 让 Windows API 失败时的行为更可预测。

建议测试：

- 无权限读取进程 owner 时跳过终止。
- 进程枚举期间进程退出时不影响守护循环。
- 匹配路径大小写不敏感。

## 优先级 P2：配置与发布

### 7. 给配置文件增加版本字段

当前配置文件模型较简单，字段缺失时通过默认值归一化。后续若增加重启冷却、日志路径、游戏进程列表等配置项，建议引入配置版本。

建议：

- 在 `ConfigFileModel` 中增加 `ConfigVersion`。
- 对缺失版本的旧配置按 v1 处理。
- 保存配置时写入当前版本。

收益：

- 为未来迁移提供明确入口。
- 用户手动编辑旧配置时仍能兼容。

建议测试：

- 旧配置无版本字段时可正常加载。
- 新字段缺失时使用默认值。
- 保存后写入当前配置版本。

### 8. 发布脚本增加产物自检

`build.ps1` 已支持版本号、自包含发布和运行时参数。建议在发布后增加轻量自检，确保产物可启动、版本号正确、关键文件存在。

建议：

- 发布后检查 `BGIguard.exe`、`app.manifest`、`Assets/icon.ico` 是否进入预期目录。
- 增加 `BGIguard.exe help` 的烟雾测试。
- 检查生成文件版本是否与传入版本一致。

收益：

- 减少“构建成功但发布包缺文件”的风险。
- 发布流程更适合 CI 自动化。

验证：

```powershell
.\build.ps1 -Version 5.0.0
.\publish\BGIguard.exe help
```

## 优先级 P2：测试覆盖

### 9. 增加日志和配置热加载测试

现有测试主要覆盖纯逻辑和命令配置规则。下一阶段建议补上两个高价值区域：日志写入清理、配置热加载。

建议：

- `AppLogger`：验证写入 UTF-8 日志、按日期前缀清理旧日志、异常时不抛出。
- `RuntimeConfigProvider`：验证配置文件变更后下一轮 `ReloadGuardRunnerConfig` 使用新阈值。
- `ConfigService`：验证非法 JSON 时返回默认配置并记录错误。

收益：

- 保护用户最常接触的配置和日志行为。
- 后续重构时更敢动 `Program` 装配和运行时配置逻辑。

## 暂不建议立即修改

### `Program` 的静态装配方式

`Program` 当前用静态属性懒加载服务。虽然不是依赖注入框架风格，但项目规模小、启动路径清晰，暂时没有必要引入 Generic Host 或 DI 容器。建议等到需要 Windows Service、托盘常驻、结构化日志或多运行模式时再调整。

### `GuardRunner` 的类长度

`GuardRunner` 已经把判断逻辑下沉到 `GuardDecision`，并通过 options 注入外部依赖。它虽然承担多种触发原因的状态推进，但仍属于“单轮守护决策”的同一职责。短期只建议提取统一重启入口，不建议继续拆成很多小类。

### `ProcessService` 的整体拆分

`ProcessService` 较长，但它集中的是 Windows 进程相关细节。当前更合适的方向是先增加边界测试和少量接口，而不是立刻按方法拆文件。

## 建议执行顺序

1. 修复中文编码与乱码提示，补充 `.editorconfig`。
2. 为 `AppLogger` 增加日志写入和清理测试。
3. 为 `GuardRunner` 增加可取消循环与 Ctrl+C 退出。
4. 为重启逻辑增加冷却时间和统一重启入口。
5. 扩展命令行参数与 `cmd /c start` 的安全测试。
6. 给配置文件增加版本字段，为后续配置迁移做准备。
7. 增强 `build.ps1` 发布产物自检。

每完成一个步骤后建议运行：

```powershell
dotnet test BGIguard.sln -c Release
```

涉及发布脚本或产物目录时，再补充：

```powershell
dotnet build BGIguard.sln -c Release
.\build.ps1 -Version 5.0.0
```
