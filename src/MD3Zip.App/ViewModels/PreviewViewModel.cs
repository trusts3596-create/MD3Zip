using System.Diagnostics;
using System.Text;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using MD3Zip.App.Infrastructure;
using MD3Zip.App.Services;
using MD3Zip.Core;
using MD3Zip.Core.Models;

namespace MD3Zip.App.ViewModels;

/// <summary>双击条目后的轻量预览：图片直接解码，文本按 UTF-8 显示，其他类型交给系统默认程序。</summary>
public sealed class PreviewViewModel : ObservableObject
{
    private readonly SevenZipService _service = new();
    private readonly string _archivePath;
    private readonly ArchiveEntry _entry;
    private readonly string? _password;
    private readonly string _tempRoot;

    private string _textContent = string.Empty;
    private Bitmap? _image;
    private string _message = L10n.L("正在读取…");
    private bool _isText;
    private bool _isImage;
    private bool _isLoading = true;

    public PreviewViewModel(string archivePath, ArchiveEntry entry, string? password)
    {
        _archivePath = archivePath;
        _entry = entry;
        _password = password;
        _tempRoot = Path.Combine(Path.GetTempPath(), "MD3Zip", "preview", Guid.NewGuid().ToString("N"));

        OpenExternallyCommand = new AsyncRelayCommand(OpenExternallyAsync);

        Title = entry.Name;
        Subtitle = $"{ArchiveEntry.FormatSize(entry.Size)} · {entry.DisplayModified} · {FileKindService.DescribeKind(entry)}";
    }

    public string Title { get; }

    public string Subtitle { get; }

    public ICommand OpenExternallyCommand { get; }

    public string TextContent
    {
        get => _textContent;
        private set => SetProperty(ref _textContent, value);
    }

    public Bitmap? Image
    {
        get => _image;
        private set => SetProperty(ref _image, value);
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public bool IsText
    {
        get => _isText;
        private set
        {
            if (SetProperty(ref _isText, value))
            {
                OnPropertyChanged(nameof(ShowFallback));
            }
        }
    }

    public bool IsImage
    {
        get => _isImage;
        private set
        {
            if (SetProperty(ref _isImage, value))
            {
                OnPropertyChanged(nameof(ShowFallback));
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(ShowFallback));
            }
        }
    }

    public bool ShowFallback => !IsLoading && !IsText && !IsImage;

    public async Task LoadAsync()
    {
        IsLoading = true;

        var bytes = await _service.ReadEntryBytesAsync(_archivePath, _entry.Path, _password, 24 * 1024 * 1024)
            .ConfigureAwait(true);

        IsLoading = false;

        if (bytes is null)
        {
            Message = L10n.L("无法读取该条目：可能体积过大、已加密，或需要密码。");
            return;
        }

        if (FileKindService.IsImage(_entry.Name))
        {
            try
            {
                using var stream = new MemoryStream(bytes);
                Image = new Bitmap(stream);
                IsImage = true;
                return;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or InvalidOperationException)
            {
                Message = L10n.L("该图片格式无法直接解码，可用默认程序打开。");
                return;
            }
        }

        if (FileKindService.IsText(_entry.Name) || LooksLikeText(bytes))
        {
            TextContent = DecodeText(bytes);
            IsText = true;
            return;
        }

        Message = L10n.L("该类型不支持内置预览，可解压后使用系统默认程序打开。");
    }

    private async Task OpenExternallyAsync()
    {
        try
        {
            var result = await _service.ExtractAsync(new ExtractionRequest
            {
                ArchivePath = _archivePath,
                TargetDirectory = _tempRoot,
                Password = _password,
                SelectedEntries = [_entry.Path]
            }).ConfigureAwait(true);

            if (!result.Succeeded)
            {
                Message = result.Message;
                return;
            }

            var extracted = Path.Combine(_tempRoot, _entry.Path.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(extracted))
            {
                Process.Start(new ProcessStartInfo(extracted) { UseShellExecute = true });
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Message = $"打开失败：{ex.Message}";
        }
    }

    private static bool LooksLikeText(byte[] bytes)
    {
        var limit = Math.Min(bytes.Length, 4096);
        for (var i = 0; i < limit; i++)
        {
            if (bytes[i] == 0)
            {
                return false;
            }
        }

        return true;
    }

    private static string DecodeText(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        return text.Length > 200_000 ? text[..200_000] + "\n\n…（内容过长，仅显示前 200000 个字符）" : text;
    }
}
