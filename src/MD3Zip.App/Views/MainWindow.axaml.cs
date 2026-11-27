using MD3Zip.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Controls.Shapes;
using Avalonia.VisualTree;
using MD3Zip.App.Services;
using MD3Zip.App.ViewModels;
using MD3Zip.Core.Models;

namespace MD3Zip.App.Views;

public partial class MainWindow : Window, IDialogService
{
    private static readonly FilePickerFileType ArchiveFileType = new(L10n.L("压缩包"))
    {
        Patterns =
        [
            "*.7z", "*.zip", "*.rar", "*.tar", "*.gz", "*.tgz", "*.xz", "*.bz2",
            "*.zst", "*.cab", "*.iso", "*.wim", "*.001"
        ]
    };

    private readonly MainViewModel _viewModel;
    private readonly string? _initialArchive;
    private readonly string? _initialPassword;
    private readonly Border? _dropOverlay;
    private readonly Avalonia.Controls.Shapes.Path? _maximizeGlyph;
    private readonly Avalonia.Controls.Shapes.Path? _restoreGlyph;

    public MainWindow()
        : this(null)
    {
    }

    public MainWindow(string? initialArchive, string? initialPassword = null)
    {
        InitializeComponent();

        _initialArchive = initialArchive;
        _initialPassword = initialPassword;
        _viewModel = new MainViewModel { Dialogs = this };
        DataContext = _viewModel;

        // Avalonia 11 的 x:Name 不生成 C# 字段，这里显式取命名控件。
        _dropOverlay = this.FindControl<Border>("DropOverlay");
        _maximizeGlyph = this.FindControl<Avalonia.Controls.Shapes.Path>("MaximizeGlyph");
        _restoreGlyph = this.FindControl<Avalonia.Controls.Shapes.Path>("RestoreGlyph");

        // Popup 的 PopupRoot 不继承窗口主题变体，显式把当前变体同步给「更多」浮层，
        // 否则深色窗口里的菜单会解析到浅色主题令牌。
        ActualThemeVariantChanged += (_, _) => SyncMoreMenuTheme();
        SyncMoreMenuTheme();

        Opened += OnOpenedAsync;
        Closing += (_, _) => _viewModel.Dispose();

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDropAsync);
    }

    private async void OnOpenedAsync(object? sender, EventArgs e) =>
        await _viewModel.InitializeAsync(_initialArchive, _initialPassword).ConfigureAwait(true);

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        // 标题栏上的按钮自带点击语义，不能被拖拽热区吞掉。
        if (e.Source is Visual source && source.FindAncestorOfType<Button>() is not null)
        {
            return;
        }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual source && source.FindAncestorOfType<Button>() is not null)
        {
            return;
        }

        ToggleMaximize();
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        UiLog("minimize click");
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeClick(object? sender, RoutedEventArgs e)
    {
        UiLog("maximize click, state=" + WindowState);
        ToggleMaximize();
        UiLog("after toggle, state=" + WindowState);
    }

    /// <summary>
    /// PopupRoot 是独立 TopLevel，默认拿到的是系统（浅色）主题变体，
    /// 会把 Md3 令牌解析成浅色值。通过 Popup.Child 上的 ThemeVariantScope 强制同步。
    /// </summary>
    private void SyncMoreMenuTheme()
    {
        if (this.FindControl<Popup>("MorePopup")?.Child is ThemeVariantScope scope)
        {
            scope.RequestedThemeVariant = ActualThemeVariant;
        }
    }

    /// <summary>点击「更多」浮层里的菜单项后收起浮层。</summary>
    private void OnMenuActionClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.IsMoreMenuOpen = false;
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        UiLog("close click");
        Close();
    }

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    /// <summary>设置环境变量 MD3ZIP_UI_DEBUG=1 时把窗口交互写入 %TEMP%\md3zip-ui.log。</summary>
    internal static void UiLog(string message)
    {
        if (Environment.GetEnvironmentVariable("MD3ZIP_UI_DEBUG") != "1")
        {
            return;
        }

        try
        {
            File.AppendAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "md3zip-ui.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // 调试日志失败不影响主流程。
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // WindowState 可能在 XAML 装载完成前就被设置，此时命名控件还是 null。
        if (change.Property == WindowStateProperty && _maximizeGlyph is not null && _restoreGlyph is not null)
        {
            var maximized = change.GetNewValue<WindowState>() == WindowState.Maximized;
            _maximizeGlyph.IsVisible = !maximized;
            _restoreGlyph.IsVisible = maximized;
        }
    }

    private async void OnEntryDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: EntryRow row })
        {
            await _viewModel.OpenEntryAsync(row).ConfigureAwait(true);
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var hasFiles = e.DataTransfer?.Contains(DataFormat.File) == true;
        e.DragEffects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        if (_dropOverlay is not null) { _dropOverlay.IsVisible = hasFiles; }
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (_dropOverlay is not null) { _dropOverlay.IsVisible = false; }
    }

    private async void OnDropAsync(object? sender, DragEventArgs e)
    {
        if (_dropOverlay is not null) { _dropOverlay.IsVisible = false; }

        var items = e.DataTransfer?.TryGetFiles();
        if (items is null)
        {
            return;
        }

        var paths = items
            .Select(item => item.TryGetLocalPath())
            .Where(path => !string.IsNullOrEmpty(path))
            .Select(path => path!)
            .ToList();

        if (paths.Count > 0)
        {
            await _viewModel.OpenPathsAsync(paths).ConfigureAwait(true);
        }
    }

    // ================= IDialogService =================

    public async Task<string?> PickArchiveAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L10n.L("打开压缩包"),
            AllowMultiple = false,
            FileTypeFilter = [ArchiveFileType, FilePickerFileTypes.All]
        }).ConfigureAwait(true);

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<IReadOnlyList<string>> PickFilesAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L10n.L("选择要压缩的文件"),
            AllowMultiple = true,
            FileTypeFilter = [FilePickerFileTypes.All]
        }).ConfigureAwait(true);

        return files.Select(f => f.TryGetLocalPath()).Where(p => !string.IsNullOrEmpty(p)).Select(p => p!).ToList();
    }

    public async Task<IReadOnlyList<string>> PickFoldersAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = L10n.L("选择要压缩的文件夹"),
            AllowMultiple = true
        }).ConfigureAwait(true);

        return folders.Select(f => f.TryGetLocalPath()).Where(p => !string.IsNullOrEmpty(p)).Select(p => p!).ToList();
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        }).ConfigureAwait(true);

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveArchiveAsync(string suggestedFileName, string extension)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L10n.L("保存压缩包"),
            SuggestedFileName = suggestedFileName,
            DefaultExtension = extension.TrimStart('.'),
            ShowOverwritePrompt = true,
            FileTypeChoices = [ArchiveFileType]
        }).ConfigureAwait(true);

        return file?.TryGetLocalPath();
    }

    public async Task<string?> AskPasswordAsync(string message, Func<string, Task<SevenZipStatus>>? validate = null)
    {
        var dialog = new PasswordWindow(message, validate);
        return await dialog.ShowDialog<string?>(this).ConfigureAwait(true);
    }

    public async Task ShowCompressDialogAsync(IReadOnlyList<string> sources)
    {
        var dialog = new CompressWindow(sources);
        await dialog.ShowDialog(this).ConfigureAwait(true);
    }

    public async Task ShowPreviewAsync(string archivePath, ArchiveEntry entry, string? password)
    {
        var dialog = new PreviewWindow(archivePath, entry, password);
        await dialog.ShowDialog(this).ConfigureAwait(true);
    }

    public async Task ShowMessageAsync(string title, string message, string? detail = null)
    {
        var dialog = new MessageWindow(title, message, detail);
        await dialog.ShowDialog(this).ConfigureAwait(true);
    }

    public async Task<bool> ConfirmAsync(string title, string message)
    {
        var dialog = new MessageWindow(title, message, null, confirm: true);
        return await dialog.ShowDialog<bool>(this).ConfigureAwait(true);
    }
}
