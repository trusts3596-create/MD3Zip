using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows.Input;
using MD3Zip.App.Infrastructure;
using MD3Zip.Core;
using MD3Zip.Core.Models;

namespace MD3Zip.App.ViewModels;

/// <summary>压缩配置对话框的视图模型（格式分段按钮 / 等级滑块 / 可折叠高级选项）。</summary>
public sealed partial class CompressViewModel : ObservableObject, IDisposable
{
    private readonly SevenZipService _service = new();
    private static readonly string[] KnownArchiveExtensions =
        [".7z", ".zip", ".tar", ".gz", ".bz2", ".xz"];

    private readonly List<string> _sources = [];
    private CancellationTokenSource? _cts;
    private bool _outputDirUserSet;
    private long _totalBytes;

    private string _fileName = L10n.L("新建压缩包.7z");
    private string _outputDirectory;
    private ArchiveFormat _format = ArchiveFormat.SevenZip;
    private double _levelValue = 5;
    private string _password = string.Empty;
    private string _passwordConfirm = string.Empty;
    private bool _encryptFileNames = true;
    private string _volumePreset = L10n.L("不分卷");
    private string _volumeCustom = string.Empty;
    private bool _isAdvancedOpen;
    private bool _isBusy;
    private double _percent;
    private string _progressText = string.Empty;
    private string _statusText = string.Empty;
    private string _errorText = string.Empty;
    private bool _isCompleted;
    private bool _deleteSourcesAfter;
    private bool _isPasswordVisible;

    public CompressViewModel()
    {
        _outputDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        AddFilesCommand = new AsyncRelayCommand(AddFilesAsync);
        AddFolderCommand = new AsyncRelayCommand(AddFolderAsync);
        RemoveSourceCommand = new RelayCommand(parameter => RemoveSource(parameter as string));
        BrowseOutputCommand = new AsyncRelayCommand(BrowseOutputAsync);
        StartCommand = new AsyncRelayCommand(StartAsync);
        CancelCommand = new RelayCommand(Cancel);
        ToggleAdvancedCommand = new RelayCommand(() => IsAdvancedOpen = !IsAdvancedOpen);
        TogglePasswordVisibilityCommand = new RelayCommand(() => IsPasswordVisible = !IsPasswordVisible);
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke());

