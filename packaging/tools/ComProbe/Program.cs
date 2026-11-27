using System.Runtime.InteropServices;

namespace ComProbe;

/// <summary>
/// 独立诊断工具：直接从 COM 运行时激活 MD3Zip 的 IExplorerCommand CLSID，
/// 完整走一遍「菜单构建 → 子命令枚举 → Invoke」链路，定位「右键点击无反应」。
/// 用法：ComProbe.exe [--invoke-compress]
/// </summary>
internal static class Native
{
    [DllImport("ole32.dll")]
    public static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

    [DllImport("ole32.dll")]
    public static extern int CoCreateInstance(
        ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid, out IntPtr ppv);

    [DllImport("ole32.dll")]
    public static extern void CoUninitialize();
}

[ComImport]
[Guid("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IExplorerCommandRaw
{
    [PreserveSig] int GetTitle(IntPtr psiItemArray, out IntPtr ppszName);
    [PreserveSig] int GetIcon(IntPtr psiItemArray, out IntPtr ppszIcon);
    [PreserveSig] int GetToolTip(IntPtr psiItemArray, out IntPtr ppszInfotip);
    [PreserveSig] int GetCanonicalName(out Guid pguidCommandName);
    [PreserveSig] int GetState(IntPtr psiItemArray, [MarshalAs(UnmanagedType.Bool)] bool fOkToBeSlow, out uint pCmdState);
    [PreserveSig] int Invoke(IntPtr psiItemArray, IntPtr pbc);
    [PreserveSig] int GetFlags(out uint pFlags);
    [PreserveSig] int EnumSubCommands(out IntPtr ppEnum);
}

[ComImport]
[Guid("a88826f8-186f-4987-aade-ea0cef8fbfe8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEnumExplorerCommandRaw
{
    [PreserveSig] int Next(uint celt, IntPtr rgelt, out uint pceltFetched);
    [PreserveSig] int Skip(uint celt);
    [PreserveSig] int Reset();
    [PreserveSig] int Clone(out IntPtr ppenum);
}

internal static class Program
{
    private const string ClsidText = "B2A6F3C1-9D7E-4A21-8C3D-5E6F7A8B9C01";
    private const uint ClsctxLocalServer = 0x4;
    private const uint CoinitApartmentThreaded = 0x2;

    private static int Main(string[] args)
    {
        var invokeCompress = args.Contains("--invoke-compress", StringComparer.OrdinalIgnoreCase);
        Console.WriteLine("=== MD3 Zip 右键菜单 COM 全链路探测 ===");
        Native.CoInitializeEx(IntPtr.Zero, CoinitApartmentThreaded);

        var clsid = new Guid(ClsidText);
        var iid = new Guid("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9");
        var hr = Native.CoCreateInstance(ref clsid, IntPtr.Zero, ClsctxLocalServer, ref iid, out var pointer);
        Console.WriteLine($"CoCreateInstance = 0x{hr:X8} ({Describe(hr)})");

        if (hr != 0 || pointer == IntPtr.Zero)
        {
            Native.CoUninitialize();
            return 1;
        }

        try
        {
            var command = (IExplorerCommandRaw)Marshal.GetTypedObjectForIUnknown(pointer, typeof(IExplorerCommandRaw));
            ProbeMenu(command);

            if (args.Contains("--invoke-root", StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine("\n-- 模拟直接点击一级菜单「MD3 Zip」（验证兜底动作） --");
                var rootHr = command.Invoke(IntPtr.Zero, IntPtr.Zero);
                Console.WriteLine($"Invoke(一级) = 0x{rootHr:X8}");
                Thread.Sleep(800);
                Console.WriteLine($"MD3Zip.App 进程数: {System.Diagnostics.Process.GetProcessesByName("MD3Zip.App").Length}");
            }

            var enumHr = command.EnumSubCommands(out var enumPtr);
            Console.WriteLine($"\nEnumSubCommands = 0x{enumHr:X8}, ptr=0x{enumPtr:X}");
            if (enumHr != 0 || enumPtr == IntPtr.Zero)
            {
                Console.WriteLine("!! 子命令枚举失败：右键只会看到一个无子菜单的项，点击后必然无反应。");
                return 2;
            }

            var buffer = Marshal.AllocCoTaskMem(IntPtr.Size * 8);
            for (var i = 0; i < 8; i++) Marshal.WriteIntPtr(buffer, i * IntPtr.Size, IntPtr.Zero);
            var enumCmd = (IEnumExplorerCommandRaw)Marshal.GetTypedObjectForIUnknown(enumPtr, typeof(IEnumExplorerCommandRaw));
            var nextHr = enumCmd.Next(8, buffer, out var fetched);
            Console.WriteLine($"IEnumExplorerCommand::Next = 0x{nextHr:X8}, fetched={fetched}");

            var childPointers = new List<IntPtr>();
            for (var i = 0u; i < fetched; i++)
            {
                var childPtr = Marshal.ReadIntPtr(buffer, (int)i * IntPtr.Size);
                childPointers.Add(childPtr);
                if (childPtr == IntPtr.Zero)
                {
                    Console.WriteLine($"  [{i}] 空指针 !!");
                    continue;
                }

                try
                {
                    var child = (IExplorerCommandRaw)Marshal.GetTypedObjectForIUnknown(childPtr, typeof(IExplorerCommandRaw));
                    var titleHr = child.GetTitle(IntPtr.Zero, out var titlePtr);
                    var title = titlePtr == IntPtr.Zero ? "(null)" : Marshal.PtrToStringUni(titlePtr) ?? "(null)";
                    var stateHr = child.GetState(IntPtr.Zero, false, out var state);
                    Console.WriteLine($"  [{i}] \"{title}\"  (GetTitle=0x{titleHr:X8}, GetState=0x{stateHr:X8}/0x{state:X})");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  [{i}] 读取子命令异常: {ex.GetType().Name}: {ex.Message}");
                }
            }

            Marshal.FreeCoTaskMem(buffer);

            if (invokeCompress && childPointers.Count >= 3 && childPointers[2] != IntPtr.Zero)
            {
                Console.WriteLine("\n-- 模拟点击「添加到压缩包…」 --");
                try
                {
                    var compress = (IExplorerCommandRaw)Marshal.GetTypedObjectForIUnknown(childPointers[2], typeof(IExplorerCommandRaw));
                    var invokeHr = compress.Invoke(IntPtr.Zero, IntPtr.Zero);
                    Console.WriteLine($"Invoke = 0x{invokeHr:X8} (0=成功启动主程序)");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Invoke 异常: {ex.GetType().Name}: {ex.Message}");
                }

                Thread.Sleep(800);
                var procs = System.Diagnostics.Process.GetProcessesByName("MD3Zip.App");
                Console.WriteLine($"当前 MD3Zip.App 进程数: {procs.Length}");
                // 首选发布目录（包外真实路径）；另外两个是历史/包虚拟化重定向路径。
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var candidates = new[]
                {
                    Path.Combine(@"D:\wb\packaging\publish\app", "logs", "shell.log"),
                    Path.Combine(localAppData, "MD3Zip", "shell.log"),
                    Path.Combine(localAppData, "Packages", "MD3Zip.Shell_ps1av0evqpwje",
                        "LocalCache", "Local", "MD3Zip", "shell.log")
                };
                var found = false;
                foreach (var logPath in candidates)
                {
                    if (!File.Exists(logPath)) continue;
                    Console.WriteLine($"--- shell.log: {logPath} ---");
                    foreach (var line in File.ReadAllLines(logPath).TakeLast(8)) Console.WriteLine("  " + line);
                    found = true;
                    break;
                }

                if (!found) Console.WriteLine("!! 未找到 shell.log（Invoke 可能未到达 ShellExtension）");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"探测异常: {ex}");
            return 3;
        }
        finally
        {
            Marshal.Release(pointer);
            Native.CoUninitialize();
        }

        Console.WriteLine("\n=== 探测结束 ===");
        Console.WriteLine("参考：0x80040154=CLASSNOTREG / 0x80070005=拒绝访问(签名) / 0x80080005=服务器启动失败");
        return 0;
    }

    private static void ProbeMenu(IExplorerCommandRaw command)
    {
        command.GetTitle(IntPtr.Zero, out var titlePtr);
        Console.WriteLine($"GetTitle  = \"{Str(titlePtr)}\"");

        command.GetIcon(IntPtr.Zero, out var iconPtr);
        var icon = Str(iconPtr);
        Console.WriteLine($"GetIcon   = \"{icon}\"");
        if (!string.IsNullOrEmpty(icon))
        {
            var dir = Path.GetDirectoryName(icon);
            Console.WriteLine($"           推断 BaseDirectory = {dir}");
            Console.WriteLine($"           主程序存在: {File.Exists(Path.Combine(dir ?? string.Empty, "MD3Zip.App.exe"))}");
        }

        command.GetToolTip(IntPtr.Zero, out var tipPtr);
        Console.WriteLine($"GetToolTip= \"{Str(tipPtr)}\"");
        command.GetCanonicalName(out var canonical);
        Console.WriteLine($"Canonical = {canonical}");
        command.GetFlags(out var flags);
        Console.WriteLine($"GetFlags  = 0x{flags:X8} (EXPCMDFLAGS: 0x1=HASSUBCOMMANDS, 0x4=HIDELABEL)");
        Console.WriteLine((flags & 0x1) != 0
            ? "           OK: 已声明子菜单，Explorer 应展开折叠项"
            : "           !! 缺少 ECF_HASSUBCOMMANDS：一级项会被当成普通命令，点击只触发 Invoke");
        command.GetState(IntPtr.Zero, false, out var state);
        Console.WriteLine($"GetState  = 0x{state:X8} (0=ENABLED)");
    }

    private static string Str(IntPtr ptr) => ptr == IntPtr.Zero ? "(null)" : Marshal.PtrToStringUni(ptr) ?? "(null)";

    private static string Describe(int hr) => (uint)hr switch
    {
        0 => "S_OK",
        0x80040154 => "REGDB_E_CLASSNOTREG 注册未生效",
        0x80040111 => "CLASS_E_CLASSNOTAVAILABLE",
        0x80070005 => "E_ACCESSDENIED 拒绝访问（多为签名/权限）",
        0x80004005 => "E_FAIL",
        0x80080005 => "CO_E_SERVER_EXEC_FAILURE 服务器启动失败",
        _ => "其它错误"
    };
}
