using MD3Zip.Core;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace MD3Zip.Setup;

internal sealed record InstallOptions(string TargetDirectory, bool DesktopShortcut, bool ContextMenu);

internal sealed record InstallProgress(string Stage, int Percent);

/// <summary>
/// 安装 / 卸载的全部副作用都收敛在这里：解压负载、快捷方式、卸载注册表、可选的右键菜单。
/// 任何一步失败都会抛异常，由 UI 层统一提示。
/// </summary>
internal static class Installer
{
    public const string ProductName = "MD3 Zip";
    public const string ProductVersion = "1.0.0";
    public const string AppExeName = "MD3Zip.App.exe";
    public const string UninstallScriptName = "uninstall.ps1";

    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MD3Zip";
    private const string PayloadResourceName = "MD3Zip.Payload.zip";
    private const string ShellPackageName = "MD3Zip.Shell";

    /// <summary>当前用户安装：免 UAC，卸载项写 HKCU，符合个人工具的定位。</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", ProductName);

    public static string DesktopDirectory =>
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    public static string StartMenuDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), ProductName);

    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static string? GetInstalledDirectory()
    {
        using var key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath);
        return key?.GetValue("InstallLocation") as string;
    }

    // ------------------------------------------------------------------
    // 安装
    // ------------------------------------------------------------------
    public static void Install(InstallOptions options, Action<InstallProgress>? report = null)
    {
        var target = options.TargetDirectory;
        if (string.IsNullOrWhiteSpace(target))
        {
            throw new InvalidOperationException(L10n.L("安装目录不能为空。"));
        }

        report?.Invoke(new(L10n.L("正在准备安装目录…"), 1));
        Directory.CreateDirectory(target);

        report?.Invoke(new(L10n.L("正在读取安装包负载…"), 3));
        using var payload = OpenPayload();

        ExtractPayload(payload, target, report);

        report?.Invoke(new(L10n.L("正在创建快捷方式…"), 82));
        CreateShortcuts(target, options.DesktopShortcut);

        report?.Invoke(new(L10n.L("正在写入卸载脚本…"), 88));
        WriteUninstallScript(target);

        report?.Invoke(new(L10n.L("正在注册卸载信息…"), 92));
        RegisterUninstall(target);

        if (options.ContextMenu)
        {
            report?.Invoke(new(L10n.L("正在注册 Windows 11 右键菜单…"), 95));
            TryRegisterContextMenu(target);
        }

        report?.Invoke(new(L10n.L("安装完成"), 100));
    }

    private static ZipArchive OpenPayload()
    {
        var stream = typeof(Installer).Assembly.GetManifestResourceStream(PayloadResourceName)
            ?? throw new InvalidOperationException(
                "安装包缺少内置文件负载（payload.zip）。请用 packaging\\build_setup.ps1 重新构建安装包。");

        return new ZipArchive(stream, ZipArchiveMode.Read);
    }

    private static void ExtractPayload(ZipArchive archive, string destination, Action<InstallProgress>? report)
    {
        var entries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
        if (entries.Count == 0)
        {
            throw new InvalidOperationException(L10n.L("安装包负载为空。"));
        }

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            var targetPath = Path.Combine(destination, relative);

            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            entry.ExtractToFile(targetPath, overwrite: true);

            if (i % 10 == 0 || i == entries.Count - 1)
            {
                var percent = 5 + (int)(75.0 * (i + 1) / entries.Count);
                report?.Invoke(new($"正在释放 {entry.Name}", percent));
            }
        }
    }

    private static void CreateShortcuts(string target, bool desktop)
    {
        var exe = Path.Combine(target, AppExeName);
        var icon = Path.Combine(target, "Assets", "AppIcon.ico");
        if (!File.Exists(icon))
        {
            icon = exe;
        }

        Shortcut.Create(
            Path.Combine(StartMenuDirectory, $"{ProductName}.lnk"),
            exe,
            target,
            ProductName,
            icon);

        if (desktop)
        {
            Shortcut.Create(
                Path.Combine(DesktopDirectory, $"{ProductName}.lnk"),
                exe,
                target,
                ProductName,
                icon);
        }
    }

    /// <summary>
    /// 安装器自身带着 40+ MB 的负载，复制一份当作卸载程序会让安装体积翻倍。
    /// 这里改为写一个几 KB 的 PowerShell 卸载脚本，卸载项直接指向它。
    /// </summary>
    private static void WriteUninstallScript(string target)
    {
        var scriptPath = Path.Combine(target, UninstallScriptName);
        var desktopLink = Path.Combine(DesktopDirectory, ProductName + ".lnk");

        var script = $@"
$ErrorActionPreference = 'SilentlyContinue'
Get-AppxPackage -Name '{ShellPackageName}' | Remove-AppxPackage
Remove-Item '{Escape(@"HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\MD3Zip")}' -Recurse -Force
Remove-Item '{Escape(StartMenuDirectory)}' -Recurse -Force
Remove-Item '{Escape(desktopLink)}' -Force
Start-Process cmd.exe -ArgumentList '/c', 'timeout /t 2 /nobreak >nul & rmdir /s /q ""{Escape(target)}""' -WindowStyle Hidden
";

        File.WriteAllText(scriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static void RegisterUninstall(string target)
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath, writable: true)
            ?? throw new InvalidOperationException(L10n.L("无法写入卸载注册表项（需要管理员权限）。"));

        var exe = Path.Combine(target, AppExeName);
        var scriptPath = Path.Combine(target, UninstallScriptName);
        var uninstallCommand =
            $"powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"";

        key.SetValue("DisplayName", ProductName);
        key.SetValue("DisplayVersion", ProductVersion);
        key.SetValue("Publisher", ProductName);
        key.SetValue("DisplayIcon", exe);
        key.SetValue("InstallLocation", target);
        key.SetValue("UninstallString", uninstallCommand);
        key.SetValue("QuietUninstallString", uninstallCommand);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", EstimateSizeKb(target), RegistryValueKind.DWord);
    }

    /// <summary>
    /// 可选步骤：导入证书并注册稀疏包。
    /// 证书要写入 LocalMachine 存储，因此这一步单独弹一次 UAC；失败不影响主程序可用。
    /// </summary>
    private static void TryRegisterContextMenu(string target)
    {
        var msix = Path.Combine(target, "MD3Zip.Shell.msix");
        var cer = Path.Combine(target, "MD3ZipDev.cer");
        if (!File.Exists(msix) || !File.Exists(cer))
        {
            return;
        }

        var script = $@"
$ErrorActionPreference = 'Stop'
Import-Certificate -FilePath '{Escape(cer)}' -CertStoreLocation 'Cert:\LocalMachine\Root' | Out-Null
Import-Certificate -FilePath '{Escape(cer)}' -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
$existing = Get-AppxPackage -Name '{ShellPackageName}' -ErrorAction SilentlyContinue
if ($existing) {{ Remove-AppxPackage -Package $existing.PackageFullName -ErrorAction SilentlyContinue }}
Add-AppxPackage -Path '{Escape(msix)}' -ExternalLocation '{Escape(target)}'
";

        RunPowerShellElevated(script);
    }

    private static string Escape(string value) => value.Replace("'", "''");

    /// <summary>提权执行 PowerShell 脚本（-EncodedCommand 规避路径引号/中文问题）。</summary>
    private static int RunPowerShellElevated(string script)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}"
        };

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return -1;
            }

            process.WaitForExit(180_000);
            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // 用户在 UAC 提示上选择了「否」。
            return -3;
        }
        catch (Exception)
        {
            return -1;
        }
    }

    // ------------------------------------------------------------------
    // 卸载
    // ------------------------------------------------------------------
    public static void Uninstall(Action<InstallProgress>? report = null)
    {
        var target = GetInstalledDirectory();

        report?.Invoke(new(L10n.L("正在移除右键菜单…"), 10));
        RunPowerShell(
            $"$p = Get-AppxPackage -Name '{ShellPackageName}' -ErrorAction SilentlyContinue\r\n" +
            "if ($p) { Remove-AppxPackage -Package $p.PackageFullName -ErrorAction SilentlyContinue }");

        report?.Invoke(new(L10n.L("正在删除快捷方式…"), 35));
        DeleteShortcuts();

        report?.Invoke(new(L10n.L("正在清理卸载注册表项…"), 55));
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, throwOnMissingSubKey: false);
        }
        catch (Exception)
        {
            // 注册表项不存在或无权访问时忽略，继续删文件。
        }

        report?.Invoke(new(L10n.L("正在删除程序文件…"), 75));
        if (!string.IsNullOrEmpty(target))
        {
            ScheduleDirectoryDelete(target);
        }

        report?.Invoke(new(L10n.L("卸载完成"), 100));
    }

    private static void DeleteShortcuts()
    {
        TryDelete(Path.Combine(StartMenuDirectory, $"{ProductName}.lnk"));
        TryDelete(Path.Combine(DesktopDirectory, $"{ProductName}.lnk"));

        try
        {
            if (Directory.Exists(StartMenuDirectory)
                && !Directory.EnumerateFileSystemEntries(StartMenuDirectory).Any())
            {
                Directory.Delete(StartMenuDirectory);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// 先尽力删除所有能删的文件，剩下的（含正在运行的 uninstall.exe）交给 cmd 延迟删除。
    /// </summary>
    private static void ScheduleDirectoryDelete(string directory)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                if (string.Equals(file, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                TryDelete(file);
            }

            foreach (var sub in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories)
                         .OrderByDescending(d => d.Length))
            {
                try
                {
                    Directory.Delete(sub);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }

        var psi = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("/c");
        psi.ArgumentList.Add($"timeout /t 2 /nobreak >nul & rmdir /s /q \"{directory}\"");

        try
        {
            Process.Start(psi);
        }
        catch (Exception)
        {
            // 延迟删除失败时，残留目录可由用户手动清理。
        }
    }

    // ------------------------------------------------------------------
    // 辅助
    // ------------------------------------------------------------------
    private static int RunPowerShell(string script)
    {
        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(script);

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return -1;
            }

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(180_000))
            {
                try { process.Kill(entireProcessTree: true); } catch (Exception) { }
                return -2;
            }

            _ = stdout.Result;
            _ = stderr.Result;
            return process.ExitCode;
        }
        catch (Exception)
        {
            return -1;
        }
    }

    private static int EstimateSizeKb(string directory)
    {
        try
        {
            var bytes = Directory
                .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Sum(path => new FileInfo(path).Length);
            return (int)Math.Min(int.MaxValue, bytes / 1024);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
