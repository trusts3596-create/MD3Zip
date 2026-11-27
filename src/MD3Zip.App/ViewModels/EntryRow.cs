using Avalonia.Media;
using MD3Zip.App.Infrastructure;
using MD3Zip.App.Services;
using MD3Zip.Core.Models;

namespace MD3Zip.App.ViewModels;

/// <summary>压缩包内的一行（文件或文件夹）。</summary>
public sealed class EntryRow : ObservableObject
{
    private readonly Action _selectionChanged;
    private bool _isSelected;

    public EntryRow(ArchiveEntry entry, Action selectionChanged)
    {
        Entry = entry;
        _selectionChanged = selectionChanged;
        Icon = FileKindService.ResolveIcon(entry);
        IconBrushKey = FileKindService.ResolveIconBrushKey(entry);
        IconBrush = FileKindService.ResolveBrush(IconBrushKey);
    }

    public ArchiveEntry Entry { get; }

    public string Name => Entry.Name;

    public bool IsDirectory => Entry.IsDirectory;

    public string SizeText => Entry.DisplaySize;

    public string ModifiedText => Entry.DisplayModified;

    public string KindText => FileKindService.DescribeKind(Entry);

    public bool IsEncrypted => Entry.IsEncrypted;

    public string FullPath => Entry.Path;

    public Geometry? Icon { get; }

    public string IconBrushKey { get; }

    public IBrush? IconBrush { get; }

    public string SubtitleText => IsDirectory
        ? $"文件夹 · {ModifiedText}"
        : $"{SizeText} · {ModifiedText}";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                _selectionChanged();
            }
        }
    }
}

/// <summary>路径面包屑的一项。</summary>
public sealed class BreadcrumbItem
{
    public BreadcrumbItem(string name, string path, bool isCurrent)
    {
        Name = name;
        Path = path;
        IsCurrent = isCurrent;
    }

    public string Name { get; }

    public string Path { get; }

    public bool IsCurrent { get; }
}
