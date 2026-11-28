using MD3Zip.Core;
using System.Diagnostics;
using System.Text;

namespace MD3Zip.Core;

/// <summary>
/// 定位 7z.exe：优先使用随包分发的 Assets/7z 副本，其次回落到本机安装路径与 PATH。
/// </summary>
public static class SevenZipLocator
{
    public const string BundledFolderName = "7z";

    private static readonly string[] SystemCandidates =
    [
        @"C:\Program Files\7-Zip",
        @"C:\Program Files (x86)\7-Zip",
        @"C:\Program Files\7-Zip-Zstandard",
        @"C:\Program Files\NanaZip",
        @"C:\Program Files\WinRAR"
    ];

    public static string BundledDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", BundledFolderName);

    /// <summary>引擎路径是否落在随包目录内；用于区分「随包内核」与「回落本机」。</summary>
    public static bool IsBundledPath(string? exePath)
    {
        return !string.IsNullOrEmpty(exePath)
            && exePath.StartsWith(BundledDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>返回可用的 7z.exe 绝对路径，找不到时返回 null。</summary>
    public static string? Locate()
    {
        var bundled = Path.Combine(BundledDirectory, "7z.exe");
        if (File.Exists(bundled))
        {
            return bundled;
        }

        var sideBySide = Path.Combine(AppContext.BaseDirectory, "7z.exe");
        if (File.Exists(sideBySide))
        {
            return sideBySide;
        }

        foreach (var folder in EnumerateSystemFolders())
        {
            var candidate = Path.Combine(folder, "7z.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var part in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(part.Trim(), "7z.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // PATH 中存在非法路径时忽略。
            }
        }

        return null;
    }

    /// <summary>
    /// 启动自检：随包内核（Assets/7z）是否完整。
    /// 缺失时**不再**从本机任何位置（系统安装目录 / PATH）复制补齐——这类副本来源不可审计，
    /// 且可能与随包声明版本不一致。返回 false 时上层必须显式提示用户重新安装。
    /// </summary>
    public static bool EnsureBundled(out string? message)
    {
        var missing = MissingBundledFiles();
        if (missing.Count == 0)
        {
            message = null;
            return true;
        }

        // 整句做双语 key；缺失文件名用语言中立的 ASCII 标点追加，避免英文态混入中文顿号。
        message = L10n.L("随包内核缺失，请重新安装 MD3Zip 以恢复 Assets/7z 目录。为避免引入来源不明的内核，程序不会自动复制系统目录中的 7z 组件。")
            + $" ({string.Join(" / ", missing)})";
        return false;
    }

    private static List<string> MissingBundledFiles()
    {
        var missing = new List<string>();
        if (!File.Exists(Path.Combine(BundledDirectory, "7z.exe")))
        {
            missing.Add("7z.exe");
        }

        if (!File.Exists(Path.Combine(BundledDirectory, "7z.dll")))
        {
            missing.Add("7z.dll");
        }

        return missing;
    }

    private static IEnumerable<string> EnumerateSystemFolders()
    {
        foreach (var folder in SystemCandidates)
        {
            yield return folder;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(localAppData))
        {
            yield return Path.Combine(localAppData, "Programs", "7-Zip");
        }

        var programW6432 = Environment.GetEnvironmentVariable("ProgramW6432");
        if (!string.IsNullOrEmpty(programW6432))
        {
            yield return Path.Combine(programW6432, "7-Zip");
        }
    }
}
