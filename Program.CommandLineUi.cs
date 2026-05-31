namespace BGIguard;

partial class Program
{
    /// <summary>
    /// 处理命令行参数
    /// </summary>
    private static void HandleCommandLine(string[] args)
    {
        string command = args[0].ToLower();

        switch (command)
        {
            case "set":
                if (args.Length >= 3)
                {
                    if (args[1].ToLower() == "path")
                    {
                        // 设置 BetterGI 路径
                        string newPath = args[2].Trim().Trim('"');
                        if (File.Exists(newPath))
                        {
                            SaveConfigPath(newPath);
                            Console.WriteLine($"BetterGI路径已设置为: {newPath}");
                        }
                        else
                        {
                            Console.WriteLine($"错误: 文件不存在: {newPath}");
                        }
                    }
                    else if (args[1].ToLower() == "memory")
                    {
                        if (int.TryParse(args[2], out int mem) && mem > 0 && mem <= 100)
                        {
                            var cfg = LoadConfig();
                            SaveConfig(mem, cfg.monitorIntervalSeconds, cfg.missingCountThreshold, cfg.skipSetup, cfg.betterGiMemoryLimitMB);
                            Console.WriteLine($"内存阈值已设置为 {mem}%");
                        }
                        else
                        {
                            Console.WriteLine("错误: 内存阈值应在 1-100 之间");
                        }
                    }
                    else if (args[1].ToLower() == "interval")
                    {
                        if (int.TryParse(args[2], out int interval) && interval > 0)
                        {
                            var cfg = LoadConfig();
                            SaveConfig(cfg.memoryPercent, interval, cfg.missingCountThreshold, cfg.skipSetup, cfg.betterGiMemoryLimitMB);
                            Console.WriteLine($"监控间隔已设置为 {interval} 秒");
                        }
                        else
                        {
                            Console.WriteLine("错误: 监控间隔应大于 0");
                        }
                    }
                    else if (args[1].ToLower() == "count")
                    {
                        if (int.TryParse(args[2], out int count) && count > 0 && count <= 10)
                        {
                            var cfg = LoadConfig();
                            SaveConfig(cfg.memoryPercent, cfg.monitorIntervalSeconds, count, cfg.skipSetup, cfg.betterGiMemoryLimitMB);
                            Console.WriteLine($"丢失计数阈值已设置为 {count} 次");
                        }
                        else
                        {
                            Console.WriteLine("错误: 丢失计数阈值应在 1-10 之间");
                        }
                    }
                    else if (args[1].ToLower() == "skip")
                    {
                        var cfg = LoadConfig();
                        bool newSkip = !cfg.skipSetup;
                        SaveConfig(cfg.memoryPercent, cfg.monitorIntervalSeconds, cfg.missingCountThreshold, newSkip, cfg.betterGiMemoryLimitMB);
                        Console.WriteLine($"跳过设置界面已设置为: {newSkip}");
                    }
                    else if (args[1].ToLower() == "memlimit")
                    {
                        if (int.TryParse(args[2], out int limit) && limit >= 0)
                        {
                            var cfg = LoadConfig();
                            SaveConfig(cfg.memoryPercent, cfg.monitorIntervalSeconds, cfg.missingCountThreshold, cfg.skipSetup, limit);
                            if (limit == 0)
                                Console.WriteLine("进程内存监控已禁用");
                            else
                                Console.WriteLine($"进程内存阈值已设置为 {limit}MB");
                        }
                        else
                        {
                            Console.WriteLine("错误: 进程内存阈值应为 >= 0 的整数 (0 表示禁用)");
                        }
                    }
                    else
                    {
                        ShowHelp();
                    }
                }
                else if (args.Length == 2 && args[1].ToLower() == "show")
                {
                    ShowConfig();
                }
                else
                {
                    ShowHelp();
                }
                break;

            case "help":
            case "?":
                ShowHelp();
                break;

            case "reset":
                if (File.Exists(ConfigFilePath))
                {
                    File.Delete(ConfigFilePath);
                    ClearConfigCache();
                    Console.WriteLine("配置已重置为默认值");
                }
                else
                {
                    Console.WriteLine("配置已是默认值");
                }
                break;

            default:
                // 正常运行模式
                var config = LoadConfig();
                ApplyRuntimeConfig(config);

                // 检测 BetterGI 路径，未找到则强制要求设置
                EnsureBetterGiPath();

                Log("INFO", "BGIguard 启动成功");
                HandleSingleInstance();
                StartBetterGiProcess();
                RunGuardLoop();
                break;
        }
    }

