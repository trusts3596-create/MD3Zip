using MD3Zip.Core;
using System.ComponentModel;
using System.Text.Json;

namespace MD3Zip.Core;

/// <summary>
/// 中英双语服务（Core 层，App / Setup 共用）。以中文原文作 key，英文词典缺失时回退中文。
/// XAML 绑定 Current 属性 + 转换器，切换语言时即时刷新。
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    private static readonly Lazy<LocalizationService> Lazy = new(() => new LocalizationService());
    public static LocalizationService Instance => Lazy.Value;

    private readonly Dictionary<string, string> _english = new(StringComparer.Ordinal);
    private string _language = "zh";

    private LocalizationService()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Localization", "en-US.json");
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    _english[prop.Name] = prop.Value.GetString() ?? prop.Name;
                }
            }
        }
        catch (Exception)
        {
            // 词典损坏时退回中文。
        }

        _language = DetectInitialLanguage();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Language
    {
        get => _language;
        private set
        {
            if (_language == value) return;
            _language = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Current)));
        }
    }

    public string Current => _language;

    public bool IsEnglish => _language == "en";

    public string this[string key] => Get(key);

    public string Get(string text)
    {
        if (string.IsNullOrEmpty(text) || _language != "en") return text;
        return _english.TryGetValue(text, out var value) ? value : text;
    }

    public void SetLanguage(string language) => Language = language == "en" ? "en" : "zh";

    public void Toggle() => SetLanguage(_language == "zh" ? "en" : "zh");

    private static string DetectInitialLanguage()
    {
        try
        {
            var settings = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MD3Zip", "settings.json");
            if (File.Exists(settings))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(settings));
                if (doc.RootElement.TryGetProperty("language", out var lang) &&
                    lang.GetString() is "zh" or "en")
                {
                    return lang.GetString()!;
                }
            }
        }
        catch (Exception)
        {
            // 配置损坏时按系统区域判断。
        }

        var culture = System.Globalization.CultureInfo.CurrentUICulture.Name;
        return culture.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh" : "en";
    }

    public void Save()
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MD3Zip");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "settings.json"),
                JsonSerializer.Serialize(new { language = _language }));
        }
        catch (Exception)
        {
            // 保存失败不影响本次会话。
        }
    }
}

/// <summary>静态快捷入口。</summary>
public static class L10n
{
    public static string L(string text) => LocalizationService.Instance.Get(text);
}
