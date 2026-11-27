namespace MD3Zip.Core.Models;

public enum ArchiveFormat
{
    SevenZip,
    Zip,
    Tar,

    /// <summary>单文件格式：只能压缩一个文件，对应 7z 的 -tgzip。</summary>
    GZip,

    /// <summary>单文件格式：对应 -tbzip2。</summary>
    BZip2,

    /// <summary>单文件格式：对应 -txz。</summary>
    Xz
}

public enum CompressionLevel
{
    Fastest = 1,
    Fast = 3,
    Normal = 5,
    Maximum = 7,
    Ultra = 9
}

/// <summary>解压时目标文件已存在的处理方式，对应 7z 的 -ao* 开关。</summary>
public enum ExtractionOverwriteMode
{
    /// <summary>-aoa：直接覆盖。</summary>
    Overwrite,

    /// <summary>-aos：跳过已存在的文件。</summary>
    Skip,

    /// <summary>-aou：自动重命名（file.txt → file_1.txt）。</summary>
    Rename
}

/// <summary>
/// 压缩请求。所有参数最终映射到 7z.exe 的命令行开关。
/// </summary>
public sealed class CompressionRequest
{
    public List<string> Sources { get; init; } = [];

    /// <summary>输出归档的绝对路径（含文件名）。</summary>
    public required string OutputPath { get; set; }

    public ArchiveFormat Format { get; set; } = ArchiveFormat.SevenZip;

    public CompressionLevel Level { get; set; } = CompressionLevel.Normal;

    /// <summary>可选密码；zip 使用 AES-256，7z 使用 AES-256。</summary>
    public string? Password { get; set; }

    /// <summary>仅在 7z 格式下生效：同时加密文件名（-mhe=on）。</summary>
    public bool EncryptFileNames { get; set; } = true;

    /// <summary>分卷大小，例如 "100m"、"1g"、"700k"。为空表示不分卷。</summary>
    public string? VolumeSize { get; set; }

    /// <summary>多线程压缩线程数（0 表示 7z 默认）。</summary>
    public int Threads { get; set; }

    /// <summary>压缩成功后删除源文件 / 文件夹（对应 7-Zip 的 "Delete files after compression"）。</summary>
    public bool DeleteSourcesAfter { get; set; }

    public string? WorkingDirectory { get; set; }

    public string FormatSwitch => Format switch
    {
        ArchiveFormat.Zip => "zip",
        ArchiveFormat.Tar => "tar",
        ArchiveFormat.GZip => "gzip",
        ArchiveFormat.BZip2 => "bzip2",
        ArchiveFormat.Xz => "xz",
        _ => "7z"
    };

    /// <summary>gz / bz2 / xz 是流式单文件格式，只能压缩一个文件。</summary>
    public bool IsSingleFileFormat => Format is ArchiveFormat.GZip or ArchiveFormat.BZip2 or ArchiveFormat.Xz;

    /// <summary>tar 为纯打包格式，不支持压缩等级与加密。</summary>
    public bool SupportsLevel => Format != ArchiveFormat.Tar;

    /// <summary>7z / zip 才支持密码；tar / gzip / bzip2 / xz 不支持。</summary>
    public bool SupportsEncryption => Format is ArchiveFormat.SevenZip or ArchiveFormat.Zip;

    /// <summary>只有 7z / zip 支持分卷。</summary>
    public bool SupportsVolume => Format is ArchiveFormat.SevenZip or ArchiveFormat.Zip;
}

/// <summary>
/// 解压请求（可携带“仅解压选中条目”的清单）。
/// </summary>
public sealed class ExtractionRequest
{
    public required string ArchivePath { get; set; }

    public required string TargetDirectory { get; set; }

    public string? Password { get; set; }

    /// <summary>内部相对路径清单；为空表示整个压缩包。</summary>
    public List<string> SelectedEntries { get; init; } = [];

    /// <summary>目标已存在时的处理方式（-aoa / -aos / -aou）。</summary>
    public ExtractionOverwriteMode OverwriteMode { get; set; } = ExtractionOverwriteMode.Overwrite;
}

/// <summary>
/// 向已有压缩包追加 / 更新条目的请求（7z u）。
/// </summary>
public sealed class UpdateRequest
{
    public required string ArchivePath { get; set; }

    public List<string> Sources { get; init; } = [];

    public CompressionLevel Level { get; set; } = CompressionLevel.Normal;

    public string? Password { get; set; }

    public string? WorkingDirectory { get; set; }
}

/// <summary>校验和算法（映射到 7z h 的 -scrc* 开关）。</summary>
public enum HashAlgorithmKind
{
    Crc32,
    Sha1,
    Sha256
}

/// <summary>一次校验和计算的结果。</summary>
public sealed class HashResult
{
    public bool Succeeded { get; init; }

    public HashAlgorithmKind Algorithm { get; init; }

    /// <summary>十六进制摘要；失败时为 null。</summary>
    public string? Hash { get; init; }

    /// <summary>被校验文件的大小（字节）。</summary>
    public long Size { get; init; }

    public string Message { get; init; } = string.Empty;

    public string RawOutput { get; init; } = string.Empty;

    public string AlgorithmName => Algorithm switch
    {
        HashAlgorithmKind.Crc32 => "CRC32",
        HashAlgorithmKind.Sha1 => "SHA-1",
        _ => "SHA-256"
    };
}

/// <summary>
/// 智能解压的决策结果，便于 UI 展示“为什么解压到了这里”。
/// </summary>
public sealed class SmartExtractPlan
{
    public required string ArchivePath { get; set; }

    public required string TargetDirectory { get; set; }

    /// <summary>true 表示为避免套娃，直接解压到父目录。</summary>
    public bool ExtractedDirectly { get; set; }

    /// <summary>当需要新建子目录时，子目录名称。</summary>
    public string? SubfolderName { get; set; }

    public string Reason { get; set; } = string.Empty;
}
