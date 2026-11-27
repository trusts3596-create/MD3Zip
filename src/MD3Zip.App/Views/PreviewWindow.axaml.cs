using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MD3Zip.App.ViewModels;
using MD3Zip.Core.Models;

namespace MD3Zip.App.Views;

public partial class PreviewWindow : Window
{
    private readonly PreviewViewModel _viewModel;

    public PreviewWindow()
        : this(string.Empty, new ArchiveEntry(), null)
    {
    }

    public PreviewWindow(string archivePath, ArchiveEntry entry, string? password)
    {
        InitializeComponent();

        _viewModel = new PreviewViewModel(archivePath, entry, password);
        DataContext = _viewModel;

        Opened += OnOpenedAsync;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private async void OnOpenedAsync(object? sender, EventArgs e) =>
        await _viewModel.LoadAsync().ConfigureAwait(true);

    private void OnHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
