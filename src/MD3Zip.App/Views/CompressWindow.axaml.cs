using MD3Zip.Core;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using MD3Zip.App.ViewModels;

namespace MD3Zip.App.Views;

public partial class CompressWindow : Window
{
    private readonly CompressViewModel _viewModel;

    public CompressWindow()
        : this([])
    {
    }

    public CompressWindow(IReadOnlyList<string> sources)
    {
        InitializeComponent();

        _viewModel = new CompressViewModel
        {
            PickSourcesAsync = PickSourcesAsync,
            PickSavePathAsync = PickSavePathAsync
        };

        _viewModel.Seed(sources);
        _viewModel.CloseRequested += Close;
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

    private async Task<IReadOnlyList<string>> PickSourcesAsync(bool folders)
    {
        if (folders)
        {
            var dirs = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = L10n.L("选择要压缩的文件夹"),
                AllowMultiple = true
            });

            return dirs.Select(d => d.TryGetLocalPath()).Where(p => !string.IsNullOrEmpty(p)).Select(p => p!).ToList();
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L10n.L("选择要压缩的文件"),
            AllowMultiple = true,
            FileTypeFilter = [FilePickerFileTypes.All]
        });

        return files.Select(f => f.TryGetLocalPath()).Where(p => !string.IsNullOrEmpty(p)).Select(p => p!).ToList();
    }

    private async Task<string?> PickSavePathAsync(string suggestedFileName, string extension)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L10n.L("保存压缩包"),
            SuggestedFileName = suggestedFileName,
            DefaultExtension = extension.TrimStart('.'),
            ShowOverwritePrompt = true,
            FileTypeChoices =
            [
                new FilePickerFileType(L10n.L("压缩包"))
                {
                    Patterns = ["*.7z", "*.zip", "*.tar"]
                }
            ]
        });

        return file?.TryGetLocalPath();
    }
}
