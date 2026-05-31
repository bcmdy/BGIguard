# Program 代码下沉长任务

## 目标

最终代码结构不再保留任何 `Program.*.cs` 文件。所有可独立复用、可测试或承载业务规则的功能继续下沉到真实模块；无法继续自然下沉的入口编排和依赖组装统一保留在 `Program.cs`。

`Program.cs` 只负责：

- 初始化运行目录、版本标题和顶层依赖。
- 处理启动参数的顶层模式选择。
- 调用 service 完成配置、控制台交互、进程运行时和守护循环。

`Program.cs` 不再直接承担 Windows API 细节、进程枚举细节、配置文件解析细节、命令参数校验规则、控制台菜单实现或可单测的守护判断逻辑。

## 当前结构

已完成：

- `ProcessService`：进程枚举、进程归属识别、终止、启动、BetterGI 快照、命令行读取、单实例守护。
- `BetterGiRuntimeService`：BetterGI 单实例保护、启动、启动命令缓存、快照查询、游戏进程查询和重启适配。
- `RuntimeConfigProvider`：运行时配置快照、BetterGI 路径解析和 `GuardRunnerConfig` 生成。
- `GuardLoopService`：`GuardRunner` 组装和运行，接入运行时、内存和重启回调。
- `ConfigService`：配置文件加载、缓存热更新、保存、默认值规范化。
- `PathService`：路径验证和 BetterGI 路径解析。
- `MemoryMonitor`：系统内存读取。
- `Logger` / `AppLogger`：日志写入、旧日志清理和日志运行状态。
- `CommandLine` / `CommandLineArguments`：命令行拆分、参数提取和启动参数清理。
- `CommandLineConfigService`：命令行和设置菜单中的配置修改规则。
- `ConsoleUiService`：帮助文本、配置展示、设置菜单、配置重置和 BetterGI 路径输入交互。
- `GuardDecision` / `GuardService`：重启条件判断。
- `GuardRunner`：守护循环、每轮检测、重启动作和运行时计数状态。
- `CurrentUserService`：当前用户 SID 和显示名读取。

当前保留：

- `Program.cs`：入口初始化、顶层命令分发、依赖懒加载和启动编排。

## 完成标准

- [x] 不再存在任何 `Program.*.cs` 文件。
- [x] 不再使用 `partial class Program`。
- [x] 可复用业务规则已迁移到真实模块。
- [x] 控制台 UI 已迁移到 `ConsoleUiService`。
- [x] BetterGI 路径交互已迁移到 `ConsoleUiService`。
- [x] 进程运行时编排已迁移到 `BetterGiRuntimeService`。
- [x] 运行时配置快照已迁移到 `RuntimeConfigProvider`。
- [x] 守护循环组装已迁移到 `GuardLoopService`。
- [x] 守护循环状态已迁移到 `GuardRuntimeState`。
- [x] 守护循环主体已迁移到 `GuardRunner`。
- [x] 命令行配置修改规则已迁移到 `CommandLineConfigService`。
- [x] 日志状态已迁移到 `AppLogger`。
- [x] 路径解析已迁移到 `PathService`。
- [x] 已通过 `dotnet build BGIguard.sln -c Release`。
- [x] 已通过 `dotnet test BGIguard.sln -c Release --no-build`。

## 结构审计结果

当前仓库执行 `rg --files -g "Program*.cs"` 只应返回 `Program.cs`。

当前 `Program.cs` 不再直接引用：

- `ProcessService`
- `PathService`
- `MemoryMonitor`
- `GuardRunner`
- `GuardRunnerConfig`
- 用户进程 SID / 显示名读取
- 监控间隔、内存阈值、丢失计数等守护运行时字段

如果后续重新出现 `Program.*.cs` 文件，应优先判断其中逻辑是否可以进入已有 service；只有无法自然下沉的入口编排才允许回到 `Program.cs`。

## 后续优化建议

这些项目不是当前下沉目标的阻塞项，但可以继续改进：

- 为 `BetterGiRuntimeService`、`RuntimeConfigProvider`、`GuardLoopService` 补充更细的单元测试。
- 梳理源码和文档中的历史编码乱码，确保所有用户可见文本统一为 UTF-8。
- 将 `build.ps1` 的版本文件更新改为显式参数，避免普通构建后产生源代码改动。

## 工作协议

后续每完成一个明确步骤：

1. 运行构建和测试。
2. 使用中文 commit 摘要和描述提交。
3. 不把多个不相关优化混在同一个提交中。
