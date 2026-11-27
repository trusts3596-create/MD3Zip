namespace MD3Zip.App;

public enum LaunchMode
{
    /// <summary>完整 MD3 压缩包浏览器。</summary>
    Browser,

    /// <summary>迷你悬浮卡片：解压到当前目录。</summary>
    ExtractHere,

    /// <summary>迷你悬浮卡片：智能解压（自动避免套娃）。</summary>
    SmartExtract,

    /// <summary>压缩配置对话框。</summary>
    CompressDialog
}

/// <summary>
/// 轻量 CLI 路由：右键菜单调用的入口只需要毫秒级解析，不加载主窗口。
/// </summary>
public sealed class CliOptions
{
    public LaunchMode Mode { get; init; } = LaunchMode.Browser;

    public string? ArchivePath { get; init; }

    public List<string> Sources { get; init; } = [];

    public string? Password { get; init; }

    public static CliOptions Parse(string[] args)
    {
        var mode = LaunchMode.Browser;
        string? archive = null;
        string? password = null;
        var sources = new List<string>();
        var explicitMode = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.IsNullOrWhiteSpace(arg))
            {
                continue;
            }

            switch (arg.ToLowerInvariant())
            {
                case "--extract-here":
                case "-x":
                    mode = LaunchMode.ExtractHere;
                    explicitMode = true;
                    archive = TakeValue(args, ref i);
                    break;

                case "--smart-extract":
                case "-sx":
                    mode = LaunchMode.SmartExtract;
                    explicitMode = true;
                    archive = TakeValue(args, ref i);
                    break;

                case "--compress-dialog":
                case "-c":
                    mode = LaunchMode.CompressDialog;
                    explicitMode = true;
                    break;

                case "--password":
                case "-p":
                    password = TakeValue(args, ref i);
                    break;

                default:
                    if (arg.StartsWith("--", StringComparison.Ordinal))
                    {
                        // 未知开关直接忽略，保证右键菜单向前兼容。
                        break;
                    }

                    if (mode == LaunchMode.CompressDialog)
                    {
                        sources.Add(arg);
                    }
                    else if (archive is null)
                    {
                        archive = arg;
                    }
                    else if (!explicitMode)
                    {
                        sources.Add(arg);
                    }

                    break;
            }
        }

        return new CliOptions
        {
            Mode = mode,
            ArchivePath = archive,
            Password = password,
            Sources = sources
        };
    }

    private static string? TakeValue(string[] args, ref int index)
    {
        if (index + 1 >= args.Length)
        {
            return null;
        }

        index++;
        return args[index];
    }
}
