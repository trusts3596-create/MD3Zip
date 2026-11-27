using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Threading;
using MD3Zip.App.Infrastructure;
using MD3Zip.App.Services;
using MD3Zip.Core;
using MD3Zip.Core.Models;

namespace MD3Zip.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private SevenZipService _service;
    private readonly DispatcherTimer _snackbarTimer;

    private ArchiveListing? _listing;
    private string? _archivePath;
    private string? _password;
    private string _currentDirectory = string.Empty;

    private string _searchText = string.Empty;
    private bool _isBusy;
    private double _progressPercent;
    private bool _isIndeterminate = true;
    private string _progressText = string.Empty;
    private string _statusText = L10n.L("准备就绪");
    private string _snackbarText = string.Empty;
    private bool _isSnackbarVisible;
    private string _engineError = string.Empty;
    private int _selectedCount;
    private bool _isDark = true;
    private bool _isMoreMenuOpen;
    private bool _archiveEncrypted;
    private CancellationTokenSource? _operationCts;

    public MainViewModel()
    {
        _service = new SevenZipService();

        _snackbarTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.2) };
        _snackbarTimer.Tick += (_, _) =>
        {
            _snackbarTimer.Stop();
            IsSnackbarVisible = false;
        };

        OpenArchiveCommand = new AsyncRelayCommand(OpenArchivePickerAsync);
        NewArchiveCommand = new AsyncRelayCommand(NewArchiveAsync);
        ExtractAllCommand = new AsyncRelayCommand(ExtractAllAsync);
        ExtractSelectedCommand = new AsyncRelayCommand(ExtractSelectedAsync);
        CancelCommand = new RelayCommand(Cancel);
        GoUpCommand = new RelayCommand(GoUp);
        NavigateCommand = new RelayCommand(parameter => Navigate(parameter as string ?? string.Empty));
        OpenEntryCommand = new AsyncRelayCommand(parameter => OpenEntryAsync(parameter as EntryRow));
        ToggleThemeCommand = new RelayCommand(ToggleTheme);
        CloseArchiveCommand = new RelayCommand(CloseArchive);
        RevealArchiveCommand = new RelayCommand(RevealArchive);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
        SelectAllCommand = new RelayCommand(SelectAll);
        ClearSelectionCommand = new RelayCommand(ClearSelection);
        TestArchiveCommand = new AsyncRelayCommand(TestArchiveAsync);
        ComputeHashCommand = new AsyncRelayCommand(ComputeHashAsync);
        DeleteSelectedCommand = new AsyncRelayCommand(DeleteSelectedAsync);
        AddFilesCommand = new AsyncRelayCommand(AddFilesAsync);
        ToggleMoreMenuCommand = new RelayCommand(() => IsMoreMenuOpen = !IsMoreMenuOpen);
        ToggleLanguageCommand = new RelayCommand(ToggleLanguage);

        CheckEngine();

        LocalizationService.Instance.PropertyChanged += OnLocalizationChanged;
        ThemeManager.Changed += OnThemeChanged;
        _isDark = ThemeManager.IsDark;
    }

    public IDialogService? Dialogs { get; set; }

    public ObservableCollection<EntryRow> Entries { get; } = [];

    public ObservableCollection<BreadcrumbItem> Breadcrumbs { get; } = [];

    public ICommand OpenArchiveCommand { get; }

    public ICommand NewArchiveCommand { get; }

    public ICommand ExtractAllCommand { get; }

    public ICommand ExtractSelectedCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand GoUpCommand { get; }

    public ICommand NavigateCommand { get; }

    public ICommand OpenEntryCommand { get; }

    public ICommand ToggleThemeCommand { get; }

    public ICommand CloseArchiveCommand { get; }

    public ICommand RevealArchiveCommand { get; }

    public ICommand ClearSearchCommand { get; }

    public ICommand SelectAllCommand { get; }

    public ICommand ClearSelectionCommand { get; }

    /// <summary>测试压缩包完整性（7z t）。</summary>
    public ICommand TestArchiveCommand { get; }

    /// <summary>计算当前压缩包的 CRC32 / SHA-256。</summary>
    public ICommand ComputeHashCommand { get; }

    /// <summary>删除选中的归档内条目（7z d）。</summary>
    public ICommand DeleteSelectedCommand { get; }

    /// <summary>把本地文件追加进当前压缩包（7z u）。</summary>
    public ICommand AddFilesCommand { get; }

    /// <summary>展开 / 收起「更多」浮层。</summary>
    public ICommand ToggleMoreMenuCommand { get; }

    /// <summary>切换中 / 英界面语言。</summary>
    public ICommand ToggleLanguageCommand { get; }

    /// <summary>菜单里显示的语言项：当前为中文时显示「English」，反之显示「中文」。</summary>
    public string LanguageLabel => LocalizationService.Instance.IsEnglish ? "中文" : "English";

    private void ToggleLanguage()
    {
        LocalizationService.Instance.Toggle();
        LocalizationService.Instance.Save();
    }

    private void OnLocalizationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LocalizationService.Language))
        {
            OnPropertyChanged(nameof(LanguageLabel));
        }
    }

    public bool IsMoreMenuOpen
    {
        get => _isMoreMenuOpen;
        set => SetProperty(ref _isMoreMenuOpen, value);
    }

    public bool HasArchive => _listing is not null;

    public bool HasNoArchive => _listing is null;

    public bool IsEmptyFolder => _listing is not null && Entries.Count == 0 && string.IsNullOrWhiteSpace(SearchText);

    public bool IsSearchEmpty => _listing is not null && Entries.Count == 0 && !string.IsNullOrWhiteSpace(SearchText);

    public bool IsArchiveBusy => IsBusy;

    public string ArchiveTitle => _archivePath is null ? "MD3 Zip" : Path.GetFileName(_archivePath);

    public string ArchiveLocationText => _archivePath ?? L10n.L("尚未打开压缩包");

    public string ArchiveSummary
    {
        get
        {
            if (_listing is null)
            {
                return L10n.L("拖入压缩包，或浏览本地文件开始");
            }

            var format = string.IsNullOrEmpty(_listing.Format) ? L10n.L("归档") : _listing.Format.ToUpperInvariant();
            var size = ArchiveEntry.FormatSize(_listing.TotalSize);
            var encrypted = _archiveEncrypted || _listing.HeaderEncrypted || _listing.Entries.Any(e => e.IsEncrypted)
                ? L10n.L(" · 加密")
                : string.Empty;
            return $"{_listing.FileCount} 个文件 · {_listing.DirectoryCount} 个文件夹 · {size} · {format}{encrypted}";
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                RefreshEntries();
                OnPropertyChanged(nameof(IsSearching));
            }
        }
    }

    public bool IsSearching => !string.IsNullOrWhiteSpace(_searchText);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsArchiveBusy));
            }
        }
    }

    public double ProgressPercent
    {
        get => _progressPercent;
        private set => SetProperty(ref _progressPercent, value);
    }

    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        private set => SetProperty(ref _isIndeterminate, value);
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

    public string SnackbarText
    {
        get => _snackbarText;
        private set => SetProperty(ref _snackbarText, value);
    }

    public bool IsSnackbarVisible
    {
        get => _isSnackbarVisible;
        private set => SetProperty(ref _isSnackbarVisible, value);
    }

    public string EngineError
    {
        get => _engineError;
        private set
        {
            if (SetProperty(ref _engineError, value))
            {
                OnPropertyChanged(nameof(HasEngineError));
            }
        }
    }

    public bool HasEngineError => !string.IsNullOrEmpty(_engineError);

    public int SelectedCount
    {
        get => _selectedCount;
        private set
        {
            if (SetProperty(ref _selectedCount, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(SelectionText));
            }
        }
    }

    public bool HasSelection => _selectedCount > 0;

    public string SelectionText => $"已选择 {_selectedCount} 项";

    public bool IsDark
    {
        get => _isDark;
        private set => SetProperty(ref _isDark, value);
    }

    public string CurrentDirectoryText => string.IsNullOrEmpty(_currentDirectory)
        ? L10n.L("根目录")
        : "/" + _currentDirectory;

    public bool CanGoUp => !string.IsNullOrEmpty(_currentDirectory);

    public async Task InitializeAsync(string? archivePath, string? password = null)
    {
        if (HasEngineError)
        {
            StatusText = L10n.L("压缩引擎缺失");
            return;
        }

        if (!string.IsNullOrWhiteSpace(archivePath) && File.Exists(archivePath))
        {
            await OpenArchiveAsync(archivePath, password).ConfigureAwait(true);
        }
    }

    /// <summary>拖放入口：单个压缩包则打开，其余走压缩对话框。</summary>
    public async Task OpenPathsAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }

        if (paths.Count == 1 && FileKindService.IsArchive(paths[0]))
        {
            await OpenArchiveAsync(paths[0]).ConfigureAwait(true);
            return;
        }

        if (Dialogs is not null)
        {
            await Dialogs.ShowCompressDialogAsync(paths).ConfigureAwait(true);
        }
    }

    public async Task OpenArchiveAsync(string path, string? password = null)
    {
        if (!_service.IsAvailable)
        {
            ShowSnack(L10n.L("未检测到 7z 引擎，无法读取压缩包"));
            return;
        }

        if (!File.Exists(path))
        {
            ShowSnack(L10n.L("找不到该文件"));
            return;
        }

        IsBusy = true;
        IsIndeterminate = true;
        ProgressText = L10n.L("正在读取归档目录…");
        StatusText = L10n.L("正在解析归档结构");

        var attempt = await _service.ListAsync(path, password).ConfigureAwait(true);
        var promptCount = 0;

        // 需要密码 / 密码错误 → 弹窗索要（弹窗内部完成“验证-报错-重试”闭环）。
        while (!attempt.Succeeded
               && attempt.Result.Status is SevenZipStatus.PasswordRequired or SevenZipStatus.WrongPassword
               && Dialogs is not null
               && promptCount < 3)
        {
            promptCount++;
            IsBusy = false;
            var fileName = Path.GetFileName(path);
            var entered = await Dialogs.AskPasswordAsync(
                $"“{fileName}” 已加密，需要密码才能查看内容。",
                async candidate => (await _service.ListAsync(path, candidate).ConfigureAwait(true)).Result.Status)
                .ConfigureAwait(true);

            if (string.IsNullOrEmpty(entered))
            {
                StatusText = L10n.L("已取消");
                ShowSnack(L10n.L("已取消输入密码"));
                return;
            }

            password = entered;
            IsBusy = true;
            IsIndeterminate = true;
            ProgressText = L10n.L("正在重新读取归档目录…");
            attempt = await _service.ListAsync(path, password).ConfigureAwait(true);
        }

        IsBusy = false;

        if (!attempt.Succeeded || attempt.Listing is null)
        {
            StatusText = L10n.L("读取失败");
            ShowSnack(attempt.Result.Message);
            if (Dialogs is not null && !string.IsNullOrEmpty(attempt.Result.Message))
            {
                await Dialogs.ShowMessageAsync(L10n.L("无法打开压缩包"), attempt.Result.Message, _service.LastRawOutput).ConfigureAwait(true);
            }

            return;
        }

        _listing = attempt.Listing;
        _archivePath = path;
        _password = password;
        _archiveEncrypted = !string.IsNullOrEmpty(password)
                            || attempt.Listing.Entries.Any(e => e.IsEncrypted);

        OnPropertyChanged(nameof(HasArchive));
        OnPropertyChanged(nameof(HasNoArchive));
        OnPropertyChanged(nameof(ArchiveTitle));
        OnPropertyChanged(nameof(ArchiveLocationText));
        OnPropertyChanged(nameof(ArchiveSummary));

        LoadDirectory(string.Empty);
        StatusText = attempt.Result.Message;
        ShowSnack($"已打开 {Path.GetFileName(path)}");
    }

    private void LoadDirectory(string internalDirectory)
    {
        _currentDirectory = ArchivePathUtil.Normalize(internalDirectory);
        RebuildBreadcrumbs();
        RefreshEntries();
        OnPropertyChanged(nameof(CurrentDirectoryText));
        OnPropertyChanged(nameof(CanGoUp));
    }

    private void RebuildBreadcrumbs()
    {
        Breadcrumbs.Clear();
        Breadcrumbs.Add(new BreadcrumbItem(L10n.L("根目录"), string.Empty, string.IsNullOrEmpty(_currentDirectory)));

        if (_listing is null || string.IsNullOrEmpty(_currentDirectory))
        {
            return;
        }

        var crumbs = _listing.GetBreadcrumbs(_currentDirectory);
        foreach (var crumb in crumbs)
        {
            Breadcrumbs.Add(new BreadcrumbItem(crumb.Name, crumb.Path, crumb.Path == _currentDirectory));
        }
    }

    private void RefreshEntries()
    {
        Entries.Clear();
        SelectedCount = 0;

        if (_listing is not null)
        {
            IEnumerable<ArchiveEntry> source = string.IsNullOrWhiteSpace(_searchText)
                ? _listing.GetChildren(_currentDirectory)
                : _listing.Entries.Where(e => e.Name.Contains(_searchText, StringComparison.CurrentCultureIgnoreCase));

            foreach (var entry in source)
            {
                Entries.Add(new EntryRow(entry, UpdateSelectionCount));
            }
        }

        OnPropertyChanged(nameof(IsEmptyFolder));
        OnPropertyChanged(nameof(IsSearchEmpty));
    }

    private void UpdateSelectionCount()
    {
        SelectedCount = Entries.Count(e => e.IsSelected);
        OnPropertyChanged(nameof(HasSelection));
    }

    private void Navigate(string path)
    {
        if (_listing is null)
        {
            return;
        }

        LoadDirectory(path);
    }

    private void GoUp()
    {
        if (string.IsNullOrEmpty(_currentDirectory))
        {
            return;
        }

        LoadDirectory(ArchivePathUtil.GetParent(_currentDirectory));
    }

    public async Task OpenEntryAsync(EntryRow? row)
    {
        if (row is null)
        {
            return;
        }

        if (row.IsDirectory)
        {
            LoadDirectory(row.FullPath);
            return;
        }

        if (Dialogs is null || _archivePath is null)
        {
            return;
        }

        await Dialogs.ShowPreviewAsync(_archivePath, row.Entry, _password).ConfigureAwait(true);
    }

    private async Task OpenArchivePickerAsync()
    {
        if (Dialogs is null)
        {
            return;
        }

        var path = await Dialogs.PickArchiveAsync().ConfigureAwait(true);
        if (!string.IsNullOrEmpty(path))
        {
            await OpenArchiveAsync(path).ConfigureAwait(true);
        }
    }

    private async Task NewArchiveAsync()
    {
        if (Dialogs is null)
        {
            return;
        }

        await Dialogs.ShowCompressDialogAsync([]).ConfigureAwait(true);
    }

    private async Task ExtractAllAsync()
    {
        if (_listing is null || _archivePath is null || Dialogs is null)
        {
            return;
        }

        var target = await Dialogs.PickFolderAsync(L10n.L("选择解压位置")).ConfigureAwait(true);
        if (string.IsNullOrEmpty(target))
        {
            return;
        }

        await RunExtractionAsync(new ExtractionRequest
        {
            ArchivePath = _archivePath,
            TargetDirectory = target,
            Password = _password
        }, L10n.L("正在解压全部文件")).ConfigureAwait(true);
    }

    private async Task ExtractSelectedAsync()
    {
        if (_listing is null || _archivePath is null || Dialogs is null)
        {
            return;
        }

        var selected = Entries.Where(e => e.IsSelected).ToList();
        if (selected.Count == 0)
        {
            ShowSnack(L10n.L("请先勾选要解压的条目"));
            return;
        }

        var target = await Dialogs.PickFolderAsync($"解压 {selected.Count} 个选中项到").ConfigureAwait(true);
        if (string.IsNullOrEmpty(target))
        {
            return;
        }

        await RunExtractionAsync(new ExtractionRequest
        {
            ArchivePath = _archivePath,
            TargetDirectory = target,
            Password = _password,
            SelectedEntries = selected.Select(s => s.FullPath).ToList()
        }, $"正在解压 {selected.Count} 个条目").ConfigureAwait(true);
    }

    private async Task RunExtractionAsync(ExtractionRequest request, string description)
    {
        _operationCts = new CancellationTokenSource();
        IsBusy = true;
        IsIndeterminate = true;
        ProgressPercent = 0;
        ProgressText = description;

        var progress = new Progress<ArchiveProgress>(OnProgress);
        var result = await _service.ExtractAsync(request, progress, _listing?.FileCount, _operationCts.Token)
            .ConfigureAwait(true);

        IsBusy = false;
        _operationCts.Dispose();
        _operationCts = null;

        if (result.Succeeded)
        {
            ProgressPercent = 100;
            StatusText = result.Message;
            ShowSnack($"已解压到 {request.TargetDirectory}");
            return;
        }

        if (result.Status == SevenZipStatus.Cancelled)
        {
            StatusText = L10n.L("已取消");
            ShowSnack(L10n.L("操作已取消"));
            return;
        }

        StatusText = L10n.L("解压失败");
        ShowSnack(result.Message);
        if (Dialogs is not null)
        {
            await Dialogs.ShowMessageAsync(L10n.L("解压失败"), result.Message, _service.LastRawOutput).ConfigureAwait(true);
        }
    }

    private async Task TestArchiveAsync()
    {
        if (_archivePath is null || Dialogs is null)
        {
            return;
        }

        _operationCts = new CancellationTokenSource();
        IsBusy = true;
        IsIndeterminate = true;
        ProgressPercent = 0;
        ProgressText = L10n.L("正在测试压缩包完整性…");

        var progress = new Progress<ArchiveProgress>(OnProgress);
        var result = await _service.TestAsync(_archivePath, _password, progress, _operationCts.Token)
            .ConfigureAwait(true);

        IsBusy = false;
        _operationCts.Dispose();
        _operationCts = null;

        if (result.Succeeded)
        {
            StatusText = result.Message;
            ShowSnack(L10n.L("测试通过"));
            await Dialogs.ShowMessageAsync(L10n.L("测试完成"), result.Message).ConfigureAwait(true);
            return;
        }

        if (result.Status == SevenZipStatus.Cancelled)
        {
            StatusText = L10n.L("已取消");
            ShowSnack(L10n.L("操作已取消"));
            return;
        }

        StatusText = L10n.L("测试失败");
        ShowSnack(result.Message);
        await Dialogs.ShowMessageAsync(L10n.L("压缩包测试失败"), result.Message, _service.LastRawOutput).ConfigureAwait(true);
    }

    private async Task ComputeHashAsync()
    {
        if (_archivePath is null || Dialogs is null)
        {
            return;
        }

        IsBusy = true;
        IsIndeterminate = true;
        ProgressText = L10n.L("正在计算 CRC32…");

        var crc = await _service.ComputeHashesAsync(_archivePath, HashAlgorithmKind.Crc32).ConfigureAwait(true);
        ProgressText = L10n.L("正在计算 SHA-256…");
        var sha = await _service.ComputeHashesAsync(_archivePath, HashAlgorithmKind.Sha256).ConfigureAwait(true);

        IsBusy = false;

        if (!sha.Succeeded && !crc.Succeeded)
        {
            StatusText = L10n.L("校验和计算失败");
            ShowSnack(sha.Message);
            await Dialogs.ShowMessageAsync(L10n.L("校验和失败"), sha.Message, sha.RawOutput).ConfigureAwait(true);
            return;
        }

        var info = new FileInfo(_archivePath);
        var detail = string.Join(
            Environment.NewLine,
            $"文件     : {Path.GetFileName(_archivePath)}",
            $"大小     : {ArchiveEntry.FormatSize(info.Length)}（{info.Length:N0} 字节）",
            $"CRC32    : {crc.Hash ?? "—"}",
            $"SHA-256  : {sha.Hash ?? "—"}",
            $"修改时间 : {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}");

        StatusText = L10n.L("校验和已生成");
        ShowSnack(L10n.L("校验和已生成"));
        await Dialogs.ShowMessageAsync(L10n.L("文件校验和"), L10n.L("可直接复制下方文本用于比对。"), detail).ConfigureAwait(true);
    }

    private async Task DeleteSelectedAsync()
    {
        if (_archivePath is null || Dialogs is null || _listing is null)
        {
            return;
        }

        var selected = Entries.Where(e => e.IsSelected).ToList();
        if (selected.Count == 0)
        {
            ShowSnack(L10n.L("请先勾选要删除的条目"));
            return;
        }

        var sample = string.Join("、", selected.Take(3).Select(s => s.Name));
        var summary = selected.Count > 3 ? $"{sample} 等 {selected.Count} 项" : sample;
        var confirmed = await Dialogs.ConfirmAsync(
            L10n.L("删除压缩包内条目"),
            $"将从压缩包中删除：{summary}。\n此操作会直接改写压缩包，无法撤销。").ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        IsBusy = true;
        IsIndeterminate = true;
        ProgressText = L10n.L("正在删除条目…");

        var result = await _service
            .DeleteEntriesAsync(_archivePath, selected.Select(s => s.FullPath).ToList(), _password)
            .ConfigureAwait(true);

        IsBusy = false;

        if (!result.Succeeded)
        {
            StatusText = L10n.L("删除失败");
            ShowSnack(result.Message);
            await Dialogs.ShowMessageAsync(L10n.L("删除失败"), result.Message, _service.LastRawOutput).ConfigureAwait(true);
            return;
        }

        var path = _archivePath;
        var password = _password;
        await OpenArchiveAsync(path, password).ConfigureAwait(true);
        StatusText = result.Message;
        ShowSnack(result.Message);
    }

    private async Task AddFilesAsync()
    {
        if (_archivePath is null || Dialogs is null)
        {
            return;
        }

        var files = await Dialogs.PickFilesAsync().ConfigureAwait(true);
        if (files.Count == 0)
        {
            return;
        }

        IsBusy = true;
        IsIndeterminate = true;
        ProgressText = L10n.L("正在追加入压缩包…");

        var result = await _service.UpdateAsync(new UpdateRequest
        {
            ArchivePath = _archivePath,
            Sources = files.ToList(),
            Password = _password
        }).ConfigureAwait(true);

        IsBusy = false;

        if (!result.Succeeded)
        {
            StatusText = L10n.L("追加失败");
            ShowSnack(result.Message);
            await Dialogs.ShowMessageAsync(L10n.L("追加失败"), result.Message, _service.LastRawOutput).ConfigureAwait(true);
            return;
        }

        var path = _archivePath;
        var password = _password;
        await OpenArchiveAsync(path, password).ConfigureAwait(true);
        StatusText = result.Message;
        ShowSnack(result.Message);
    }

    private void OnProgress(ArchiveProgress progress)
    {
        IsIndeterminate = progress.IsIndeterminate;
        if (!progress.IsIndeterminate)
        {
            ProgressPercent = progress.Percent;
        }

        if (!string.IsNullOrWhiteSpace(progress.CurrentFile))
        {
            ProgressText = progress.CurrentFile!;
        }
        else if (!string.IsNullOrWhiteSpace(progress.Operation))
        {
            ProgressText = progress.Operation!;
        }
    }

    private void Cancel()
    {
        try
        {
            _operationCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 操作已结束。
        }
    }

    private void ToggleTheme()
    {
        ThemeManager.Toggle();
    }

    private void OnThemeChanged(object? sender, bool dark)
    {
        IsDark = dark;
        // 主题切换后重新解析语义色画刷，让行内图标同步换色。
        RefreshEntries();
    }

    private void CloseArchive()
    {
        _listing = null;
        _archivePath = null;
        _password = null;
        _archiveEncrypted = false;
        _currentDirectory = string.Empty;
        Entries.Clear();
        Breadcrumbs.Clear();
        SelectedCount = 0;
        StatusText = L10n.L("准备就绪");

        OnPropertyChanged(nameof(HasArchive));
        OnPropertyChanged(nameof(HasNoArchive));
        OnPropertyChanged(nameof(ArchiveTitle));
        OnPropertyChanged(nameof(ArchiveLocationText));
        OnPropertyChanged(nameof(ArchiveSummary));
        OnPropertyChanged(nameof(IsEmptyFolder));
        OnPropertyChanged(nameof(IsSearchEmpty));
        OnPropertyChanged(nameof(CurrentDirectoryText));
        OnPropertyChanged(nameof(CanGoUp));
    }

    private void RevealArchive()
    {
        if (_archivePath is not null)
        {
            ShellService.Reveal(_archivePath);
        }
    }

    private void SelectAll()
    {
        foreach (var entry in Entries)
        {
            entry.IsSelected = true;
        }
    }

    private void ClearSelection()
    {
        foreach (var entry in Entries)
        {
            entry.IsSelected = false;
        }
    }

    private void ShowSnack(string message)
    {
        SnackbarText = message;
        IsSnackbarVisible = true;
        _snackbarTimer.Stop();
        _snackbarTimer.Start();
    }

    private void CheckEngine()
    {
        if (_service.IsAvailable)
        {
            return;
        }

        SevenZipLocator.EnsureBundled(out var message);
        _service = new SevenZipService();
        if (_service.IsAvailable)
        {
            return;
        }

        EngineError = message ?? L10n.L("未找到 7z.exe，解压/压缩功能不可用。");
        StatusText = L10n.L("压缩引擎缺失");
    }

    public void Dispose()
    {
        LocalizationService.Instance.PropertyChanged -= OnLocalizationChanged;
        ThemeManager.Changed -= OnThemeChanged;
        _snackbarTimer.Stop();
        _operationCts?.Cancel();
        _operationCts?.Dispose();
    }
}
