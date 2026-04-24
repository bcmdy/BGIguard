# BGIguard

BetterGI 进程守护程序

**仓库地址**: https://github.com/bcmdy/BGIguard

## 功能说明

- 自动监控 BetterGI.exe 进程运行状态
- 进程退出时自动重启（使用 cmd /c start 独立窗口）
- 系统内存（物理+虚拟）监控，内存超阈值自动重启
- 游戏进程联动：检测 YuanShen.exe / GenshinImpact.exe
- **多用户支持**：进程检查与终止按用户隔离，只操作当前用户的进程
- 单实例保护：防止多开
- 配置文件使用 JSON 格式（UTF8 编码）
- 日志按日轮转，自动清理旧日志
- 支持带引号的路径输入（如 `"D:\YS\BetterGI\BetterGI.exe"`）

## 使用方法

### 启动守护进程

直接运行 `BGIguard.exe`，首次会提示设置 BetterGI.exe 路径

### 配置命令

```bash
BGIguard.exe set path <路径>     # 设置 BetterGI.exe 路径
BGIguard.exe set memory <值>    # 设置内存阈值 (1-100)
BGIguard.exe set interval <值>  # 设置监控间隔 (秒)
BGIguard.exe set count <值>     # 设置丢失计数阈值 (1-10)
BGIguard.exe set skip           # 切换跳过设置界面
BGIguard.exe set show           # 显示当前配置
BGIguard.exe reset              # 重置配置
BGIguard.exe help               # 显示帮助
```

### 默认配置

| 配置项 | 默认值 | 取值范围 | 说明 |
|--------|--------|----------|------|
| 内存阈值 | 95% | 1-100% | 物理+虚拟内存占用百分比，超阈值重启 BetterGI |
| 监控间隔 | 5秒 | 1-999秒 | 守护循环检测间隔 |
| 丢失计数阈值 | 3次 | 1-10次 | 连续检测丢失进程次数才触发重启 |
| 跳过设置 | false | true/false | 每次启动是否跳过设置界面 |

### 配置文件说明

**BetterGiPath**: BetterGI.exe 完整路径
- 示例: `D:\Games\BetterGI\BetterGI.exe`
- 支持带引号输入

**MemoryPercent**: 内存阈值
- 系统总内存（物理+虚拟内存）占用百分比
- 超过此值时自动终止并重启 BetterGI

**MonitorInterval**: 监控间隔（秒）
- 守护循环检测频率
- 值越小检测越频繁，性能开销略增

**MissingCount**: 丢失计数阈值
- 连续检测到 BetterGI/游戏 退出次数达到此值才触发重启
- 避免短暂退出误触发

**SkipSetup**: 跳过设置
- `true`: 启动时直接进入守护模式
- `false`: 启动时显示设置菜单

首次运行后会自动生成 `BGIguard_config.json`（UTF8 编码）：

```json
{
  "BetterGiPath": "D:\\Games\\BetterGI\\BetterGI.exe",
  "MemoryPercent": 95,
  "MonitorInterval": 5,
  "MissingCount": 3,
  "SkipSetup": false
}
```

> 注意: 修改配置文件后立即生效，无需重启

## 日志说明

每次检测输出简洁日志：
```
[2026-04-24 14:30:05.123] [BGIguard_v3.0.2] [INFO] 检测 14:30:05 | 内存: 45% | BetterGI: 运行 | 游戏: YuanShen
```

内存警告（ >= 配置值-5% ）：
```
[2026-04-24 14:35:00.123] [BGIguard_v3.0.2] [WARN] [内存警告] 已用: 32768MB/49152MB (67%) | 物理: 16384MB | 虚拟: 32768MB
```

进程终止日志（含用户信息）：
```
[2026-04-24 14:40:00.123] [BGIguard_v3.0.2] [INFO] 已终止 BetterGI.exe PID:1234 (用户:Bcmdy)
[2026-04-24 14:40:00.456] [BGIguard_v3.0.2] [WARN] BetterGI.exe PID:5678 属于用户 Admin，跳过终止
```

日志文件：`BGI_guardYYYYMMDD.log`（UTF8 编码，按日生成）

## 多用户说明

BGIguard 支持多用户环境，所有进程相关操作都会按用户隔离：

- **进程检查**：只检测当前用户启动的 BetterGI 和游戏进程
- **进程终止**：只终止当前用户启动的进程，避免影响其他用户
- **日志记录**：终止日志会显示被终止进程的所属用户

这确保了在共享电脑或多用户环境下，每个用户的守护进程独立运行，互不干扰。

## 技术要求

- .NET 8.0 Runtime（或使用自包含发布版）
- Windows 10/11

## 项目结构

```
BGIguard/
├── BGIguard.csproj
├── Program.cs
├── SPEC.md              # 需求规格文档
├── CHANGELOG.md         # 更新日志
├── README.md            # 说明文档
├── build.ps1            # 构建脚本
└── Assets/icon.ico      # 程序图标
```

## 构建发布

### PowerShell 脚本
```powershell
.\build.ps1 -Version 3.0.2
```

### 手动构建
```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
```

---

By: Bcmdy