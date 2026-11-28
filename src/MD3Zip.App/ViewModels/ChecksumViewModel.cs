using System.Text;
using System.Windows.Input;
using MD3Zip.App.Infrastructure;
using MD3Zip.Core;
using MD3Zip.Core.Models;

namespace MD3Zip.App.ViewModels;

/// <summary>「文件校验」窗口的视图模型：算法多选、结果可复制、可与预期哈希比对。</summary>
public sealed class ChecksumViewModel : ObservableObject, IDisposable
{
    private readonly SevenZipService _service = new();
    private readonly List<HashRow> _rows = [];
    private CancellationTokenSource? _cts;

    private string? _targetPath;
    private bool _isCrc32 = true;
    private bool _isCrc64;
    private bool _isSha1;
    private bool _isSha256 = true;
    private bool _isBusy;
    private bool _hasResults;
    private bool _hasComparison;
    private bool _isMatch;
    private bool _isMismatch;
    private string _progressText = string.Empty;
    private string _resultsText = string.Empty;
    private string _expectedValue = string.Empty;
    private string _comparisonText = string.Empty;
    private string _errorText = string.Empty;

    public ChecksumViewModel()
    {
        ChooseTargetCommand = new AsyncRelayCommand(ChooseTargetAsync);
        SelectAllAlgorithmsCommand = new RelayCommand(() =>
        {
            IsCrc32 = true;
            IsCrc64 = true;
            IsSha1 = true;
            IsSha256 = true;
        });
        ComputeCommand = new AsyncRelayCommand(ComputeAsync);
        CancelCommand = new RelayCommand(Cancel);
        CompareCommand = new RelayCommand(Compare);
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke());
    }

    /// <summary>视图层注入的选文件能力（StorageProvider 只在窗口层可用）。</summary>
    public Func<Task<string?>>? PickTargetAsync { get; set; }

    public event Action? CloseRequested;

    public ICommand ChooseTargetCommand { get; }

    public ICommand SelectAllAlgorithmsCommand { get; }

    public ICommand ComputeCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand CompareCommand { get; }

    public ICommand CloseCommand { get; }

    public string? TargetPath
    {
        get => _targetPath;
        set
        {
            if (SetProperty(ref _targetPath, value))
            {
                OnPropertyChanged(nameof(TargetSummary));
                ResetResults();
            }
        }
    }

    public string TargetSummary => File.Exists(_targetPath)
        ? $"{System.IO.Path.GetFileName(_targetPath)} · {ArchiveEntry.FormatSize(new FileInfo(_targetPath!).Length)}"
        : L10n.L("尚未选择要校验的文件");

    public bool IsCrc32
    {
        get => _isCrc32;
        set => SetProperty(ref _isCrc32, value);
    }

    public bool IsCrc64
    {
        get => _isCrc64;
        set => SetProperty(ref _isCrc64, value);
    }

    public bool IsSha1
    {
        get => _isSha1;
        set => SetProperty(ref _isSha1, value);
    }

    public bool IsSha256
    {
        get => _isSha256;
        set => SetProperty(ref _isSha256, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsIdle));
            }
        }
    }

    public bool IsIdle => !_isBusy;

    public string ProgressText
    {
        get => _progressText;
        private set => SetProperty(ref _progressText, value);
    }

    public bool HasResults
    {
        get => _hasResults;
        private set => SetProperty(ref _hasResults, value);
    }

    public string ResultsText
    {
        get => _resultsText;
        private set => SetProperty(ref _resultsText, value);
    }

    public string ExpectedValue
    {
        get => _expectedValue;
        set
        {
            if (SetProperty(ref _expectedValue, value))
            {
                Compare();
            }
        }
    }

    public bool HasComparison
    {
        get => _hasComparison;
        private set => SetProperty(ref _hasComparison, value);
    }

    public bool IsMatch
    {
        get => _isMatch;
        private set => SetProperty(ref _isMatch, value);
    }

    public bool IsMismatch
    {
        get => _isMismatch;
        private set => SetProperty(ref _isMismatch, value);
    }

    public string ComparisonText
    {
        get => _comparisonText;
        private set => SetProperty(ref _comparisonText, value);
    }

    public string ErrorText
    {
        get => _errorText;
        private set
        {
            if (SetProperty(ref _errorText, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_errorText);

    /// <summary>默认校验目标 = 主窗口当前打开的文件。</summary>
    public void ApplyTarget(string? archivePath)
    {
        if (File.Exists(archivePath))
        {
            TargetPath = archivePath;
        }
    }

    private async Task ChooseTargetAsync()
    {
        if (PickTargetAsync is null)
        {
            return;
        }

        var path = await PickTargetAsync().ConfigureAwait(true);
        if (!string.IsNullOrEmpty(path))
        {
            TargetPath = path;
        }
    }

    private async Task ComputeAsync()
    {
        ErrorText = string.Empty;

        if (!File.Exists(TargetPath))
        {
            ResetResults();
            ErrorText = L10n.L("找不到该文件");
            return;
        }

        if (!_service.IsAvailable)
        {
            ErrorText = L10n.L("未检测到 7z.exe，无法计算校验和。");
            return;
        }

        var selected = SelectedAlgorithms();
        if (selected.Count == 0)
        {
            ErrorText = L10n.L("请至少选择一种校验算法。");
            return;
        }

        ResetResults();
        var cts = new CancellationTokenSource();
        _cts = cts;
        IsBusy = true;

        var rows = new List<HashRow>();
        try
        {
            // 逐个算法调用：实测 300 MB 样本单算法 0.2-0.3 s，而 -scrc* 因附带 BLAKE2sp 需 76 s，
            // 所以「全选」只是四个复选框的快捷勾选，不改成一次性多算法调用。
            foreach (var algorithm in selected)
            {
                ProgressText = ProgressLabel(algorithm);
                var result = await _service.ComputeHashesAsync(TargetPath!, algorithm, cts.Token)
                    .ConfigureAwait(true);

                if (!result.Succeeded || result.Hash is null)
                {
                    ErrorText = result.Message;
                    break;
                }

                rows.Add(new HashRow(result.AlgorithmName, result.Hash));
            }
        }
        finally
        {
            cts.Dispose();
            _cts = null;
            IsBusy = false;
            ProgressText = string.Empty;
        }

        if (rows.Count == 0)
        {
            if (string.IsNullOrEmpty(ErrorText))
            {
                ErrorText = L10n.L("校验和计算失败");
            }

            return;
        }

        _rows.Clear();
        _rows.AddRange(rows);
        ResultsText = Render(rows);
        HasResults = true;
        Compare();
    }

    private void Cancel()
    {
        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 已经结束。
        }
    }

    private void Compare()
    {
        var expected = Normalize(ExpectedValue);
        if (expected.Length == 0 || _rows.Count == 0)
        {
            HasComparison = false;
            IsMatch = false;
            IsMismatch = false;
            ComparisonText = string.Empty;
            return;
        }

        var hit = _rows.FirstOrDefault(row =>
            string.Equals(Normalize(row.Value), expected, StringComparison.OrdinalIgnoreCase));

        IsMatch = hit is not null;
        IsMismatch = hit is null;
        ComparisonText = hit is null ? L10n.L("不匹配") : MatchTextFor(hit.Algorithm);
        HasComparison = true;
    }

    private void ResetResults()
    {
        _rows.Clear();
        HasResults = false;
        ResultsText = string.Empty;
        HasComparison = false;
        IsMatch = false;
        IsMismatch = false;
        ComparisonText = string.Empty;
    }

    private List<HashAlgorithmKind> SelectedAlgorithms()
    {
        var selected = new List<HashAlgorithmKind>();
        if (IsCrc32)
        {
            selected.Add(HashAlgorithmKind.Crc32);
        }

        if (IsCrc64)
        {
            selected.Add(HashAlgorithmKind.Crc64);
        }

        if (IsSha1)
        {
            selected.Add(HashAlgorithmKind.Sha1);
        }

        if (IsSha256)
        {
            selected.Add(HashAlgorithmKind.Sha256);
        }

        return selected;
    }

    private static string Render(List<HashRow> rows)
    {
        var width = rows.Max(row => row.Algorithm.Length);
        var builder = new StringBuilder();

        foreach (var row in rows)
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(row.Algorithm.PadRight(width)).Append("  ").Append(row.Value);
        }

        return builder.ToString();
    }

    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (!char.IsWhiteSpace(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static string ProgressLabel(HashAlgorithmKind algorithm) => algorithm switch
    {
        HashAlgorithmKind.Crc32 => L10n.L("正在计算 CRC32…"),
        HashAlgorithmKind.Crc64 => L10n.L("正在计算 CRC64…"),
        HashAlgorithmKind.Sha1 => L10n.L("正在计算 SHA-1…"),
        _ => L10n.L("正在计算 SHA-256…")
    };

    private static string MatchTextFor(string algorithm) => algorithm switch
    {
        "CRC32" => L10n.L("匹配（CRC32）"),
        "CRC64" => L10n.L("匹配（CRC64）"),
        "SHA-1" => L10n.L("匹配（SHA-1）"),
        _ => L10n.L("匹配（SHA-256）")
    };

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }

    private sealed record HashRow(string Algorithm, string Value);
}
