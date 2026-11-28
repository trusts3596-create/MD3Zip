using MD3Zip.Core;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using MD3Zip.App.ViewModels;

namespace MD3Zip.App.Views;

/// <summary>「文件校验」窗口：算法多选、结果可复制、预期值比对。</summary>
public partial class ChecksumWindow : Window
{
    private readonly ChecksumViewModel _viewModel;

    public ChecksumWindow()
        : this(null)
    {
    }

    public ChecksumWindow(string? archivePath)
    {
        InitializeComponent();

        _viewModel = new ChecksumViewModel { PickTargetAsync = PickTargetAsync };
        _viewModel.ApplyTarget(archivePath);

        // 返回值告诉主窗口本次是否真的算出了结果（用于状态栏 / snack）。
        _viewModel.CloseRequested += () => Close(_viewModel.HasResults);

        DataContext = _viewModel;

        Closing += (_, _) => _viewModel.Dispose();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is not null && _viewModel.HasResults)
        {
            await Clipboard.SetTextAsync(_viewModel.ResultsText);
        }
    }

    private async Task<string?> PickTargetAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L10n.L("选择要校验的文件"),
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.All]
        }).ConfigureAwait(true);

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
