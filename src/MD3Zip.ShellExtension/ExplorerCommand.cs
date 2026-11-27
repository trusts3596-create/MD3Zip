using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MD3Zip.ShellExtension;

internal static class CommandIds
{
    /// <summary>
    /// 一级菜单命令的 COM CLSID。必须与 packaging/AppxManifest.xml 中的
    /// com:Class Id 以及 desktop4:Verb Clsid 完全一致。
    /// </summary>
    public const string RootCommandClsid = "B2A6F3C1-9D7E-4A21-8C3D-5E6F7A8B9C01";

    public static readonly Guid RootCommand = new(RootCommandClsid);
}

internal enum ShellAction
{
    ExtractHere,
    SmartExtract,
    CompressDialog
}

internal static class HResults
{
    public const int S_OK = 0;
    public const int S_FALSE = 1;
    public const int E_NOTIMPL = unchecked((int)0x80004001);
    public const int E_FAIL = unchecked((int)0x80004005);
}

/// <summary>
/// 定位主程序与图标：稀疏包的 ExternalLocation 会把两个可执行文件放在同一目录。
/// </summary>
internal static class AppLauncher
{
    private const string AppFileName = "MD3Zip.App.exe";

    /// <summary>
    /// 关键修正：COM 服务器由 dllhost 承载时，AppContext.BaseDirectory 指向 System32
    /// 而不是发布目录，因此必须用托管程序集自身的位置来定位主程序与图标。
    /// </summary>
    public static string BaseDirectory
    {
        get
        {
            var location = typeof(AppLauncher).Assembly.Location;
            if (!string.IsNullOrEmpty(location))
            {
                return Path.GetDirectoryName(location)!;
            }

            return AppContext.BaseDirectory;
        }
    }

    public static string AppPath => Path.Combine(BaseDirectory, AppFileName);

    public static string IconLocation
    {
        get
        {
            var ico = Path.Combine(BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(ico))
            {
                return ico;
            }

            return File.Exists(AppPath) ? AppPath : string.Empty;
        }
    }

    /// <summary>
    /// 主日志固定在发布目录（包外真实路径）。
    /// 关键坑：COM 服务器运行在 MSIX 包上下文中，%LOCALAPPDATA% 会被虚拟化重定向到
    /// %LOCALAPPDATA%\Packages\&lt;PackageFamilyName&gt;\LocalCache\Local，
    /// 只写 LocalAppData 会让排障时在常规路径下永远找不到日志，误判成「COM 从未激活」。
    /// </summary>
    public static string LogFile => Path.Combine(BaseDirectory, "logs", "shell.log");

    /// <summary>右键菜单的调用日志（排查“点了没反应”类问题）。</summary>
    public static void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}";

        if (TryAppend(LogFile, line))
        {
            return;
        }

        // 发布目录不可写时退回 LocalAppData（注意此时路径会被包虚拟化重定向）。
        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MD3Zip", "shell.log");
        TryAppend(fallback, line);
    }

    private static bool TryAppend(string file, string line)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.AppendAllText(file, line, Encoding.UTF8);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (ArgumentException) { return false; }
    }

    private static int _environmentLogged;

    /// <summary>每次 COM 服务器进程首次执行动作时记录一次环境，便于定位路径问题。</summary>
    private static void LogEnvironmentOnce()
    {
        if (Interlocked.Exchange(ref _environmentLogged, 1) != 0)
        {
            return;
        }

        Log($"COM 环境: 进程={Process.GetCurrentProcess().ProcessName}, " +
            $"BaseDirectory={BaseDirectory}, AppContext.BaseDirectory={AppContext.BaseDirectory}, " +
            $"AppPath={AppPath}, 主程序存在={File.Exists(AppPath)}");
    }

    public static int Launch(ShellAction action, IReadOnlyList<string> paths)
    {
        LogEnvironmentOnce();
        if (!File.Exists(AppPath))
        {
            Log($"主程序不存在: {AppPath}");
            return HResults.E_FAIL;
        }

        var arguments = new StringBuilder();
        switch (action)
        {
            case ShellAction.ExtractHere:
                arguments.Append("--extract-here ").Append(Quote(paths.FirstOrDefault() ?? string.Empty));
                break;

            case ShellAction.SmartExtract:
                arguments.Append("--smart-extract ").Append(Quote(paths.FirstOrDefault() ?? string.Empty));
                break;

            case ShellAction.CompressDialog:
                arguments.Append("--compress-dialog");
                foreach (var path in paths)
                {
                    arguments.Append(' ').Append(Quote(path));
                }

                break;
        }

        try
        {
            var process = Process.Start(new ProcessStartInfo(AppPath)
            {
                Arguments = arguments.ToString(),
                UseShellExecute = false,
                WorkingDirectory = BaseDirectory
            });
            Log($"已启动 {action}: {AppPath} {arguments}（pid={process?.Id}）");
            return HResults.S_OK;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            Log($"启动失败 {action}: {ex.GetType().Name} {ex.Message}");
            return HResults.E_FAIL;
        }
    }

    /// <summary>Windows 命令行参数转义：处理引号与结尾反斜杠。</summary>
    private static string Quote(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        var builder = new StringBuilder("\"");
        foreach (var ch in value)
        {
            if (ch == '"')
            {
                builder.Append('\\').Append('"');
            }
            else
            {
                builder.Append(ch);
            }
        }

        if (value.EndsWith('\\'))
        {
            builder.Append('\\');
        }

        return builder.Append('"').ToString();
    }
}

