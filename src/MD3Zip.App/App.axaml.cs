using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Material.Styles.Themes;
using MD3Zip.App.Services;
using MD3Zip.App.Views;

namespace MD3Zip.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // Material 主题实例由 XAML 创建，这里取回来以便运行时切换深浅色。
        ThemeManager.Attach(Styles.OfType<MaterialThemeBase>().FirstOrDefault());

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var options = Program.Options;

            var isQuickMode = options.Mode is LaunchMode.ExtractHere or LaunchMode.SmartExtract;
            if (isQuickMode && !string.IsNullOrWhiteSpace(options.ArchivePath) && File.Exists(options.ArchivePath))
            {
                // 模式 2/3：只弹迷你悬浮卡片，不加载主窗口（保证右键点击秒开）。
                var card = new ExtractCardWindow(options.ArchivePath!, options.Mode == LaunchMode.SmartExtract);
                card.Closed += (_, _) => desktop.Shutdown();
                desktop.MainWindow = card;
                card.Show();
            }
            else if (options.Mode == LaunchMode.CompressDialog)
            {
                // 模式 4：直接弹出压缩配置对话框。
                var compress = new CompressWindow(options.Sources);
                compress.Closed += (_, _) => desktop.Shutdown();
                desktop.MainWindow = compress;
                compress.Show();
            }
            else
            {
                var main = new MainWindow(options.Mode == LaunchMode.Browser ? options.ArchivePath : null, options.Password);
                main.Closed += (_, _) => desktop.Shutdown();
                desktop.MainWindow = main;
                main.Show();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
