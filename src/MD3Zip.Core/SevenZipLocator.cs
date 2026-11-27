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
    /// 启动自检：若 Assets/7z 下缺少 7z.exe/7z.dll，尝试从本机 7-Zip 安装目录复制一份。
    /// 返回 true 表示随包副本可用。
    /// </summary>
    public static bool EnsureBundled(out string? message)
    {
        var targetDirectory = BundledDirectory;
        var targetExe = Path.Combine(targetDirectory, "7z.exe");
        var targetDll = Path.Combine(targetDirectory, "7z.dll");

        if (File.Exists(targetExe) && File.Exists(targetDll))
        {
            message = null;
            return true;
        }

        var source = Locate();
        if (source is null)
        {
            message = L10n.L("未找到 7z.exe：请将 7-Zip 的 7z.exe 与 7z.dll 放入 Assets/7z 目录，或安装 7-Zip 后重试。");
            return false;
        }

        try
        {
            var sourceDirectory = Path.GetDirectoryName(source)!;
            Directory.CreateDirectory(targetDirectory);
            File.Copy(source, targetExe, overwrite: true);

            var sourceDll = Path.Combine(sourceDirectory, "7z.dll");
            if (File.Exists(sourceDll))
            {
                File.Copy(sourceDll, targetDll, overwrite: true);
            }

            message = $"已从 {sourceDirectory} 自动补齐 7z 组件。";
            return File.Exists(targetExe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            message = $"复制 7z 组件失败（{ex.GetType().Name}）：仍将直接使用 {source}";
            return File.Exists(targetExe);
        }
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
