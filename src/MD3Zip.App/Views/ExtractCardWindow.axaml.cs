using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using MD3Zip.App.ViewModels;

namespace MD3Zip.App.Views;

public partial class ExtractCardWindow : Window
{
    private readonly ExtractCardViewModel _viewModel;

    public ExtractCardWindow()
        : this(string.Empty, false)
    {
    }

    public ExtractCardWindow(string archivePath, bool smart)
    {
        InitializeComponent();

        _viewModel = new ExtractCardViewModel(archivePath, smart);
        _viewModel.PasswordPromptAsync = ShowPasswordPromptAsync;
        _viewModel.CloseRequested += Close;
        DataContext = _viewModel;

        Opened += OnOpenedAsync;
        Closing += (_, _) => _viewModel.Dispose();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private async Task<string?> ShowPasswordPromptAsync(Func<string, Task<Core.Models.SevenZipStatus>> validator)
    {
        var prompt = new PasswordWindow(
            $"“{System.IO.Path.GetFileName(_viewModel.FileName)}” 已加密，请先输入密码再解压。",
            validator)
        {
            // 卡片本身是 Topmost，密码框必须跟着置顶，否则会落到其他窗口后面。
            Topmost = Topmost
        };

        return await prompt.ShowDialog<string?>(this).ConfigureAwait(true);
    }

    private async void OnOpenedAsync(object? sender, EventArgs e)
    {
        PositionBottomRight();
        await _viewModel.StartAsync().ConfigureAwait(true);
    }

    private void OnCardPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void PositionBottomRight()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        var width = (int)((ClientSize.Width > 0 ? ClientSize.Width : Width) * scale);
        var height = (int)((ClientSize.Height > 0 ? ClientSize.Height : Height) * scale);
        var margin = (int)(24 * scale);

        Position = new PixelPoint(area.Right - width - margin, area.Bottom - height - margin);
    }
}
