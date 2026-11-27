namespace MD3Zip.Core.Models;

/// <summary>
/// 一次 7z 进程运行过程中的实时进度快照。
/// </summary>
public sealed class ArchiveProgress
{
    /// <summary>0-100 的百分比；-1 表示不确定进度（例如刚启动、或调用了非 -bsp1 命令）。</summary>
    public double Percent { get; init; } = -1;

    /// <summary>7z 当前正在处理的条目（可能是相对路径）。</summary>
    public string? CurrentFile { get; init; }

    /// <summary>7z 已处理的条目数量（-bsp1 输出的中间数字）。</summary>
    public long? ProcessedEntries { get; init; }

    /// <summary>操作描述，例如“正在解压”“正在压缩”。</summary>
    public string? Operation { get; init; }

    /// <summary>7z 报告的总条目数（若能推算）。</summary>
    public long? TotalEntries { get; init; }

    public bool IsIndeterminate => Percent < 0;

    public static ArchiveProgress Indeterminate(string? operation = null) => new() { Percent = -1, Operation = operation };
}

/// <summary>
/// 7z 退出码与错误语义的结构化映射。
/// </summary>
public enum SevenZipStatus
{
    Success = 0,
    Warning = 1,
    Cancelled = 2,
    /// <summary>归档已加密、但调用方没有提供密码（需要弹窗索要密码）。</summary>
    PasswordRequired = 9,
    WrongPassword = 3,
    DiskFull = 4,
    AccessDenied = 5,
    FileNotFound = 6,
    NotAnArchive = 7,
    EngineMissing = 8,
    Unknown = 99
}

public sealed class SevenZipResult
{
    public SevenZipStatus Status { get; init; } = SevenZipStatus.Unknown;
    public int ExitCode { get; init; }
    public string Message { get; init; } = string.Empty;
    public string RawOutput { get; init; } = string.Empty;

    public bool Succeeded => Status is SevenZipStatus.Success or SevenZipStatus.Warning;

    public static SevenZipResult Ok(int exitCode = 0, string? message = null) =>
        new() { Status = SevenZipStatus.Success, ExitCode = exitCode, Message = message ?? L10n.L("操作完成") };

    public static SevenZipResult Fail(SevenZipStatus status, string message, int exitCode = -1, string raw = "") =>
        new() { Status = status, Message = message, ExitCode = exitCode, RawOutput = raw };
}
