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
                if (args.Length == 2 && args[1].ToLower() == "show")
                {
                    ShowConfig();
                }
                else if (args.Length >= 2)
                {
                    CommandLineConfigResult result = HandleSetCommand(args);
                    if (!string.IsNullOrEmpty(result.Message))
                        Console.WriteLine(result.Message);
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
    private static CommandLineConfigResult HandleSetCommand(string[] args)
    {
        if (args.Length < 2)
            return CommandLineConfigResult.Failure("");

        string option = args[1].ToLower();
        if (option == "skip")
            return CommandLineConfigService.ToggleSkipSetup(ConfigStore);

        if (args.Length < 3)
        {
            ShowHelp();
            return CommandLineConfigResult.Failure("");
        }

        return option switch
        {
            "path" => CommandLineConfigService.SetPath(ConfigStore, args[2]),
            "memory" => CommandLineConfigService.SetMemory(ConfigStore, args[2]),
            "interval" => CommandLineConfigService.SetInterval(ConfigStore, args[2]),
            "count" => CommandLineConfigService.SetMissingCount(ConfigStore, args[2]),
            "memlimit" => CommandLineConfigService.SetProcessMemoryLimit(ConfigStore, args[2]),
            _ => ShowHelpAndReturnEmpty()
        };
    }

    private static void WriteConfigResult(CommandLineConfigResult result)
    {
        if (!string.IsNullOrEmpty(result.Message))
            Console.WriteLine(result.Message);
    }

    private static CommandLineConfigResult ShowHelpAndReturnEmpty()
    {
        ShowHelp();
        return CommandLineConfigResult.Failure("");
    }

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
                WriteConfigResult(CommandLineConfigService.SetPath(ConfigStore, Console.ReadLine() ?? ""));
                break;

            case "2":
                Console.Write("请输入系统内存阈值 (1-100): ");
                WriteConfigResult(CommandLineConfigService.SetMemory(ConfigStore, Console.ReadLine() ?? ""));
                break;

            case "3":
                Console.Write("请输入监控间隔 (秒): ");
                WriteConfigResult(CommandLineConfigService.SetInterval(ConfigStore, Console.ReadLine() ?? ""));
                break;

            case "4":
                Console.Write("请输入丢失计数阈值 (1-10): ");
                WriteConfigResult(CommandLineConfigService.SetMissingCount(ConfigStore, Console.ReadLine() ?? ""));
                break;

            case "5":
                Console.Write("请输入进程内存阈值 (MB, 0=禁用): ");
                WriteConfigResult(CommandLineConfigService.SetProcessMemoryLimit(ConfigStore, Console.ReadLine() ?? ""));
                break;

            case "6":
                break;

            case "7":
                WriteConfigResult(CommandLineConfigService.SetSkipSetup(ConfigStore, true));
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
