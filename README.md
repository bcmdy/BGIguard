# BGIguard

BetterGI 进程守护程序。

仓库地址: https://github.com/bcmdy/BGIguard

优化与拆分记录:

- [优化方案](docs/OPTIMIZATION_PROPOSAL.md)
- [Program 代码下沉长任务](docs/PROGRAM_DOWNSHIFT_LONG_TASK.md)

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
