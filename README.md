# BGIguard

BetterGI 进程守护程序

**仓库地址**: https://github.com/bcmdy/BGIguard

## 功能说明

- 自动监控 BGI.exe 进程运行状态
- 进程退出时自动重启
- 系统内存（物理+虚拟）监控，内存超阈值自动重启
- 支持命令行配置

## 使用方法

### 启动守护进程

直接运行 `BGIguard.exe` 或 `BetterGI.exe`

### 配置命令

```bash
BGIguard.exe set memory <值>     # 设置内存阈值 (1-100)
BGIguard.exe set interval <值>   # 设置监控间隔 (秒)
BGIguard.exe set show             # 显示当前配置
BGIguard.exe set skip            # 切换跳过设置界面
BGIguard.exe reset               # 重置配置
BGIguard.exe help                # 显示帮助
```

### 默认配置

| 配置项 | 默认值 |
|--------|--------|
| 内存阈值 | 95% |
| 监控间隔 | 5秒 |

## 日志说明

每次检测输出简洁日志：
```
[INFO] 检测 18:30:15 | 内存: 45% | BGI: 运行 | 游戏: YuanShen
```

当内存占用 >= 85% 时输出详细警告：
```
[WARN] [内存警告] 已用: 41779MB/49152MB (85%) | 阈值: 95%
[WARN] [内存详情] 物理: 16384MB | 虚拟: 32768MB | 可用: 7373MB
```

日志文件：`BGI_guardYYYYMMDD.log`

## 文件说明

运行后会生成以下文件：
- `BGIguard.exe` - 守护程序主文件
- `BGI.exe` - 原始游戏启动器
- `BetterGI.exe.bak` - 原 BetterGI.exe 备份

## 环境要求

- .NET 8.0 Runtime

---
By: Bcmdy