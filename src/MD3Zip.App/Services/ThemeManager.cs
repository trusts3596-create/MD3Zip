using Avalonia;
using Avalonia.Styling;
using Material.Styles.Themes;
using Material.Styles.Themes.Base;

namespace MD3Zip.App.Services;

/// <summary>
/// 深色/浅色模式切换：同时驱动 Avalonia 主题变量与 Material 基础主题，
/// 保证 MD3 令牌与控件样式始终一致。
/// </summary>
public static class ThemeManager
{
    private static MaterialThemeBase? _materialTheme;

    public static bool IsDark { get; private set; } = true;

    public static event EventHandler<bool>? Changed;

    public static void Attach(MaterialThemeBase? materialTheme)
    {
        _materialTheme = materialTheme;
        Apply(IsDark);
    }

    public static void Toggle() => Apply(!IsDark);

    public static void Apply(bool dark)
    {
        IsDark = dark;

        if (Application.Current is { } application)
        {
            application.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        }

        if (_materialTheme is not null)
        {
            _materialTheme.BaseTheme = dark ? BaseThemeMode.Dark : BaseThemeMode.Light;
        }

        Changed?.Invoke(null, dark);
    }
}
