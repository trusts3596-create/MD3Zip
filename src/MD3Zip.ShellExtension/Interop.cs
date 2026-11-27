using System.Runtime.InteropServices;

namespace MD3Zip.ShellExtension;

/// <summary>
/// 右键菜单扩展使用到的 COM 互操作声明。
/// 注意：Win32 的 vtable 顺序必须与 Windows SDK 完全一致，
/// 因此这里按顺序声明了不使用但必须占位的方法。
/// </summary>
/// <summary>
/// GetFlags() 返回 EXPCMDFLAGS（shobjidl_core.h）——与 GetState() 的 EXPCMDSTATE 是两套枚举。
/// 旧实现把两者混在一起，还自造了 ECS_HASSUBCOMMANDS = 0x4；
/// 实际 0x4 是 ECF_HIDELABEL，Explorer 因此把一级项当成「无子菜单的普通命令」渲染，
/// 点击直接走 Invoke（旧实现为空操作）→ 表现为「右键点了打不开」。
/// </summary>
internal static class ExplorerCommandFlags
{
    public const uint ECF_DEFAULT = 0x000;
    public const uint ECF_HASSUBCOMMANDS = 0x001;
    public const uint ECF_HASSPLITBUTTON = 0x002;
    public const uint ECF_HIDELABEL = 0x004;
    public const uint ECF_ISSEPARATOR = 0x008;
    public const uint ECF_HASLUASHIELD = 0x010;
    public const uint ECF_SEPARATORBEFORE = 0x020;
    public const uint ECF_SEPARATORAFTER = 0x040;
    public const uint ECF_ISDROPDOWN = 0x080;
}

/// <summary>GetState() 返回 EXPCMDSTATE。</summary>
internal static class ExplorerCommandState
{
    public const uint ECS_ENABLED = 0x00;
    public const uint ECS_DISABLED = 0x01;
    public const uint ECS_HIDDEN = 0x02;
    public const uint ECS_CHECKBOX = 0x04;
    public const uint ECS_CHECKED = 0x08;
    public const uint ECS_RADIOCHECK = 0x10;
}

internal static class Sigdn
{
    public const uint FileSystemPath = 0x80058000;
}

[ComImport]
[Guid("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IExplorerCommand
{
    [PreserveSig]
    int GetTitle(IShellItemArray? psiItemArray, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);

    [PreserveSig]
    int GetIcon(IShellItemArray? psiItemArray, [MarshalAs(UnmanagedType.LPWStr)] out string ppszIcon);

    [PreserveSig]
    int GetToolTip(IShellItemArray? psiItemArray, [MarshalAs(UnmanagedType.LPWStr)] out string ppszInfotip);

    [PreserveSig]
    int GetCanonicalName(out Guid pguidCommandName);

    [PreserveSig]
    int GetState(IShellItemArray? psiItemArray, [MarshalAs(UnmanagedType.Bool)] bool fOkToBeSlow, out uint pCmdState);

    [PreserveSig]
    int Invoke(IShellItemArray? psiItemArray, IntPtr pbc);

    [PreserveSig]
    int GetFlags(out uint pFlags);

    [PreserveSig]
    int EnumSubCommands(out IEnumExplorerCommand? ppEnum);
}

[ComImport]
[Guid("a88826f8-186f-4987-aade-ea0cef8fbfe8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IEnumExplorerCommand
{
    [PreserveSig]
    int Next(uint celt, IntPtr rgelt, out uint pceltFetched);

    [PreserveSig]
    int Skip(uint celt);

    [PreserveSig]
    int Reset();

    [PreserveSig]
    int Clone(out IEnumExplorerCommand ppenum);
}

[ComImport]
[Guid("b63ea76d-1f85-456f-a19c-48159efa858b")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellItemArray
{
    [PreserveSig]
    int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppvOut);

    [PreserveSig]
    int GetPropertyStore(int flags, ref Guid riid, out IntPtr ppv);

    [PreserveSig]
    int GetPropertyDescriptionList(IntPtr keyType, ref Guid riid, out IntPtr ppv);

    [PreserveSig]
    int GetAttributes(uint attribFlags, uint sfgaoMask, out uint psfgaoAttribs);

    [PreserveSig]
    int GetCount(out uint pdwNumItems);

    [PreserveSig]
    int GetItemAt(uint dwIndex, out IShellItem? ppsi);

    [PreserveSig]
    int EnumItems(out IntPtr ppenumShellItems);
}

[ComImport]
[Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellItem
{
    [PreserveSig]
    int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppvOut);

    [PreserveSig]
    int GetParent(out IShellItem? ppsi);

    [PreserveSig]
    int GetDisplayName(uint sigdnName, out IntPtr ppszName);

    [PreserveSig]
    int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);

    [PreserveSig]
    int Compare(IShellItem psi, uint hint, out int piOrder);

    [PreserveSig]
    int IsEqual(IShellItem psi, out int pfEqual);
}
