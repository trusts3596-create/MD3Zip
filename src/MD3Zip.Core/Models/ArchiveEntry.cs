using System.Globalization;

namespace MD3Zip.Core.Models;

/// <summary>
/// 压缩包内部的一个条目（文件或文件夹）。路径统一使用 '/' 分隔。
/// </summary>
public sealed class ArchiveEntry
{
    public string Path { get; init; } = string.Empty;

    /// <summary>末级名称，用于列表显示。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>父级内部路径，根目录为 ""。</summary>
    public string ParentPath { get; init; } = string.Empty;

    public bool IsDirectory { get; init; }

    public long Size { get; init; }

    public long PackedSize { get; init; }

    public DateTime? Modified { get; init; }

    public string Attributes { get; init; } = string.Empty;

    public bool IsEncrypted { get; init; }

    /// <summary>层级深度：根目录下的条目为 1。</summary>
    public int Depth { get; init; }

    /// <summary>是否为目录树构建过程中合成出来的空目录（压缩包未显式记录）。</summary>
    public bool IsSynthesized { get; init; }

    public string DisplaySize => IsDirectory ? "—" : FormatSize(Size);

    public string DisplayModified => Modified?.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture) ?? "—";

    public string Extension
    {
        get
        {
            if (IsDirectory)
            {
                return string.Empty;
            }

            var ext = System.IO.Path.GetExtension(Name);
            return string.IsNullOrEmpty(ext) ? string.Empty : ext.TrimStart('.').ToLowerInvariant();
        }
    }

    public static string FormatSize(long bytes)
    {
        if (bytes < 0)
        {
            return "—";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes} B"
            : $"{value:0.##} {units[unit]}";
    }

    internal static ArchiveEntry CreateDirectory(string path, bool synthesized)
    {
        var normalized = ArchivePathUtil.Normalize(path);
        return new ArchiveEntry
        {
            Path = normalized,
            Name = ArchivePathUtil.GetName(normalized),
            ParentPath = ArchivePathUtil.GetParent(normalized),
            IsDirectory = true,
            Depth = ArchivePathUtil.GetDepth(normalized),
            IsSynthesized = synthesized
        };
    }
}

/// <summary>
/// 压缩包内部路径的规范化工具。
/// </summary>
public static class ArchivePathUtil
{
    public static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var value = path.Replace('\\', '/').Trim();
        while (value.StartsWith("./", StringComparison.Ordinal))
        {
            value = value[2..];
        }

        value = value.Trim('/');
        while (value.Contains("//", StringComparison.Ordinal))
        {
            value = value.Replace("//", "/", StringComparison.Ordinal);
        }

        return value;
    }

    public static string GetName(string normalizedPath)
    {
        if (string.IsNullOrEmpty(normalizedPath))
        {
            return string.Empty;
        }

        var index = normalizedPath.LastIndexOf('/');
        return index < 0 ? normalizedPath : normalizedPath[(index + 1)..];
    }

    public static string GetParent(string normalizedPath)
    {
        var index = normalizedPath.LastIndexOf('/');
        return index <= 0 ? string.Empty : normalizedPath[..index];
    }

    public static int GetDepth(string normalizedPath) =>
        string.IsNullOrEmpty(normalizedPath) ? 0 : normalizedPath.Count(c => c == '/') + 1;

    /// <summary>把内部路径拼接为 Windows 绝对路径，并阻断 .. 逃逸。</summary>
    public static string ToAbsolute(string rootDirectory, string internalPath)
    {
        var relative = Normalize(internalPath).Replace('/', System.IO.Path.DirectorySeparatorChar);
        var combined = System.IO.Path.GetFullPath(System.IO.Path.Combine(rootDirectory, relative));
        var rootFull = System.IO.Path.GetFullPath(rootDirectory);
        if (!combined.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"非法的归档路径：{internalPath}");
        }

        return combined;
    }
}
