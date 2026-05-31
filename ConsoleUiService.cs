namespace BGIguard;

internal sealed class ConsoleUiService
{
    private readonly ConfigService _configStore;
    private readonly string _configFilePath;
    private readonly Action _clearConfigCache;

    public ConsoleUiService(ConfigService configStore, string configFilePath, Action clearConfigCache)
    {
        _configStore = configStore;
        _configFilePath = configFilePath;
        _clearConfigCache = clearConfigCache;
    }

    public void HandleSetCommand(string[] args)
    {
        WriteConfigResult(GetSetCommandResult(args));
    }

    public CommandLineConfigResult GetSetCommandResult(string[] args)
    {
        if (args.Length < 2)
            return CommandLineConfigResult.Failure("");

        string option = args[1].ToLower();
        if (option == "skip")
            return CommandLineConfigService.ToggleSkipSetup(_configStore);

        if (args.Length < 3)
        {
            ShowHelp();
            return CommandLineConfigResult.Failure("");
        }

        return option switch
        {
            "path" => CommandLineConfigService.SetPath(_configStore, args[2]),
            "memory" => CommandLineConfigService.SetMemory(_configStore, args[2]),
            "interval" => CommandLineConfigService.SetInterval(_configStore, args[2]),
            "count" => CommandLineConfigService.SetMissingCount(_configStore, args[2]),
            "memlimit" => CommandLineConfigService.SetProcessMemoryLimit(_configStore, args[2]),
            _ => ShowHelpAndReturnEmpty()
        };
    }

    public void ResetConfig()
    {
        if (File.Exists(_configFilePath))
        {
            File.Delete(_configFilePath);
            _clearConfigCache();
            Console.WriteLine("配置已重置为默认值");
        }
        else
        {
            Console.WriteLine("配置已经是默认值");
        }
    }

    public void ShowHelp()
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
        Console.WriteLine("默认值: 系统内存=85%, 监控间隔=5秒, 丢失计数=6次, 进程内存=4096MB, 跳过设置=false");
    }

    public void ShowConfig()
    {
        RuntimeConfig config = _configStore.Load();
        Console.WriteLine("当前配置:");
        Console.WriteLine($"  BetterGI 路径: {config.BetterGiPath}");
        Console.WriteLine($"  系统内存阈值: {config.MemoryPercent}%");
        Console.WriteLine($"  监控间隔: {config.MonitorIntervalSeconds} 秒");
        Console.WriteLine($"  丢失计数阈值: {config.MissingCountThreshold} 次");
        Console.WriteLine($"  进程内存阈值: {(config.BetterGiMemoryLimitMB > 0 ? $"{config.BetterGiMemoryLimitMB}MB" : "已禁用")}");
        Console.WriteLine($"  跳过设置: {config.SkipSetup}");
    }

    public void ShowCommandLineSetup()
    {
        Console.WriteLine("=== BGIguard 设置 ===");
        Console.WriteLine();

        ShowConfig();
        Console.WriteLine();

        Console.WriteLine("请选择操作:");
        Console.WriteLine("  1. 修改 BetterGI 路径        (BetterGI.exe 完整路径)");
        Console.WriteLine("  2. 修改系统内存阈值          (1-100%，超阈值重启)");
        Console.WriteLine("  3. 修改监控间隔              (1-999 秒，检测频率)");
        Console.WriteLine("  4. 修改丢失计数阈值          (1-10 次，连续退出触发重启)");
        Console.WriteLine("  5. 修改进程内存阈值          (MB, 0=禁用)");
        Console.WriteLine("  6. 启动守护进程              (进入守护监控模式)");
        Console.WriteLine("  7. 跳过设置直接启动          (直接进入守护)");
        Console.WriteLine("  8. 重置配置                  (恢复默认设置)");
        Console.WriteLine("  9. 退出");
        Console.WriteLine();

        Console.Write("请输入选项 (1-9): ");
        string? input = Console.ReadLine();

        switch (input)
        {
            case "1":
                Console.Write("请输入 BetterGI.exe 路径（或拖入文件，可带引号）: ");
                WriteConfigResult(CommandLineConfigService.SetPath(_configStore, Console.ReadLine() ?? ""));
                break;

            case "2":
                Console.Write("请输入系统内存阈值 (1-100): ");
                WriteConfigResult(CommandLineConfigService.SetMemory(_configStore, Console.ReadLine() ?? ""));
                break;

            case "3":
                Console.Write("请输入监控间隔 (秒): ");
                WriteConfigResult(CommandLineConfigService.SetInterval(_configStore, Console.ReadLine() ?? ""));
                break;

            case "4":
                Console.Write("请输入丢失计数阈值 (1-10): ");
                WriteConfigResult(CommandLineConfigService.SetMissingCount(_configStore, Console.ReadLine() ?? ""));
                break;

            case "5":
                Console.Write("请输入进程内存阈值 (MB, 0=禁用): ");
                WriteConfigResult(CommandLineConfigService.SetProcessMemoryLimit(_configStore, Console.ReadLine() ?? ""));
                break;

            case "6":
                break;

            case "7":
                WriteConfigResult(CommandLineConfigService.SetSkipSetup(_configStore, true));
                break;

            case "8":
                ResetConfig();
                break;

            case "9":
                Environment.Exit(0);
                break;

            default:
                break;
        }

        Console.WriteLine();
    }

    public string PromptForBetterGiPath()
    {
        Console.WriteLine("错误: 未找到 BetterGI.exe");
        Console.WriteLine("请设置 BetterGI.exe 路径:");
        Console.Write("> ");

        PathValidationResult validation = ReadBetterGiPath();
        while (!validation.IsValid)
        {
            Console.WriteLine("文件不存在或不是有效的 .exe，请重新输入 BetterGI.exe 路径:");
            Console.Write("> ");
            validation = ReadBetterGiPath();
        }

        _configStore.SavePath(validation.NormalizedPath, out _);
        Console.WriteLine($"路径已设置为: {validation.NormalizedPath}");
        return validation.NormalizedPath;
    }

    private static void WriteConfigResult(CommandLineConfigResult result)
    {
        if (!string.IsNullOrEmpty(result.Message))
            Console.WriteLine(result.Message);
    }

    private static PathValidationResult ReadBetterGiPath()
    {
        string? pathInput = Console.ReadLine();
        if (!string.IsNullOrEmpty(pathInput))
        {
            pathInput = pathInput.Trim().Trim('"');
        }

        return PathService.ValidateExecutablePath(pathInput ?? "");
    }

    private CommandLineConfigResult ShowHelpAndReturnEmpty()
    {
        ShowHelp();
        return CommandLineConfigResult.Failure("");
    }
}