    /// <summary>
    /// 显示帮助
    /// </summary>
    private static void ShowHelp()
    {
        Console.WriteLine("BGIguard 命令行工具");
        Console.WriteLine();
        Console.WriteLine("用法:");
        Console.WriteLine("  BGIguard.exe                       启动守护进程（无参数）");
        Console.WriteLine("  BGIguard.exe set path <路径>       设置 BetterGI.exe 路径");
        Console.WriteLine("  BGIguard.exe set memory <值>       设置系统内存阈值 (1-100)");
        Console.WriteLine("  BGIguard.exe set interval <值>     设置监控间隔 (秒)");
        Console.WriteLine("  BGIguard.exe set count <值>        设置丢失计数阈值 (1-10)");
        Console.WriteLine("  BGIguard.exe set memlimit <值>     设置进程内存阈值 MB (0=禁用)");
        Console.WriteLine("  BGIguard.exe set skip              设置/取消跳过设置界面");
        Console.WriteLine("  BGIguard.exe set show              显示当前配置");
        Console.WriteLine("  BGIguard.exe reset                 重置配置为默认值");
        Console.WriteLine("  BGIguard.exe help                  显示帮助");
        Console.WriteLine();
        Console.WriteLine("默认值: 系统内存=85%, 监控间隔=5秒, 丢失计数=6次, 进程内存=4096MB, 跳过设置=否");
    }

    /// <summary>
    /// 显示当前配置
    /// </summary>
    private static void ShowConfig()
    {
        var config = LoadConfig();
        Console.WriteLine("当前配置:");
        Console.WriteLine($"  BetterGI路径: {config.betterGiPath}");
        Console.WriteLine($"  系统内存阈值: {config.memoryPercent}%");
        Console.WriteLine($"  监控间隔: {config.monitorIntervalSeconds} 秒");
        Console.WriteLine($"  丢失计数阈值: {config.missingCountThreshold} 次");
        Console.WriteLine($"  进程内存阈值: {(config.betterGiMemoryLimitMB > 0 ? $"{config.betterGiMemoryLimitMB}MB" : "已禁用")}");
        Console.WriteLine($"  跳过设置: {config.skipSetup}");
    }

