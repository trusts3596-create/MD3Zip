using MD3Zip.Core;
using System.Windows.Forms;

namespace MD3Zip.Setup;

internal static class Program
{
    /// <summary>
    /// 用法：
    ///   MD3Zip-Setup-x64.exe                图形安装向导
    ///   MD3Zip-Setup-x64.exe /S             静默安装（默认目录）
    ///   MD3Zip-Setup-x64.exe /S /D="路径"   静默安装到指定目录
    ///   …\uninstall.exe /uninstall          图形卸载
    ///   …\uninstall.exe /uninstall /S       静默卸载
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        // 高 DPI：强制 PerMonitorV2，避免 125%/150% 缩放下向导控件错位。
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        ApplicationConfiguration.Initialize();

        var silent = HasFlag(args, "/S") || HasFlag(args, "--silent");
        var uninstall = HasFlag(args, "/uninstall") || HasFlag(args, "--uninstall");
        var targetDirectory = GetOption(args, "/D=") ?? GetOption(args, "--dir=");

        if (uninstall)
        {
            return silent ? RunSilentUninstall() : RunInteractiveUninstall();
        }

        // 当前用户安装不需要管理员；只有「注册右键菜单」那一步会单独弹一次 UAC。
        if (silent)
        {
            try
            {
                // 静默模式默认不注册右键菜单：那一步必须弹 UAC，无法无人值守完成。
                // 需要时显式加 /WithContextMenu。
                Installer.Install(new InstallOptions(
                    targetDirectory ?? Installer.DefaultDirectory,
                    DesktopShortcut: true,
                    ContextMenu: HasFlag(args, "/WithContextMenu")));
                return 0;
            }
            catch (Exception ex)
            {
                WriteLog(ex);
                return 1;
            }
        }

        Application.Run(new SetupForm(targetDirectory));
        return 0;
    }

    private static int RunSilentUninstall()
    {
        try
        {
            Installer.Uninstall();
            return 0;
        }
        catch (Exception ex)
        {
            WriteLog(ex);
            return 1;
        }
    }

    private static int RunInteractiveUninstall()
    {
        var directory = Installer.GetInstalledDirectory();
        if (string.IsNullOrEmpty(directory))
        {
            MessageBox.Show(
                $"没有找到 {Installer.ProductName} 的安装记录，可能已经被卸载。",
                Installer.ProductName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return 0;
        }

        var confirmed = MessageBox.Show(
            $"确定要卸载 {Installer.ProductName} 吗？\n\n安装位置：{directory}",
            Installer.ProductName,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (confirmed != DialogResult.Yes)
        {
            return 0;
        }

        try
        {
            Installer.Uninstall();
        }
        catch (Exception ex)
        {
            WriteLog(ex);
            MessageBox.Show($"卸载过程中出现问题：{ex.Message}", Installer.ProductName,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        MessageBox.Show(L10n.L("卸载完成。"), Installer.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        return 0;
    }

    private static bool HasFlag(string[] args, string flag) =>
        args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

    private static string? GetOption(string[] args, string prefix)
    {
        var match = args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return match?[prefix.Length..].Trim('"');
    }

    private static void WriteLog(Exception ex)
    {
        try
        {
            var file = Path.Combine(Path.GetTempPath(), "md3zip-setup.log");
            File.AppendAllText(file, $"[{DateTime.Now:O}] {ex}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // 日志失败不影响返回值。
        }
    }
}
