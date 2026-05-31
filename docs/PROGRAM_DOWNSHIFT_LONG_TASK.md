# Program 代码下沉长任务

## 目标

最终代码结构不再保留任何 `Program.*.cs` 文件。所有可独立复用、可测试或承担业务规则的功能继续下沉到真实模块；无法继续自然下沉的入口编排、控制台输入输出和依赖组装统一回到 `Program.cs`。

`Program.cs` 应只负责：

- 组装运行时依赖和全局状态。
- 处理启动顺序和顶层模式选择。
- 调用 service 完成配置、日志、进程、内存、命令行和守护决策。

`Program.cs` 不应直接承担 Windows API 细节、进程枚举细节、配置文件解析细节、命令参数校验规则或可单测的守护判断逻辑。

## 当前结构

已完成：

- `ProcessService`：进程枚举、进程归属识别、终止、启动、BetterGI 快照、命令行读取、单实例守护。
- `ConfigService`：配置文件加载、缓存热更新、保存、默认值规范化。
- `PathService`：路径验证和 BetterGI 路径解析。
- `MemoryMonitor`：系统内存读取。
- `Logger` / `AppLogger`：日志写入、旧日志清理和日志运行状态。
- `CommandLine` / `CommandLineArguments`：命令行拆分、参数提取和启动参数清理。
- `CommandLineConfigService`：命令行和设置菜单中的配置修改规则。
- `GuardDecision` / `GuardService`：重启条件判断。
- `GuardRunner`：守护循环、每轮检测、重启动作和运行时计数状态。
- `CurrentUserService`：当前用户 SID 和显示名读取。

当前保留：

- `Program.cs`：入口编排、控制台输入输出、依赖组装和少量适配方法。

## 完成标准

- [x] 不再存在任何 `Program.*.cs` 文件。
- [x] 不再使用 `partial class Program`。
- [x] 可复用业务规则已迁移到真实模块。
- [x] 守护循环状态已迁移到 `GuardRuntimeState`。
- [x] 守护循环主体已迁移到 `GuardRunner`。
- [x] 命令行配置修改规则已迁移到 `CommandLineConfigService`。
- [x] 日志状态已迁移到 `AppLogger`。
- [x] 路径解析已迁移到 `PathService`。
- [x] 已通过 `dotnet build BGIguard.sln -c Release`。
- [x] 已通过 `dotnet test BGIguard.sln -c Release --no-build`。

## 后续优化建议

这些项目不是当前下沉目标的阻塞项，但可以继续改进：

- 将 `Program.cs` 中的控制台 UI 迁移到 `ConsoleUiService`，进一步缩小入口文件。
- 将 `GuardRunner.Run()` 的配置加载语义改为单次快照或明确的“两阶段”快照。
- 为配置重置、设置菜单和发布脚本补充更多测试或脚本验证。
- 将 `build.ps1` 的版本文件更新改为显式参数，避免普通构建后产生源码改动。

## 工作协议

后续每完成一个明确步骤：

1. 运行构建和测试。
2. 使用中文 commit 摘要和描述提交。
3. 不把多个不相关优化混在同一个提交中。
