using System.Diagnostics;

namespace MD3Zip.App.Services;

public static class ShellService
{
    /// <summary>用系统默认程序打开文件或文件夹。</summary>
    public static void Open(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            // 没有关联程序时静默失败，由调用方提示。
        }
    }

    /// <summary>在资源管理器中定位文件（或打开文件夹）。</summary>
    public static void Reveal(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
                return;
            }

            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            // 忽略：资源管理器不可用时不做处理。
        }
    }
}
