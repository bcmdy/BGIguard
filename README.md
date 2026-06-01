# BGIguard

BetterGI 进程守护程序。

仓库地址: https://github.com/bcmdy/BGIguard

## 功能

- 自动监控 `BetterGI.exe` 进程运行状态。
- 进程退出时自动重启，启动方式保留 `cmd /c start`，并过滤启动参数中的高风险 cmd 字符。
- 监控系统内存占用，超过阈值时重启 BetterGI。
- 监控 BetterGI 进程独占内存，超过阈值时重启 BetterGI。
- 检测 `YuanShen.exe` / `GenshinImpact.exe` 游戏进程。
- 按当前用户 SID 隔离进程检查和终止，避免影响其他用户。
- 单实例保护，防止重复运行守护进程。
- JSON 配置文件支持运行时热加载。
- 日志按日滚动，并自动清理旧日志。

## 使用方法

直接运行：

```powershell
BGIguard.exe
```

首次运行时会提示设置 `BetterGI.exe` 路径。

## 命令行

```powershell
BGIguard.exe set path <路径>       # 设置 BetterGI.exe 路径
BGIguard.exe set memory <值>       # 设置系统内存阈值 (1-100)
BGIguard.exe set memlimit <值>     # 设置进程内存阈值 MB (0=禁用)
BGIguard.exe set interval <值>     # 设置监控间隔 (秒)
BGIguard.exe set count <值>        # 设置丢失计数阈值 (1-10)
BGIguard.exe set skip              # 切换是否跳过设置界面
BGIguard.exe set show              # 显示当前配置
BGIguard.exe reset                 # 重置配置
BGIguard.exe help                  # 显示帮助
```

## 默认配置

| 配置项 | 默认值 | 范围 | 说明 |
| --- | --- | --- | --- |
| 系统内存阈值 | 85% | 1-100% | 系统内存占用超过阈值时重启 BetterGI |
| 进程内存阈值 | 4096MB | >=0 MB | BetterGI 独占内存阈值，0 表示禁用 |
| 监控间隔 | 5 秒 | >0 秒 | 守护循环检测间隔 |
| 丢失计数阈值 | 6 次 | 1-10 次 | 连续检测到进程丢失多少次后触发重启 |
| 跳过设置 | false | true/false | 是否跳过启动时设置菜单 |

## 配置文件

配置文件保存为程序同目录下的 `BGIguard_config.json`：

```json
{
  "BetterGiPath": "D:\\Games\\BetterGI\\BetterGI.exe",
  "MemoryPercent": 85,
  "MonitorInterval": 5,
  "MissingCount": 6,
  "SkipSetup": false,
  "BetterGiMemoryLimitMB": 4096
}
```

程序会检测配置文件更新时间，运行中手动修改配置后会在后续监控循环中生效。

## 日志

日志保存在程序同目录，文件名格式：

```text
BGI_guardYYYYMMDD.log
```

默认保留最近 7 个日志文件。请避免把程序放在 `Program Files` 等普通用户无写入权限的目录中，否则日志和配置文件可能写入失败。

## 项目文件结构

### 入口与运行编排

| 文件 | 作用 |
| --- | --- |
| `src/BGIguard/Program.cs` | 程序入口，负责初始化运行目录、标题、顶层命令分发和启动编排。 |
| `src/BGIguard/GuardLoopService.cs` | 组装并运行 `GuardRunner`，把运行时服务、配置快照、内存读取和重启回调接到守护循环。 |
| `src/BGIguard/BetterGiRuntimeService.cs` | 管理 BetterGI 运行时行为，包括单实例保护、启动、命令缓存、进程快照、游戏进程查询和重启适配。 |
| `src/BGIguard/RuntimeConfigProvider.cs` | 负责运行时配置快照、BetterGI 路径解析和 `GuardRunnerConfig` 生成。 |
| `src/BGIguard/ConsoleUiService.cs` | 负责控制台 UI，包括帮助文本、配置展示、设置菜单、配置重置和 BetterGI 路径输入交互。 |

