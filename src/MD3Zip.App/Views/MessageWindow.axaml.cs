using MD3Zip.Core;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace MD3Zip.App.Views;

/// <summary>
/// 信息 / 错误 / 确认窗口。
/// 默认只展示内容（「知道了」）；confirm = true 时变成「取消 / 确定」并返回 bool。
/// </summary>
public partial class MessageWindow : Window
{
    private readonly bool _confirm;

    public MessageWindow()
        : this(L10n.L("提示"), string.Empty, null)
    {
    }

    public MessageWindow(string title, string message, string? detail, bool confirm = false)
    {
        InitializeComponent();

        _confirm = confirm;
        Title = title;

        var titleText = this.FindControl<TextBlock>("TitleText");
        var messageText = this.FindControl<TextBlock>("MessageText");
        var detailText = this.FindControl<SelectableTextBlock>("DetailText");
        var detailBorder = this.FindControl<Border>("DetailBorder");
        var detailDivider = this.FindControl<Border>("DetailDivider");
        var okButton = this.FindControl<Button>("OkButton");
        var cancelButton = this.FindControl<Button>("CancelButton");

        if (titleText is not null) { titleText.Text = title; }
        if (messageText is not null) { messageText.Text = message; }

        var hasDetail = !string.IsNullOrWhiteSpace(detail);
        if (detailText is not null) { detailText.Text = detail ?? string.Empty; }
        if (detailBorder is not null) { detailBorder.IsVisible = hasDetail; }
        if (detailDivider is not null) { detailDivider.IsVisible = hasDetail; }

        if (confirm)
        {
            if (okButton is not null) { okButton.Content = L10n.L("确定"); }
            if (cancelButton is not null) { cancelButton.IsVisible = true; }
        }

        if (!hasDetail)
        {
            Height = 260;
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnOkClick(object? sender, RoutedEventArgs e) => Close(!_confirm);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    /// <summary>右上角关闭：确认模式下视为取消。</summary>
    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close(!_confirm);
}
