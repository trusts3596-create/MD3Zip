using Avalonia;
using Avalonia.Controls;

namespace MD3Zip.App;

internal static class Program
{
    /// <summary>启动参数（右键菜单进入迷你模式时使用）。</summary>
    public static CliOptions Options { get; private set; } = CliOptions.Parse([]);

    [STAThread]
    public static int Main(string[] args)
    {
        Options = CliOptions.Parse(args);

        try
        {
            return BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }
        catch (Exception ex)
        {
            WriteCrashLog(ex);
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    private static void WriteCrashLog(Exception ex)
    {
        var content = $"[{DateTime.Now:O}] {ex}{Environment.NewLine}";
        foreach (var folder in new[] { AppContext.BaseDirectory, Path.GetTempPath() })
        {
            try
            {
                var directory = Path.Combine(folder, "logs");
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, $"crash-{DateTime.Now:yyyyMMdd}.log"), content);
                return;
            }
            catch (Exception logError) when (logError is IOException or UnauthorizedAccessException)
            {
                // 尝试下一个位置。
            }
        }
    }
}