    /// <summary>
    /// 显示命令行设置界面
    /// </summary>
    private static void ShowCommandLineSetup()
    {
        Console.WriteLine("=== BGIguard 设置 ===");
        Console.WriteLine();

        var config = LoadConfig();
        Console.WriteLine($"当前配置:");
        Console.WriteLine($"  BetterGI路径: {config.betterGiPath}");
        Console.WriteLine($"  系统内存阈值: {config.memoryPercent}%");
        Console.WriteLine($"  监控间隔: {config.monitorIntervalSeconds} 秒");
        Console.WriteLine($"  丢失计数阈值: {config.missingCountThreshold} 次");
        Console.WriteLine($"  进程内存阈值: {(config.betterGiMemoryLimitMB > 0 ? $"{config.betterGiMemoryLimitMB}MB" : "已禁用")}");
        Console.WriteLine();

        Console.WriteLine("请选择操作:");
        Console.WriteLine("  1. 修改 BetterGI 路径        (BetterGI.exe 完整路径)");
        Console.WriteLine("  2. 修改系统内存阈值        (1-100%，超阈值重启)");
        Console.WriteLine("  3. 修改监控间隔            (1-999秒，检测频率)");
        Console.WriteLine("  4. 修改丢失计数阈值        (1-10次，连续退出触发重启)");
        Console.WriteLine("  5. 修改进程内存阈值        (MB, 0=禁用, BetterGI独占内存超限重启)");
        Console.WriteLine("  6. 启动守护进程            (进入守护监控模式)");
        Console.WriteLine("  7. 跳过设置直接启动        (直接进入守护)");
        Console.WriteLine("  8. 重置配置                (恢复默认设置)");
        Console.WriteLine("  9. 退出");
        Console.WriteLine();

        Console.Write("请输入选项 (1-9): ");
        string? input = Console.ReadLine();

        switch (input)
        {
            case "1":
                Console.Write("请输入 BetterGI.exe 路径（或拖入文件，可带引号）: ");
                string? pathInput = Console.ReadLine();
                if (!string.IsNullOrEmpty(pathInput))
                {
                    pathInput = pathInput.Trim().Trim('"');
                }
                if (!string.IsNullOrWhiteSpace(pathInput) && File.Exists(pathInput))
                {
                    SaveConfigPath(pathInput);
                    Console.WriteLine($"路径已设置为: {pathInput}");
                }
                else
                {
                    Console.WriteLine("文件不存在，保留原值");
                }
                break;

            case "2":
                Console.Write("请输入系统内存阈值 (1-100): ");
                if (int.TryParse(Console.ReadLine(), out int mem) && mem > 0 && mem <= 100)
                {
                    var cfg2 = LoadConfig();
                    SaveConfig(mem, cfg2.monitorIntervalSeconds, cfg2.missingCountThreshold, cfg2.skipSetup, cfg2.betterGiMemoryLimitMB);
                    Console.WriteLine($"系统内存阈值已设置为 {mem}%");
                }
                break;

            case "3":
                Console.Write("请输入监控间隔 (秒): ");
                if (int.TryParse(Console.ReadLine(), out int interval) && interval > 0)
                {
                    var cfg3 = LoadConfig();
                    SaveConfig(cfg3.memoryPercent, interval, cfg3.missingCountThreshold, cfg3.skipSetup, cfg3.betterGiMemoryLimitMB);
                    Console.WriteLine($"监控间隔已设置为 {interval} 秒");
                }
                break;

            case "4":
                Console.Write("请输入丢失计数阈值 (1-10): ");
                if (int.TryParse(Console.ReadLine(), out int count) && count > 0 && count <= 10)
                {
                    var cfg4 = LoadConfig();
                    SaveConfig(cfg4.memoryPercent, cfg4.monitorIntervalSeconds, count, cfg4.skipSetup, cfg4.betterGiMemoryLimitMB);
                    Console.WriteLine($"丢失计数阈值已设置为 {count} 次");
                }
                break;

            case "5":
                Console.Write("请输入进程内存阈值 (MB, 0=禁用): ");
                if (int.TryParse(Console.ReadLine(), out int limit) && limit >= 0)
                {
                    var cfg5 = LoadConfig();
                    SaveConfig(cfg5.memoryPercent, cfg5.monitorIntervalSeconds, cfg5.missingCountThreshold, cfg5.skipSetup, limit);
                    if (limit == 0)
                        Console.WriteLine("进程内存监控已禁用");
                    else
                        Console.WriteLine($"进程内存阈值已设置为 {limit}MB");
                }
                break;

            case "6":
                break;

            case "7":
                var cfg7 = LoadConfig();
                SaveConfig(cfg7.memoryPercent, cfg7.monitorIntervalSeconds, cfg7.missingCountThreshold, true, cfg7.betterGiMemoryLimitMB);
                Console.WriteLine("已设置跳过设置界面");
                break;

            case "8":
                if (File.Exists(ConfigFilePath))
                {
                    File.Delete(ConfigFilePath);
                    ClearConfigCache();
                    Console.WriteLine("配置已重置");
                }
                break;

            case "9":
                Environment.Exit(0);
                break;

            default:
                break;
        }

        Console.WriteLine();
    }
}