internal static class ShellSelection
{
    public static IReadOnlyList<string> GetFileSystemPaths(IShellItemArray? items)
    {
        if (items is null)
        {
            return [];
        }

        var paths = new List<string>();
        try
        {
            if (items.GetCount(out var count) != HResults.S_OK)
            {
                return paths;
            }

            for (uint i = 0; i < count; i++)
            {
                if (items.GetItemAt(i, out var item) != HResults.S_OK || item is null)
                {
                    continue;
                }

                try
                {
                    if (item.GetDisplayName(Sigdn.FileSystemPath, out var buffer) == HResults.S_OK && buffer != IntPtr.Zero)
                    {
                        var path = Marshal.PtrToStringUni(buffer);
                        Marshal.FreeCoTaskMem(buffer);
                        if (!string.IsNullOrEmpty(path))
                        {
                            paths.Add(path);
                        }
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(item);
                }
            }
        }
        catch (COMException)
        {
            // 选择项不可访问时返回已解析的部分结果。
        }

        return paths;
    }
}

/// <summary>
/// 一级菜单：折叠式 “MD3 Zip”，通过 EnumSubCommands 展开三个动作。
/// </summary>
[ComVisible(true)]
[Guid(CommandIds.RootCommandClsid)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class RootExplorerCommand : IExplorerCommand
{
    public int GetTitle(IShellItemArray? psiItemArray, out string ppszName)
    {
        ppszName = "MD3 Zip";
        return HResults.S_OK;
    }

    public int GetIcon(IShellItemArray? psiItemArray, out string ppszIcon)
    {
        ppszIcon = AppLauncher.IconLocation;
        return HResults.S_OK;
    }

    public int GetToolTip(IShellItemArray? psiItemArray, out string ppszInfotip)
    {
        ppszInfotip = "使用 MD3 Zip 压缩或解压";
        return HResults.S_OK;
    }

    public int GetCanonicalName(out Guid pguidCommandName)
    {
        pguidCommandName = CommandIds.RootCommand;
        return HResults.S_OK;
    }

    public int GetState(IShellItemArray? psiItemArray, bool fOkToBeSlow, out uint pCmdState)
    {
        pCmdState = ExplorerCommandState.ECS_ENABLED;
        return HResults.S_OK;
    }

    public int Invoke(IShellItemArray? psiItemArray, IntPtr pbc)
    {
        // 兜底：部分系统/Explorer 版本不会展开折叠项，点击会直接走 Invoke。
        // 给一级项一个默认动作（打开压缩对话框），保证「点了一级项也一定能打开」。
        // 子菜单正常展开时不会调用 Invoke，因此不影响正常路径。
        var paths = ShellSelection.GetFileSystemPaths(psiItemArray);
        AppLauncher.Log($"点击一级菜单「MD3 Zip」，选中 {paths.Count} 项 → 兜底打开压缩对话框");
        return AppLauncher.Launch(ShellAction.CompressDialog, paths);
    }

    public int GetFlags(out uint pFlags)
    {
        pFlags = ExplorerCommandFlags.ECF_HASSUBCOMMANDS;
        return HResults.S_OK;
    }

    public int EnumSubCommands(out IEnumExplorerCommand? ppEnum)
    {
        IExplorerCommand[] commands =
        [
            new ActionExplorerCommand("解压到当前文件夹", ShellAction.ExtractHere),
            new ActionExplorerCommand("智能解压", ShellAction.SmartExtract),
            new ActionExplorerCommand("添加到压缩包…", ShellAction.CompressDialog)
        ];

        ppEnum = new EnumExplorerCommand(commands);
        return HResults.S_OK;
    }
}

/// <summary>二级动作命令（无子命令）。</summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class ActionExplorerCommand : IExplorerCommand
{
    private readonly string _title;
    private readonly ShellAction _action;

    public ActionExplorerCommand(string title, ShellAction action)
    {
        _title = title;
        _action = action;
    }

    public int GetTitle(IShellItemArray? psiItemArray, out string ppszName)
    {
        ppszName = _title;
        return HResults.S_OK;
    }

    public int GetIcon(IShellItemArray? psiItemArray, out string ppszIcon)
    {
        ppszIcon = AppLauncher.IconLocation;
        return HResults.S_OK;
    }

    public int GetToolTip(IShellItemArray? psiItemArray, out string ppszInfotip)
    {
        ppszInfotip = _title;
        return HResults.S_OK;
    }

    public int GetCanonicalName(out Guid pguidCommandName)
    {
        pguidCommandName = Guid.Empty;
        return HResults.S_OK;
    }

    public int GetState(IShellItemArray? psiItemArray, bool fOkToBeSlow, out uint pCmdState)
    {
        pCmdState = ExplorerCommandState.ECS_ENABLED;
        return HResults.S_OK;
    }

    public int Invoke(IShellItemArray? psiItemArray, IntPtr pbc)
    {
        var paths = ShellSelection.GetFileSystemPaths(psiItemArray);
        AppLauncher.Log($"Invoke {_action}，选中 {paths.Count} 项");

        if ((_action is ShellAction.ExtractHere or ShellAction.SmartExtract) && paths.Count == 0)
        {
            AppLauncher.Log("没有可用的文件路径，忽略本次调用");
            return HResults.E_FAIL;
        }

        return AppLauncher.Launch(_action, paths);
    }

    public int GetFlags(out uint pFlags)
    {
        pFlags = ExplorerCommandFlags.ECF_DEFAULT;
        return HResults.S_OK;
    }

    public int EnumSubCommands(out IEnumExplorerCommand? ppEnum)
    {
        ppEnum = null;
        return HResults.E_NOTIMPL;
    }
}

/// <summary>
/// IEnumExplorerCommand 实现：返回动作命令的 COM 接口指针。
/// 使用 Marshal.GetComInterfaceForObject 为托管对象创建 CCW，
/// 避免在 SurrogateServer 中额外注册每个子命令的 CLSID。
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class EnumExplorerCommand : IEnumExplorerCommand
{
    private readonly IExplorerCommand[] _commands;
    private int _index;

    public EnumExplorerCommand(IExplorerCommand[] commands) => _commands = commands;

    public int Next(uint celt, IntPtr rgelt, out uint pceltFetched)
    {
        pceltFetched = 0;
        if (rgelt == IntPtr.Zero)
        {
            return HResults.E_FAIL;
        }

        for (var i = 0u; i < celt && _index < _commands.Length; i++, _index++)
        {
            var pointer = Marshal.GetComInterfaceForObject(_commands[_index], typeof(IExplorerCommand));
            Marshal.WriteIntPtr(rgelt, (int)i * IntPtr.Size, pointer);
            pceltFetched++;
        }

        return pceltFetched == celt ? HResults.S_OK : HResults.S_FALSE;
    }

    public int Skip(uint celt)
    {
        _index = Math.Min(_commands.Length, _index + (int)celt);
        return HResults.S_OK;
    }

    public int Reset()
    {
        _index = 0;
        return HResults.S_OK;
    }

    public int Clone(out IEnumExplorerCommand ppenum)
    {
        var clone = new EnumExplorerCommand(_commands) { _index = _index };
        ppenum = clone;
        return HResults.S_OK;
    }
}
