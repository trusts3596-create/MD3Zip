using MD3Zip.Core;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using MD3Zip.App.Services;
using System.Globalization;

namespace MD3Zip.App.Localization;

/// <summary>
/// XAML 翻译扩展：Text="{loc:T 打开压缩包}"。
/// 返回绑定到 LocalizationService 的转换器，语言切换时随 Item[] 通知即时刷新。
/// </summary>
public sealed class TExtension : MarkupExtension
{
    public TExtension(string key) => Key = key;

    public TExtension() { }

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return new Binding(nameof(LocalizationService.Current))
        {
            Source = LocalizationService.Instance,
            Converter = TranslateConverter.Instance,
            ConverterParameter = Key,
            Mode = BindingMode.OneWay
        };
    }
}

internal sealed class TranslateConverter : IValueConverter
{
    public static readonly TranslateConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = parameter as string ?? value as string ?? string.Empty;
        return LocalizationService.Instance.Get(key);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;
}
