using System.Globalization;

namespace MD3Zip.Core.Models;

/// <summary>
/// 由 `7z l -slt` 输出解析得到的压缩包结构，内置目录树索引。
/// </summary>
public sealed class ArchiveListing
{
    private readonly Dictionary<string, List<ArchiveEntry>> _children = new(StringComparer.OrdinalIgnoreCase);

    public string ArchivePath { get; init; } = string.Empty;

    public string Format { get; init; } = string.Empty;

    public bool HeaderEncrypted { get; init; }

    public List<ArchiveEntry> Entries { get; init; } = [];

    public long TotalSize => Entries.Where(e => !e.IsDirectory).Sum(e => e.Size);

    public int FileCount => Entries.Count(e => !e.IsDirectory);

    public int DirectoryCount => Entries.Count(e => e.IsDirectory);

    public bool IsEmpty => Entries.Count == 0;

    /// <summary>根层（内部路径 "" 的直接子项）。</summary>
    public IReadOnlyList<ArchiveEntry> Roots => GetChildren(string.Empty);

    public IReadOnlyList<ArchiveEntry> GetChildren(string internalDirectory)
    {
        var key = ArchivePathUtil.Normalize(internalDirectory);
        return _children.TryGetValue(key, out var list) ? list : [];
    }

    /// <summary>生成面包屑：根 + 逐级目录。</summary>
    public IReadOnlyList<ArchiveEntry> GetBreadcrumbs(string internalDirectory)
    {
        var breadcrumbs = new List<ArchiveEntry>();
        var current = ArchivePathUtil.Normalize(internalDirectory);
        if (string.IsNullOrEmpty(current))
        {
            return breadcrumbs;
        }

        var segments = current.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var accumulated = string.Empty;
        foreach (var segment in segments)
        {
            accumulated = string.IsNullOrEmpty(accumulated) ? segment : $"{accumulated}/{segment}";
            breadcrumbs.Add(Entries.FirstOrDefault(e => e.IsDirectory && string.Equals(e.Path, accumulated, StringComparison.OrdinalIgnoreCase))
                            ?? ArchiveEntry.CreateDirectory(accumulated, synthesized: true));
        }

        return breadcrumbs;
    }

    public ArchiveEntry? Find(string internalPath)
    {
        var key = ArchivePathUtil.Normalize(internalPath);
        return Entries.FirstOrDefault(e => string.Equals(e.Path, key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>建立索引，并补全压缩包未显式记录的中间目录。</summary>
    public ArchiveListing BuildIndex()
    {
        var known = new HashSet<string>(Entries.Where(e => e.IsDirectory).Select(e => e.Path), StringComparer.OrdinalIgnoreCase);

        foreach (var entry in Entries.Where(e => !e.IsDirectory).ToList())
        {
            var parent = entry.ParentPath;
            while (!string.IsNullOrEmpty(parent) && known.Add(parent))
            {
                Entries.Add(ArchiveEntry.CreateDirectory(parent, synthesized: true));
                parent = ArchivePathUtil.GetParent(parent);
            }
        }

        _children.Clear();
        foreach (var entry in Entries)
        {
            var key = entry.ParentPath;
            if (!_children.TryGetValue(key, out var list))
            {
                list = [];
                _children[key] = list;
            }

            list.Add(entry);
        }

        foreach (var list in _children.Values)
        {
            list.Sort(static (a, b) =>
            {
                if (a.IsDirectory != b.IsDirectory)
                {
                    return a.IsDirectory ? -1 : 1;
                }

                return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });
        }

        return this;
    }

    /// <summary>
    /// 解析 `7z l -slt` 的标准输出。
    /// </summary>
    public static ArchiveListing Parse(string output, string archivePath)
    {
        var listing = new ArchiveListing { ArchivePath = archivePath };
        if (string.IsNullOrWhiteSpace(output))
        {
            return listing.BuildIndex();
        }

        var lines = output.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        var separatorIndex = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("----------", StringComparison.Ordinal))
            {
                separatorIndex = i;
                break;
            }
        }

        var headerLines = separatorIndex < 0 ? lines : lines[..separatorIndex];
        var header = ParseBlock(headerLines);
        var format = header.GetValueOrDefault("Type", string.Empty);
        var headerEncrypted = header.GetValueOrDefault("Encrypted", "-") == "+";

        var entries = new List<ArchiveEntry>();
        if (separatorIndex >= 0)
        {
            var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = separatorIndex + 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    Flush(current, entries);
                    current.Clear();
                    continue;
                }

                var eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                var key = line[..eq].Trim();
                var value = line[(eq + 1)..].Trim();
                current[key] = value;
            }

            Flush(current, entries);
        }

        return new ArchiveListing
        {
            ArchivePath = archivePath,
            Format = format,
            HeaderEncrypted = headerEncrypted,
            Entries = entries
        }.BuildIndex();
    }

    private static void Flush(Dictionary<string, string> block, List<ArchiveEntry> entries)
    {
        if (!block.TryGetValue("Path", out var rawPath) || string.IsNullOrWhiteSpace(rawPath))
        {
            return;
        }

        // 归档自身的元数据块没有 Attributes / Folder / Size 组合，用 Folder 或 Attributes 判定目录。
        var attributes = block.GetValueOrDefault("Attributes", string.Empty);
        var isFolder = block.GetValueOrDefault("Folder", string.Empty) == "+"
                       || attributes.StartsWith("D", StringComparison.OrdinalIgnoreCase);

        var normalized = ArchivePathUtil.Normalize(rawPath);
        if (string.IsNullOrEmpty(normalized))
        {
            return;
        }

        entries.Add(new ArchiveEntry
        {
            Path = normalized,
            Name = ArchivePathUtil.GetName(normalized),
            ParentPath = ArchivePathUtil.GetParent(normalized),
            IsDirectory = isFolder,
            Size = ParseLong(block.GetValueOrDefault("Size")),
            PackedSize = ParseLong(block.GetValueOrDefault("Packed Size")),
            Modified = ParseDate(block.GetValueOrDefault("Modified")),
            Attributes = attributes,
            IsEncrypted = block.GetValueOrDefault("Encrypted", "-") == "+",
            Depth = ArchivePathUtil.GetDepth(normalized)
        });
    }

    private static Dictionary<string, string> ParseBlock(IEnumerable<string> lines)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var key = line[..eq].Trim();
            if (key.Length == 0 || key.Any(char.IsWhiteSpace))
            {
                continue;
            }

            result[key] = line[(eq + 1)..].Trim();
        }

        return result;
    }

    private static long ParseLong(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0;

    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result)
            ? result
            : null;
    }
}
