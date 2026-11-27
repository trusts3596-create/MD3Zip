using MD3Zip.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using MD3Zip.Core.Models;

namespace MD3Zip.App.Views;

/// <summary>
/// MD3 密码输入弹窗。内置“验证 → 报错 → 重试”闭环：
/// 传入 validator 时，密码会先被验证；失败则停留在此窗口并显示红色错误提示。
/// </summary>
public partial class PasswordWindow : Window
{
    private readonly Func<string, Task<SevenZipStatus>>? _validator;
    private readonly TextBox? _passwordBox;
    private readonly TextBlock? _promptText;
    private readonly TextBlock? _errorText;
    private readonly StackPanel? _errorPanel;
    private readonly ProgressBar? _validationBar;
    private readonly Button? _confirmButton;
    private readonly Button? _revealButton;
    private readonly Avalonia.Controls.Shapes.Path? _revealGlyph;

    private bool _isRevealed;
    private bool _isBusy;

    public PasswordWindow()
        : this(L10n.L("该压缩包已加密，请输入密码。"), null)
    {
    }

    public PasswordWindow(string prompt, Func<string, Task<SevenZipStatus>>? validator = null)
    {
        InitializeComponent();

        _validator = validator;
        _promptText = this.FindControl<TextBlock>("PromptText");
        _passwordBox = this.FindControl<TextBox>("PasswordBox");
        _errorText = this.FindControl<TextBlock>("ErrorText");
        _errorPanel = this.FindControl<StackPanel>("ErrorPanel");
        _validationBar = this.FindControl<ProgressBar>("ValidationBar");
        _confirmButton = this.FindControl<Button>("ConfirmButton");
        _revealButton = this.FindControl<Button>("RevealButton");
        _revealGlyph = this.FindControl<Avalonia.Controls.Shapes.Path>("RevealGlyph");

        if (_promptText is not null)
        {
            _promptText.Text = prompt;
        }

        if (_passwordBox is not null)
        {
            _passwordBox.KeyDown += OnPasswordKeyDown;
        }

        Opened += (_, _) => Dispatcher.UIThread.Post(() => _passwordBox?.Focus(), DispatcherPriority.Input);
    }

    private void OnHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private async void OnPasswordKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await SubmitAsync();
        }
    }

    private async void OnConfirmClick(object? sender, RoutedEventArgs e) => await SubmitAsync();

    private async Task SubmitAsync()
    {
        if (_isBusy)
        {
            return;
        }

        var password = _passwordBox?.Text ?? string.Empty;
        if (password.Length == 0)
        {
            ShowError(L10n.L("请输入密码后再确认。"));
            return;
        }

        if (_validator is null)
        {
            Close(password);
            return;
        }

        SetBusy(true);
        SevenZipStatus status;
        try
        {
            status = await _validator(password).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            SetBusy(false);
            ShowError(L10n.L("验证失败：") + ex.Message);
            return;
        }

        SetBusy(false);

        if (status is SevenZipStatus.Success or SevenZipStatus.Warning)
        {
            Close(password);
            return;
        }

        ShowError(status switch
        {
            SevenZipStatus.WrongPassword => L10n.L("密码错误，请重试。"),
            SevenZipStatus.PasswordRequired => L10n.L("该压缩包需要密码。"),
            SevenZipStatus.NotAnArchive => L10n.L("这不是受支持的压缩包，或文件已损坏。"),
            _ => L10n.L("无法用该密码打开压缩包，请重试。")
        });

        _passwordBox?.SelectAll();
        _passwordBox?.Focus();
    }

    private void OnRevealClick(object? sender, RoutedEventArgs e)
    {
        _isRevealed = !_isRevealed;

        if (_passwordBox is not null)
        {
            _passwordBox.PasswordChar = _isRevealed ? '\0' : '●';
        }

        if (_revealGlyph is not null && Application.Current is { } app)
        {
            _revealGlyph.Data = app.FindResource(_isRevealed ? "Icon.EyeOff" : "Icon.Eye") as Geometry;
        }

        if (_revealButton is not null)
        {
            ToolTip.SetTip(_revealButton, _isRevealed ? L10n.L("隐藏密码") : L10n.L("显示密码"));
        }
    }

    private void ShowError(string message)
    {
        if (_errorText is not null)
        {
            _errorText.Text = message;
        }

        if (_errorPanel is not null)
        {
            _errorPanel.IsVisible = true;
        }
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;

        if (_validationBar is not null)
        {
            _validationBar.IsVisible = busy;
        }

        if (_confirmButton is not null)
        {
            _confirmButton.IsEnabled = !busy;
        }

        if (_passwordBox is not null)
        {
            _passwordBox.IsEnabled = !busy;
        }

        if (busy && _errorPanel is not null)
        {
            _errorPanel.IsVisible = false;
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