        UpdateSuggestedName();
    }

    /// <summary>视图层注入的对话框能力（选择文件/文件夹/保存位置）。</summary>
    public Func<bool, Task<IReadOnlyList<string>>>? PickSourcesAsync { get; set; }

    public Func<string, string, Task<string?>>? PickSavePathAsync { get; set; }

    public event Action? CloseRequested;

    public ObservableCollection<SourceItem> Sources { get; } = [];

    public ICommand AddFilesCommand { get; }

    public ICommand AddFolderCommand { get; }

    public ICommand RemoveSourceCommand { get; }

    public ICommand BrowseOutputCommand { get; }

    public ICommand StartCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand ToggleAdvancedCommand { get; }

    public ICommand CloseCommand { get; }

    public string FileName
    {
        get => _fileName;
        set
        {
            if (SetProperty(ref _fileName, value))
            {
                OnPropertyChanged(nameof(OutputPath));
            }
        }
    }

    public string OutputDirectory
    {
        get => _outputDirectory;
        set
        {
            if (SetProperty(ref _outputDirectory, value))
            {
                OnPropertyChanged(nameof(OutputPath));
            }
        }
    }

    public string OutputPath
    {
        get
        {
            var name = EnsureExtension(FileName);
            return string.IsNullOrWhiteSpace(OutputDirectory)
                ? name
                : Path.Combine(OutputDirectory, name);
        }
    }

    public bool IsSevenZip
    {
        get => _format == ArchiveFormat.SevenZip;
        set
        {
            if (value)
            {
                ChangeFormat(ArchiveFormat.SevenZip);
            }
        }
    }

    public bool IsZip
    {
        get => _format == ArchiveFormat.Zip;
        set
        {
            if (value)
            {
                ChangeFormat(ArchiveFormat.Zip);
            }
        }
    }

    public bool IsTar
    {
        get => _format == ArchiveFormat.Tar;
        set
        {
            if (value)
            {
                ChangeFormat(ArchiveFormat.Tar);
            }
        }
    }

    public bool IsGZip
    {
        get => _format == ArchiveFormat.GZip;
        set
        {
            if (value)
            {
                ChangeFormat(ArchiveFormat.GZip);
            }
        }
    }

    public bool IsBZip2
    {
        get => _format == ArchiveFormat.BZip2;
        set
        {
            if (value)
            {
                ChangeFormat(ArchiveFormat.BZip2);
            }
        }
    }

    public bool IsXz
    {
        get => _format == ArchiveFormat.Xz;
        set
        {
            if (value)
            {
                ChangeFormat(ArchiveFormat.Xz);
            }
        }
    }

    public bool SupportsAdvanced => _format != ArchiveFormat.Tar;

    /// <summary>只有 7z / zip 支持密码。</summary>
    public bool SupportsEncryption => _format is ArchiveFormat.SevenZip or ArchiveFormat.Zip;

    /// <summary>只有 7z / zip 支持分卷。</summary>
    public bool SupportsVolume => _format is ArchiveFormat.SevenZip or ArchiveFormat.Zip;

    /// <summary>gz / bz2 / xz 只能压缩单个文件。</summary>
    public bool IsSingleFileFormat => _format is ArchiveFormat.GZip or ArchiveFormat.BZip2 or ArchiveFormat.Xz;

    /// <summary>压缩成功后删除源文件（7-Zip 的 "Delete files after compression"）。</summary>
    public bool DeleteSourcesAfter
    {
        get => _deleteSourcesAfter;
        set => SetProperty(ref _deleteSourcesAfter, value);
    }

    /// <summary>密码明文 / 掩码切换。</summary>
    public bool IsPasswordVisible
    {
        get => _isPasswordVisible;
        set
        {
            if (SetProperty(ref _isPasswordVisible, value))
            {
                OnPropertyChanged(nameof(PasswordCharValue));
            }
        }
    }

    /// <summary>'\0' 表示显示明文；'●' 表示掩码。</summary>
    public char PasswordCharValue => _isPasswordVisible ? '\0' : '●';

    public ICommand TogglePasswordVisibilityCommand { get; }

    public double LevelValue
    {
        get => _levelValue;
        set
        {
            if (SetProperty(ref _levelValue, value))
            {
                OnPropertyChanged(nameof(LevelText));
                OnPropertyChanged(nameof(LevelDescription));
            }
        }
    }

    public string LevelText => ((int)Math.Round(LevelValue)) switch
    {
        <= 2 => L10n.L("极速"),
        <= 4 => L10n.L("快速"),
        <= 6 => L10n.L("标准"),
        <= 8 => L10n.L("最大"),
        _ => L10n.L("极限")
    };

    public string LevelDescription => ((int)Math.Round(LevelValue)) switch
    {
        <= 2 => L10n.L("-mx1 · 速度优先，体积最大"),
        <= 4 => L10n.L("-mx3 · 日常使用"),
        <= 6 => L10n.L("-mx5 · 压缩与速度平衡（默认）"),
        <= 8 => L10n.L("-mx7 · 更小体积"),
        _ => L10n.L("-mx9 · 极限压缩，耗时最长")
    };

    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    public string PasswordConfirm
    {
        get => _passwordConfirm;
        set => SetProperty(ref _passwordConfirm, value);
    }

    public bool EncryptFileNames
    {
        get => _encryptFileNames;
        set => SetProperty(ref _encryptFileNames, value);
    }

    public string VolumePreset
    {
        get => _volumePreset;
        set
        {
            if (SetProperty(ref _volumePreset, value))
            {
                OnPropertyChanged(nameof(IsCustomVolume));
                OnPropertyChanged(nameof(IsVolumeNone));
                OnPropertyChanged(nameof(IsVolume100m));
                OnPropertyChanged(nameof(IsVolume500m));
                OnPropertyChanged(nameof(IsVolume1g));
                OnPropertyChanged(nameof(IsVolumeCustom));
                OnPropertyChanged(nameof(VolumeSummary));
            }
        }
    }

    public string VolumeCustom
    {
        get => _volumeCustom;
        set
        {
            if (SetProperty(ref _volumeCustom, value))
            {
                OnPropertyChanged(nameof(VolumeSummary));
            }
        }
    }

    public bool IsVolumeNone
    {
        get => VolumePreset == L10n.L("不分卷");
        set
        {
            if (value)
            {
                VolumePreset = L10n.L("不分卷");
            }
        }
    }

    public bool IsVolume100m
    {
        get => VolumePreset == "100m";
        set
        {
            if (value)
            {
                VolumePreset = "100m";
            }
        }
    }

    public bool IsVolume500m
    {
        get => VolumePreset == "500m";
        set
        {
            if (value)
            {
                VolumePreset = "500m";
            }
        }
    }

    public bool IsVolume1g
    {
        get => VolumePreset == "1g";
        set
        {
            if (value)
            {
                VolumePreset = "1g";
            }
        }
    }

    public bool IsVolumeCustom
    {
        get => VolumePreset == L10n.L("自定义");
        set
        {
            if (value)
            {
                VolumePreset = L10n.L("自定义");
            }
        }
    }

    public bool IsCustomVolume => VolumePreset == L10n.L("自定义");

    public string VolumeSummary => ResolveVolume() ?? L10n.L("不分卷");

    public bool IsAdvancedOpen
    {
        get => _isAdvancedOpen;
        set => SetProperty(ref _isAdvancedOpen, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsFormVisible));
            }
        }
    }

    public bool IsFormVisible => !IsBusy;

    public bool IsCompleted
    {
        get => _isCompleted;
        private set => SetProperty(ref _isCompleted, value);
    }

    public double Percent
    {
        get => _percent;
        private set => SetProperty(ref _percent, value);
    }

    public string ProgressText
    {
        get => _progressText;
        private set => SetProperty(ref _progressText, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string ErrorText
    {
        get => _errorText;
        private set
        {
            if (SetProperty(ref _errorText, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_errorText);

    public bool HasSources => Sources.Count > 0;

    /// <summary>gz / bz2 / xz 仅在「恰好一个普通文件」时可用。</summary>
    public bool CanUseSingleFileFormats =>
        _sources.Count == 1 && !Directory.Exists(_sources[0]);

    public string SourceSummary => Sources.Count == 0
        ? L10n.L("尚未选择要压缩的内容")
        : $"共 {Sources.Count} 项待压缩";

    /// <summary>底部状态行：无源时提示引擎，有源时显示「共 N 项待压缩 · 大小」。</summary>
    public string FooterSummary => Sources.Count == 0
        ? L10n.L("使用内置 7z.exe 引擎压缩")
        : $"共 {Sources.Count} 项待压缩 · {ArchiveEntry.FormatSize(_totalBytes)}";

    private void RefreshDerived()
    {
        OnPropertyChanged(nameof(HasSources));
        OnPropertyChanged(nameof(SourceSummary));
        OnPropertyChanged(nameof(CanUseSingleFileFormats));
        _totalBytes = ComputeSourcesSize();
        OnPropertyChanged(nameof(FooterSummary));

        // 当前格式被新列表判为不可用时回落到 7z（选项本身已置灰）。
        if (IsSingleFileFormat && !CanUseSingleFileFormats)
        {
            ChangeFormat(ArchiveFormat.SevenZip);
        }
    }

    private long ComputeSourcesSize()
    {
        long total = 0;
        foreach (var path in _sources)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            total += new FileInfo(file).Length;
                        }
                        catch (IOException)
                        {
                        }
                        catch (UnauthorizedAccessException)
                        {
                        }
                    }
                }
                else if (File.Exists(path))
                {
                    total += new FileInfo(path).Length;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return total;
    }

    public void Seed(IReadOnlyList<string> sources)
    {
        foreach (var source in sources)
        {
            AddSource(source);
        }

        // 右键菜单/拖放场景：默认输出到源文件所在目录，而不是桌面。
        if (sources.Count > 0)
        {
            var first = sources[0];
            OutputDirectory = Directory.Exists(first)
                ? (Path.GetDirectoryName(first) ?? OutputDirectory)
                : (Path.GetDirectoryName(first) ?? OutputDirectory);
        }

        UpdateSuggestedName();
    }

    private void AddSource(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || _sources.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        _sources.Add(path);
        Sources.Add(new SourceItem(path));

        // 默认输出目录跟随首个源所在目录（用户手动指定过则不改）。
        if (!_outputDirUserSet && _sources.Count == 1)
        {
            OutputDirectory = Path.GetDirectoryName(path) ?? OutputDirectory;
        }

        RefreshDerived();
    }

    private void RemoveSource(string? path)
    {
        if (path is null)
        {
            return;
        }

        var index = _sources.FindIndex(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return;
        }

        _sources.RemoveAt(index);
        Sources.RemoveAt(index);
        RefreshDerived();
    }

    private async Task AddFilesAsync()
    {
        if (PickSourcesAsync is null)
        {
            return;
        }

        foreach (var path in await PickSourcesAsync(false).ConfigureAwait(true))
        {
            AddSource(path);
        }

        UpdateSuggestedName();
    }

    private async Task AddFolderAsync()
    {
        if (PickSourcesAsync is null)
        {
            return;
        }

        foreach (var path in await PickSourcesAsync(true).ConfigureAwait(true))
        {
            AddSource(path);
        }

        UpdateSuggestedName();
    }

    private async Task BrowseOutputAsync()
    {
        if (PickSavePathAsync is null)
        {
            return;
        }

        var path = await PickSavePathAsync(EnsureExtension(FileName), ExtensionForFormat()).ConfigureAwait(true);
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        _outputDirUserSet = true;
        OutputDirectory = Path.GetDirectoryName(path) ?? OutputDirectory;
        FileName = Path.GetFileName(path);
    }

    private void ChangeFormat(ArchiveFormat format)
    {
        if (_format == format)
        {
            return;
        }

        _format = format;

        // 单文件格式沿用源文件名：a.txt → a.txt.gz（7-Zip 的行为）。
        FileName = IsSingleFileFormat && _sources.Count == 1 && File.Exists(_sources[0])
            ? EnsureExtension(Path.GetFileName(_sources[0]))
            : EnsureExtension(FileName);  // 裸名不再二次剥离：EnsureExtension 的白名单已覆盖两种形态

        OnPropertyChanged(nameof(IsSevenZip));
        OnPropertyChanged(nameof(IsZip));
        OnPropertyChanged(nameof(IsTar));
        OnPropertyChanged(nameof(IsGZip));
        OnPropertyChanged(nameof(IsBZip2));
        OnPropertyChanged(nameof(IsXz));
        OnPropertyChanged(nameof(SupportsAdvanced));
        OnPropertyChanged(nameof(SupportsEncryption));
        OnPropertyChanged(nameof(SupportsVolume));
        OnPropertyChanged(nameof(IsSingleFileFormat));
        OnPropertyChanged(nameof(OutputPath));

        // 不支持加密的格式清掉已输入的密码，避免“设了密码却没生效”的错觉。
        if (!SupportsEncryption)
        {
            Password = string.Empty;
            PasswordConfirm = string.Empty;
        }

        // 不支持分卷的格式重置分卷设置。
        if (!SupportsVolume || format == ArchiveFormat.Tar)
        {
            VolumePreset = L10n.L("不分卷");
        }

        if (format == ArchiveFormat.Tar)
        {
            IsAdvancedOpen = false;
        }
    }

    private async Task StartAsync()
    {
        ErrorText = string.Empty;

        if (_sources.Count == 0)
        {
            ErrorText = L10n.L("请先添加要压缩的文件或文件夹。");
            return;
        }

        if (!string.IsNullOrEmpty(Password) && Password != PasswordConfirm)
        {
            ErrorText = L10n.L("两次输入的密码不一致。");
            return;
        }

        var volume = ResolveVolume();
        if (volume == "invalid")
        {
            ErrorText = L10n.L("分卷大小格式无效，请使用例如 100m、1g、500k。");
            return;
        }

        if (IsSingleFileFormat && (_sources.Count != 1 || Directory.Exists(_sources[0])))
        {
            ErrorText = L10n.L("gz / bz2 / xz 是单文件格式，只能压缩一个文件；多个项目请改用 7z 或 zip。");
            return;
        }

        if (!_service.IsAvailable)
        {
            ErrorText = L10n.L("未检测到 7z.exe，无法执行压缩。");
            return;
        }

        var request = new CompressionRequest
        {
            Sources = [.. _sources],
            OutputPath = OutputPath,
            Format = _format,
            Level = ResolveLevel(),
            Password = string.IsNullOrEmpty(Password) ? null : Password,
            EncryptFileNames = EncryptFileNames,
            VolumeSize = volume,
            DeleteSourcesAfter = DeleteSourcesAfter,
            WorkingDirectory = Path.GetDirectoryName(_sources[0])
        };

        _cts = new CancellationTokenSource();
        IsBusy = true;
        IsCompleted = false;
        Percent = 0;
        ProgressText = L10n.L("正在准备压缩…");
        StatusText = L10n.L("正在压缩");

        var progress = new Progress<ArchiveProgress>(p =>
        {
            if (!p.IsIndeterminate)
            {
                Percent = p.Percent;
            }

            if (!string.IsNullOrWhiteSpace(p.CurrentFile))
            {
                ProgressText = p.CurrentFile!;
            }
        });

        var result = await _service.CompressAsync(request, progress, _sources.Count, _cts.Token).ConfigureAwait(true);

        _cts.Dispose();
        _cts = null;

        if (result.Succeeded)
        {
            Percent = 100;
            IsCompleted = true;
            StatusText = L10n.L("压缩完成");
            ProgressText = Path.GetFileName(OutputPath);

            await Task.Delay(900).ConfigureAwait(true);
            CloseRequested?.Invoke();
            return;
        }

        IsBusy = false;
        StatusText = L10n.L("压缩失败");
        ErrorText = result.Status == SevenZipStatus.Cancelled ? L10n.L("已取消压缩。") : result.Message;
    }

    private void Cancel()
    {
        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 已经结束。
        }
    }

    private CompressionLevel ResolveLevel() => ((int)Math.Round(LevelValue)) switch
    {
        <= 2 => CompressionLevel.Fastest,
        <= 4 => CompressionLevel.Fast,
        <= 6 => CompressionLevel.Normal,
        <= 8 => CompressionLevel.Maximum,
        _ => CompressionLevel.Ultra
    };

    private string? ResolveVolume()
    {
        if (!SupportsAdvanced || VolumePreset == L10n.L("不分卷"))
        {
            return null;
        }

        var raw = VolumePreset == L10n.L("自定义") ? VolumeCustom : VolumePreset;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return VolumeRegex().IsMatch(raw.Trim()) ? raw.Trim().ToLowerInvariant() : "invalid";
    }

    private string ExtensionForFormat() => _format switch
    {
        ArchiveFormat.Zip => ".zip",
        ArchiveFormat.Tar => ".tar",
        ArchiveFormat.GZip => ".gz",
        ArchiveFormat.BZip2 => ".bz2",
        ArchiveFormat.Xz => ".xz",
        _ => ".7z"
    };

    private string EnsureExtension(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return L10n.L("新建压缩包") + ExtensionForFormat();
        }

        var trimmed = name.Trim();
        var wanted = ExtensionForFormat();
        if (trimmed.EndsWith(wanted, StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        // 只剥离「已知的其它归档扩展名」，绝不按通用扩展名去猜——
        // 否则 hap_installer-Windows-3.1.0 会被截成 hap_installer-Windows-3.1。
        foreach (var ext in KnownArchiveExtensions)
        {
            if (!ext.Equals(wanted, StringComparison.OrdinalIgnoreCase)
                && trimmed.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[..^ext.Length] + wanted;
            }
        }

        // 单文件格式沿用 7-Zip 的命名习惯：a.txt → a.txt.gz
        return trimmed + wanted;
    }

    private void UpdateSuggestedName()
    {
        if (_sources.Count == 0)
        {
            return;
        }

        var first = _sources[0];
        var baseName = Directory.Exists(first)
            ? new DirectoryInfo(first).Name
            : Path.GetFileNameWithoutExtension(first);

        if (_sources.Count > 1)
        {
            var parent = Path.GetDirectoryName(first);
            baseName = string.IsNullOrEmpty(parent) ? L10n.L("压缩包") : new DirectoryInfo(parent).Name;
        }

        FileName = EnsureExtension(baseName);

        if (!_outputDirUserSet)
        {
            OutputDirectory = Path.GetDirectoryName(first) ?? OutputDirectory;
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }

    [GeneratedRegex(@"^\d+\s*[kKmMgG]$")]
    private static partial Regex VolumeRegex();
}

/// <summary>压缩源列表项。</summary>
public sealed class SourceItem
{
    public SourceItem(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public string Name => Directory.Exists(Path)
        ? new DirectoryInfo(Path).Name + System.IO.Path.DirectorySeparatorChar
        : System.IO.Path.GetFileName(Path);

    public string Location => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;

    public string KindText => Directory.Exists(Path) ? L10n.L("文件夹") : L10n.L("文件");
}