### 核心服务

| 文件 | 作用 |
| --- | --- |
| `src/BGIguard/ProcessService.cs` | 封装进程枚举、进程归属识别、进程终止、启动、命令行读取和 BetterGI 快照。 |
| `src/BGIguard/ConfigService.cs` | 读取、规范化、缓存和保存 `BGIguard_config.json`。 |
| `src/BGIguard/PathService.cs` | 验证可执行文件路径，并解析 BetterGI 的本地或配置路径。 |
| `src/BGIguard/MemoryMonitor.cs` | 读取系统内存、物理内存和虚拟内存占用。 |
| `src/BGIguard/CurrentUserService.cs` | 读取当前用户 SID 和显示名，用于多用户进程隔离。 |
| `src/BGIguard/AppLogger.cs` | 当前日志实现，负责写入日志、控制台输出和旧日志清理。 |

### 命令行与配置规则

| 文件 | 作用 |
| --- | --- |
| `src/BGIguard/CommandLine.cs` | 从完整进程命令行中提取 BetterGI 启动参数。 |
| `src/BGIguard/CommandLineArguments.cs` | 清理启动参数，并过滤 cmd 高风险字符。 |
| `src/BGIguard/CommandLineConfigService.cs` | 实现 `set path/memory/interval/count/memlimit/skip` 等配置修改规则。 |

### 守护判断

| 文件 | 作用 |
| --- | --- |
| `src/BGIguard/GuardRunner.cs` | 守护循环主体，负责每轮检测、状态日志、计数状态和触发重启。 |
| `src/BGIguard/GuardDecision.cs` | 纯判断逻辑，便于单元测试重启条件。 |

### 项目与发布

| 文件 | 作用 |
| --- | --- |
| `src/BGIguard/BGIguard.csproj` | 主程序项目文件，定义目标框架、版本、图标和清单。 |
| `BGIguard.sln` | Visual Studio / dotnet 解决方案文件。 |
| `src/BGIguard/app.manifest` | Windows 应用清单。 |
| `src/BGIguard/Assets/icon.ico` | 应用图标。 |
| `build.ps1` | 发布脚本，支持版本号、自包含发布和运行时参数。 |
| `clean.ps1` | 清理脚本。 |
| `global.json` | 固定或约束 .NET SDK 版本。 |
| `CHANGELOG.md` | 版本变更记录。 |
| `SPEC.md` | 功能规格说明。 |

### 测试

| 文件 | 作用 |
| --- | --- |
| `tests/BGIguard.Tests/BGIguard.Tests.csproj` | 测试项目文件。 |
| `tests/BGIguard.Tests/ConfigServiceTests.cs` | 配置默认值、保存和读取测试。 |
| `tests/BGIguard.Tests/PathServiceTests.cs` | 路径验证和 BetterGI 路径解析测试。 |
| `tests/BGIguard.Tests/CommandLineArgumentsTests.cs` | 启动参数清理和过滤测试。 |
| `tests/BGIguard.Tests/CommandLineConfigServiceTests.cs` | 命令行配置修改规则测试。 |
| `tests/BGIguard.Tests/GuardDecisionTests.cs` | 守护判断逻辑测试。 |
| `tests/BGIguard.Tests/GuardRunnerTests.cs` | 守护循环单轮执行和关键分支测试。 |
| `tests/BGIguard.Tests/ProcessServiceTests.cs` | 进程启动参数拼接和用户匹配测试。 |
| `tests/BGIguard.Tests/GlobalUsings.cs` | 测试项目全局 using。 |

## 构建

需要 .NET 8 SDK。

```powershell
dotnet build BGIguard.sln -c Release
dotnet test BGIguard.sln -c Release --no-build
```

发布：

```powershell
.\build.ps1 -Version 5.0.0
```

自包含发布：

```powershell
.\build.ps1 -Version 5.0.0 -SelfContained -Runtime win-x64
```
