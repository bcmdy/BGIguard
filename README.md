# BGIguard

BetterGI 进程守护程序

**仓库地址**: https://github.com/bcmdy/BGIguard

## 功能说明

- 自动监控 BetterGI.exe 进程运行状态
- 进程退出时自动重启（使用 cmd /c start 独立窗口）
- 系统内存（物理+虚拟）监控，内存超阈值自动重启
- 游戏进程联动：检测 YuanShen.exe / GenshinImpact.exe
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

| 配置项 | 默认值 |
|--------|--------|
| 内存阈值 | 95% |
| 监控间隔 | 5秒 |
| 丢失计数阈值 | 2次 |

### 配置文件

首次运行后会自动生成 `BGIguard_config.json`：

```json
{
  "BetterGiPath": "D:\\Games\\BetterGI\\BetterGI.exe",
  "MemoryPercent": 95,
  "MonitorInterval": 5,
  "MissingCount": 2,
  "SkipSetup": false
}
```

## 日志说明

每次检测输出简洁日志：
```
[2026-04-15 14:30:05.123] [BGIguard_v1.0] [INFO] 检测 14:30:05 | 内存: 45% | BetterGI: 运行 | 游戏: YuanShen
```

内存警告（ >= 配置值-5% ）：
```
[2026-04-15 14:35:00.123] [BGIguard_v1.0] [WARN] [内存警告] 已用: 41779MB/49152MB (85%) | 物理: 16384MB | 虚拟: 32768MB
```

日志文件：`BGI_guardYYYYMMDD.log`（UTF8 编码，按日生成）

## 技术要求

- .NET 8.0 Runtime（或使用自包含发布版）
- Windows 10/11

## 项目结构

```
BGIguard/
├── BGIguard.csproj
├── Program.cs
├── SPEC.md              # 需求规格文档
└── README.md            # 说明文档
```

## 构建发布

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
```

---

By: Bcmdy