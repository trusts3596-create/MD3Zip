using MD3Zip.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using MD3Zip.Core.Models;

namespace MD3Zip.App.Services;

/// <summary>按扩展名给出图标与预览能力，避免为每种类型引入图片资源。</summary>
public static class FileKindService
{
    private static readonly HashSet<string> Archives = new(StringComparer.OrdinalIgnoreCase)
    {
        "7z", "zip", "rar", "tar", "gz", "xz", "bz2", "tbz", "tgz", "zst", "cab", "iso", "wim", "lzh", "001"
    };

    private static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase)
    {
        "png", "jpg", "jpeg", "bmp", "gif", "webp", "ico", "tif", "tiff"
    };

    private static readonly HashSet<string> Texts = new(StringComparer.OrdinalIgnoreCase)
    {
        "txt", "md", "json", "xml", "csv", "tsv", "log", "ini", "cfg", "conf", "yml", "yaml",
        "cs", "js", "ts", "tsx", "jsx", "html", "htm", "css", "scss", "py", "java", "c", "h",
        "cpp", "hpp", "go", "rs", "sh", "ps1", "bat", "cmd", "sql", "sln", "csproj", "props"
    };

    public static bool IsArchive(string fileNameOrPath)
    {
        var extension = Path.GetExtension(fileNameOrPath).TrimStart('.').ToLowerInvariant();
        return Archives.Contains(extension);
    }

    private static readonly HashSet<string> Code = new(StringComparer.OrdinalIgnoreCase)
    {
        "cs", "js", "ts", "tsx", "jsx", "html", "htm", "css", "scss", "py", "java", "c", "h",
        "cpp", "hpp", "go", "rs", "sh", "ps1", "bat", "cmd", "sql", "json", "xml", "yml",
        "yaml", "sln", "csproj", "props", "toml", "gradle"
    };

    public static bool IsCode(string fileName) => Code.Contains(ExtensionOf(fileName));

    public static bool IsImage(string fileName) => Images.Contains(ExtensionOf(fileName));

    public static bool IsText(string fileName) => Texts.Contains(ExtensionOf(fileName));

    public static Geometry? ResolveIcon(ArchiveEntry entry)
    {
        var key = entry.IsDirectory
            ? "Icon.Folder"
            : IsArchive(entry.Name)
                ? "Icon.Archive"
                : IsImage(entry.Name)
                    ? "Icon.Image"
                    : IsCode(entry.Name)
                        ? "Icon.Code"
                        : "Icon.File";

        return Lookup(key) as Geometry;
    }

    public static IBrush? ResolveBrush(string key) => Lookup(key) as IBrush;

    /// <summary>
    /// 从应用资源里取语义令牌：优先按当前主题变体查 ThemeDictionaries，
    /// 失败时再回退到默认变体（Application.TryFindResource 对主题字典不够可靠）。
    /// </summary>
    private static object? Lookup(string key)
    {
        if (Application.Current is not { } application)
        {
            return null;
        }

        if (application.TryGetResource(key, application.ActualThemeVariant, out var value) && value is not null)
        {
            return value;
        }

        if (application.TryGetResource(key, ThemeVariant.Default, out value) && value is not null)
        {
            return value;
        }

        return application.TryFindResource(key, out value) ? value : null;
    }

    /// <summary>图标着色用的语义色 key。</summary>
    public static string ResolveIconBrushKey(ArchiveEntry entry)
    {
        if (entry.IsDirectory)
        {
            return "Md3.Primary";
        }

        if (IsArchive(entry.Name))
        {
            return "Md3.Tertiary";
        }

        return IsImage(entry.Name) ? "Md3.Secondary" : "Md3.OnSurfaceVariant";
    }

    public static string DescribeKind(ArchiveEntry entry)
    {
        if (entry.IsDirectory)
        {
            return L10n.L("文件夹");
        }

        var extension = ExtensionOf(entry.Name);
        if (extension.Length == 0)
        {
            return L10n.L("文件");
        }

        return IsArchive(entry.Name) ? $"压缩包 · {extension.ToUpperInvariant()}" : $"{extension.ToUpperInvariant()} 文件";
    }

    public static IReadOnlyList<string> ArchiveExtensions => Archives.ToArray();

    private static string ExtensionOf(string name) => Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
}
