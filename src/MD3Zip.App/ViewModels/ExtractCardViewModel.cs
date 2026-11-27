using System.Windows.Input;
using MD3Zip.App.Infrastructure;
using MD3Zip.Core;
using MD3Zip.Core.Models;

namespace MD3Zip.App.ViewModels;

/// <summary>
/// 迷你悬浮进度卡片的视图模型：右键菜单触发的静默解压。
/// </summary>
public sealed class ExtractCardViewModel : ObservableObject, IDisposable
{
    private readonly SevenZipService _service = new();
    private readonly string _archivePath;
    private readonly bool _smart;
    private CancellationTokenSource? _cts;

    private double _percent;
    private bool _isIndeterminate = true;
    private string _detailText = L10n.L("正在准备…");
    private string _statusText = string.Empty;
    private bool _isCompleted;
    private bool _hasError;

    public ExtractCardViewModel(string archivePath, bool smart)
    {
        _archivePath = archivePath;
        _smart = smart;

        CancelCommand = new RelayCommand(Cancel);

        FileName = Path.GetFileName(archivePath);
        TargetDirectory = Path.GetDirectoryName(archivePath) ?? Environment.CurrentDirectory;
        OperationText = smart ? L10n.L("智能解压") : L10n.L("解压到当前文件夹");
    }

    public event Action? CloseRequested;

    /// <summary>由视图层注入：弹 MD3 密码框并返回用户密码（validator 用于即时校验）。</summary>
    public Func<Func<string, Task<SevenZipStatus>>, Task<string?>>? PasswordPromptAsync { get; set; }

    public string FileName { get; }

    public string OperationText { get; }

    public string TargetDirectory { get; }

    public ICommand CancelCommand { get; }

    public double Percent
    {
        get => _percent;
        private set => SetProperty(ref _percent, value);
    }

    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        private set => SetProperty(ref _isIndeterminate, value);
    }

    public string DetailText
    {
        get => _detailText;
        private set => SetProperty(ref _detailText, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsCompleted
    {
        get => _isCompleted;
        private set
        {
            if (SetProperty(ref _isCompleted, value))
            {
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(ShowCancel));
            }
        }
    }

    public bool HasError
    {
        get => _hasError;
        private set
        {
            if (SetProperty(ref _hasError, value))
            {
                OnPropertyChanged(nameof(ShowCancel));
            }
        }
    }

    public bool IsRunning => !IsCompleted;

    public bool ShowCancel => !IsCompleted;

    public async Task StartAsync()
    {
        if (!_service.IsAvailable)
        {
            HasError = true;
            IsCompleted = true;
            DetailText = L10n.L("未检测到 7z.exe");
            StatusText = L10n.L("无法解压：请确认 Assets/7z 目录完整。");
            return;
        }

        _cts = new CancellationTokenSource();
        IsIndeterminate = true;
        DetailText = L10n.L("正在读取归档结构…");

        var progress = new Progress<ArchiveProgress>(p =>
        {
            IsIndeterminate = p.IsIndeterminate;
            if (!p.IsIndeterminate)
            {
                Percent = p.Percent;
            }

            if (!string.IsNullOrWhiteSpace(p.CurrentFile))
            {
                DetailText = p.CurrentFile!;
            }
        });

        string? password = null;
        var result = SevenZipResult.Fail(SevenZipStatus.Unknown, L10n.L("未执行"));
        SmartExtractPlan? plan = null;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (_smart)
            {
                var (smartResult, smartPlan) = await _service
                    .SmartExtractAsync(_archivePath, TargetDirectory, password, progress, _cts.Token)
                    .ConfigureAwait(true);
                result = smartResult;
                plan = smartPlan;
            }
            else
            {
                result = await _service.ExtractAsync(new ExtractionRequest
                {
                    ArchivePath = _archivePath,
                    TargetDirectory = TargetDirectory,
                    Password = password
                }, progress, null, _cts.Token).ConfigureAwait(true);
            }

            var needsPassword = result.Status is SevenZipStatus.PasswordRequired or SevenZipStatus.WrongPassword;
            if (result.Succeeded || !needsPassword || PasswordPromptAsync is null || _cts.IsCancellationRequested)
            {
                break;
            }

            // 加密包：停掉不确定进度，弹出 MD3 密码框；验证通过后带着密码重试。
            IsIndeterminate = true;
            var entered = await PasswordPromptAsync(async candidate =>
                    (await _service.ListAsync(_archivePath, candidate).ConfigureAwait(true)).Result.Status)
                .ConfigureAwait(true);

            if (string.IsNullOrEmpty(entered))
            {
                result = SevenZipResult.Fail(SevenZipStatus.Cancelled, L10n.L("已取消"));
                break;
            }

            password = entered;
            DetailText = L10n.L("正在使用新密码重试…");
        }

        if (plan is not null)
        {
            StatusText = plan.Reason;
        }

        _cts.Dispose();
        _cts = null;

        if (result.Succeeded)
        {
            Percent = 100;
            IsIndeterminate = false;
            IsCompleted = true;
            DetailText = Path.GetFileName(TargetDirectory);
            StatusText = string.IsNullOrEmpty(StatusText) ? L10n.L("解压完成") : L10n.L("解压完成 · ") + StatusText;

            await Task.Delay(1500).ConfigureAwait(true);
            CloseRequested?.Invoke();
            return;
        }

        IsCompleted = true;
        HasError = true;
        DetailText = L10n.L("解压失败");
        StatusText = result.Status == SevenZipStatus.Cancelled ? L10n.L("已取消") : result.Message;
    }

    private void Cancel()
    {
        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 已结束。
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
