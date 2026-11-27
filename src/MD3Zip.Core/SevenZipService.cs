using MD3Zip.Core;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MD3Zip.Core.Models;

namespace MD3Zip.Core;

public sealed class ArchiveListingResult
{
    public required SevenZipResult Result { get; init; }

    public ArchiveListing? Listing { get; init; }

    public bool Succeeded => Result.Succeeded && Listing is not null;
}

/// <summary>
/// 7z.exe 的异步调度引擎：负责参数拼装、实时进度解析、进程树终止与错误语义化。
/// 不实现任何压缩算法，全部委托给内置的 7z.exe。
/// </summary>
public sealed class SevenZipService
{
    /// <summary>
    /// 百分比之后的部分：` 12 - folder\file.bin` 或仅 ` 12`。
    /// </summary>
    private static readonly Regex ProgressTailRegex = new(
        @"^(?<done>\d+)?\s*(?:[-+*]\s*(?<name>.*))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private const int MaxOutputLength = 96 * 1024;
    private const int MaxDirectArgumentLength = 28000;

    private readonly string _executablePath;

    public SevenZipService(string? executablePath = null)
    {
        _executablePath = executablePath ?? SevenZipLocator.Locate() ?? string.Empty;
    }

    public string ExecutablePath => _executablePath;

    public bool IsAvailable => !string.IsNullOrEmpty(_executablePath) && File.Exists(_executablePath);

    /// <summary>最近一次 7z 调用的原始输出，便于排查问题或展示“详情”。</summary>
    public string LastRawOutput { get; private set; } = string.Empty;

    /// <summary>读取压缩包目录（-slt 键值块解析为目录树）。</summary>
    public async Task<ArchiveListingResult> ListAsync(string archivePath, string? password = null, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            return new ArchiveListingResult { Result = EngineMissingResult() };
        }

        if (!File.Exists(archivePath))
        {
            return new ArchiveListingResult
            {
                Result = SevenZipResult.Fail(SevenZipStatus.FileNotFound, $"找不到压缩包：{archivePath}")
            };
        }

        var arguments = new List<string> { "l", "-slt", "-sccUTF-8", "-bd", "-y" };
        var passwordProvided = !string.IsNullOrEmpty(password);

        // 关键：无密码时必须显式传 -p-，否则 7z 会在控制台等待交互输入而永久挂起。
        arguments.Add(passwordProvided ? "-p" + password : "-p-");
        arguments.Add(archivePath);

        var run = await RunAsync(arguments, Path.GetDirectoryName(archivePath), null, L10n.L("正在读取归档目录"), null, cancellationToken)
            .ConfigureAwait(false);
        var result = Classify(run, passwordProvided);

        if (!result.Succeeded)
        {
            return new ArchiveListingResult { Result = result };
        }

        var listing = ArchiveListing.Parse(run.Output, archivePath);
        return new ArchiveListingResult
        {
            Result = SevenZipResult.Ok(run.ExitCode, $"共 {listing.FileCount} 个文件、{listing.DirectoryCount} 个目录"),
            Listing = listing
        };
    }

    /// <summary>解压整个压缩包或其中选中的条目。</summary>
    public async Task<SevenZipResult> ExtractAsync(
        ExtractionRequest request,
        IProgress<ArchiveProgress>? progress = null,
        long? totalEntries = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            return EngineMissingResult();
        }

        if (!File.Exists(request.ArchivePath))
        {
            return SevenZipResult.Fail(SevenZipStatus.FileNotFound, $"找不到压缩包：{request.ArchivePath}");
        }

        try
        {
            Directory.CreateDirectory(request.TargetDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return SevenZipResult.Fail(SevenZipStatus.AccessDenied, $"无法创建目标目录：{ex.Message}");
        }

        var arguments = new List<string>
        {
            "x",
            "-o" + request.TargetDirectory,
            "-y",
            OverwriteSwitch(request.OverwriteMode),
            "-bsp1",
            "-sccUTF-8"
        };

        var passwordProvided = !string.IsNullOrEmpty(request.Password);

        // 同上：不给密码时传 -p-，保证 7z 立刻失败而不是等待输入。
        arguments.Add(passwordProvided ? "-p" + request.Password : "-p-");
        arguments.Add(request.ArchivePath);
        foreach (var entry in request.SelectedEntries)
        {
            var normalized = ArchivePathUtil.Normalize(entry);
            if (!string.IsNullOrEmpty(normalized))
            {
                arguments.Add(normalized);
            }
        }

        var run = await RunAsync(arguments, Path.GetDirectoryName(request.ArchivePath), progress, L10n.L("正在解压"), totalEntries, cancellationToken)
            .ConfigureAwait(false);
        return Classify(run, passwordProvided);
    }

    /// <summary>
    /// 智能解压：
    /// A. 根目录只有唯一文件夹 → 直接解压到目标父目录（不套娃）；
    /// B. 根目录散落多个条目 → 以压缩包名新建子目录后解压。
    /// </summary>
    public async Task<(SevenZipResult Result, SmartExtractPlan? Plan)> SmartExtractAsync(
        string archivePath,
        string targetRootDirectory,
        string? password = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var listing = await ListAsync(archivePath, password, cancellationToken).ConfigureAwait(false);
        if (!listing.Succeeded || listing.Listing is null)
        {
            return (listing.Result, null);
        }

        var roots = listing.Listing.Roots;
        var singleFolder = roots.Count == 1 && roots[0].IsDirectory;

        var plan = new SmartExtractPlan
        {
            ArchivePath = archivePath,
            TargetDirectory = targetRootDirectory,
            ExtractedDirectly = singleFolder,
            SubfolderName = singleFolder ? null : SanitizeFolderName(Path.GetFileNameWithoutExtension(archivePath)),
            Reason = singleFolder
                ? $"根目录仅包含文件夹“{roots[0].Name}”，直接解压到当前目录以避免重复嵌套。"
                : $"根目录包含 {roots.Count} 个条目，自动创建同名文件夹后解压。"
        };

        if (!singleFolder)
        {
            plan.TargetDirectory = Path.Combine(targetRootDirectory, plan.SubfolderName!);
        }

        var result = await ExtractAsync(
            new ExtractionRequest
            {
                ArchivePath = archivePath,
                TargetDirectory = plan.TargetDirectory,
                Password = password
            },
            progress,
            listing.Listing.FileCount,
            cancellationToken).ConfigureAwait(false);

        return (result, plan);
    }

    /// <summary>创建压缩包。</summary>
    public async Task<SevenZipResult> CompressAsync(
        CompressionRequest request,
        IProgress<ArchiveProgress>? progress = null,
        long? totalEntries = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            return EngineMissingResult();
        }

        if (request.Sources.Count == 0)
        {
            return SevenZipResult.Fail(SevenZipStatus.FileNotFound, L10n.L("没有需要压缩的文件或文件夹。"));
        }

        // gz / bz2 / xz 是单文件流式格式，7z 会拒绝目录或多文件输入。
        if (request.IsSingleFileFormat
            && (request.Sources.Count != 1 || !File.Exists(request.Sources[0])))
        {
            return SevenZipResult.Fail(
                SevenZipStatus.Unknown,
                L10n.L("gz / bz2 / xz 是单文件格式，只能压缩一个文件；多个项目请改用 7z 或 zip。"));
        }

        foreach (var source in request.Sources)
        {
            if (!File.Exists(source) && !Directory.Exists(source))
            {
                return SevenZipResult.Fail(SevenZipStatus.FileNotFound, $"源路径不存在：{source}");
            }
        }

        try
        {
            var outputDirectory = Path.GetDirectoryName(request.OutputPath);
            if (!string.IsNullOrEmpty(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return SevenZipResult.Fail(SevenZipStatus.AccessDenied, $"无法创建输出目录：{ex.Message}");
        }

        var arguments = new List<string>
        {
            "a",
            "-t" + request.FormatSwitch,
            "-bsp1",
            "-sccUTF-8",
            "-y"
        };

        if (request.SupportsLevel)
        {
            arguments.Add("-mx" + (int)request.Level);
            arguments.Add(request.Threads > 0 ? $"-mmt={request.Threads}" : "-mmt=on");
        }

        if (request.SupportsEncryption && !string.IsNullOrEmpty(request.Password))
        {
            arguments.Add("-p" + request.Password);
            arguments.Add(request.Format == ArchiveFormat.Zip ? "-mem=AES256" : "-mhe=on");
        }

        if (request.SupportsVolume && !string.IsNullOrWhiteSpace(request.VolumeSize))
        {
            arguments.Add("-v" + NormalizeVolumeSize(request.VolumeSize));
        }

        arguments.Add(request.OutputPath);

        string? listFile = null;
        try
        {
            var sourcesLength = request.Sources.Sum(s => s.Length + 3);
            if (sourcesLength > MaxDirectArgumentLength)
            {
                listFile = await WriteListFileAsync(request.Sources, cancellationToken).ConfigureAwait(false);
                arguments.Add("-scsUTF-8");
                arguments.Add("@" + listFile);
            }
            else
            {
                arguments.AddRange(request.Sources);
            }

            var run = await RunAsync(
                    arguments,
                    request.WorkingDirectory ?? Path.GetDirectoryName(request.Sources[0]),
                    progress,
                    L10n.L("正在压缩"),
                    totalEntries,
                    cancellationToken)
                .ConfigureAwait(false);

            var result = Classify(run, !string.IsNullOrEmpty(request.Password));
            if (result.Succeeded && request.DeleteSourcesAfter)
            {
                DeleteSources(request.Sources);
            }

            return result;
        }
        finally
        {
            if (listFile is not null)
            {
                TryDelete(listFile);
            }
        }
    }

    /// <summary>读取单个条目的字节（用于文本/图片预览）。</summary>
    public async Task<byte[]?> ReadEntryBytesAsync(
        string archivePath,
        string internalPath,
        string? password = null,
        int maxBytes = 32 * 1024 * 1024,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable || !File.Exists(archivePath))
        {
            return null;
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), "MD3Zip", "preview", Guid.NewGuid().ToString("N"));
        try
        {
            var result = await ExtractAsync(
                new ExtractionRequest
                {
                    ArchivePath = archivePath,
                    TargetDirectory = tempDirectory,
                    Password = password,
                    SelectedEntries = [ArchivePathUtil.Normalize(internalPath)],
                    OverwriteMode = ExtractionOverwriteMode.Overwrite
                },
                null,
                null,
                cancellationToken).ConfigureAwait(false);

            if (!result.Succeeded)
            {
                return null;
            }

            var extracted = ArchivePathUtil.ToAbsolute(tempDirectory, internalPath);
            if (!File.Exists(extracted))
            {
                return null;
            }

            var info = new FileInfo(extracted);
            if (info.Length > maxBytes)
            {
                return null;
            }

            return await File.ReadAllBytesAsync(extracted, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
        finally
        {
            TryDeleteDirectory(tempDirectory);
        }
    }

    /// <summary>测试压缩包完整性（7z t）。</summary>
    public async Task<SevenZipResult> TestAsync(
        string archivePath,
        string? password = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            return EngineMissingResult();
        }

        if (!File.Exists(archivePath))
        {
            return SevenZipResult.Fail(SevenZipStatus.FileNotFound, $"找不到压缩包：{archivePath}");
        }

        var arguments = new List<string> { "t", "-bsp1", "-sccUTF-8", "-y" };
        var passwordProvided = !string.IsNullOrEmpty(password);
        arguments.Add(passwordProvided ? "-p" + password : "-p-");
        arguments.Add(archivePath);

        var run = await RunAsync(arguments, Path.GetDirectoryName(archivePath), progress, L10n.L("正在测试压缩包"), null, cancellationToken)
            .ConfigureAwait(false);
        var result = Classify(run, passwordProvided);

        return result.Succeeded
            ? SevenZipResult.Ok(run.ExitCode, BuildTestSummary(run.Output))
            : result;
    }

    /// <summary>从压缩包中删除指定条目（7z d）。</summary>
    public async Task<SevenZipResult> DeleteEntriesAsync(
        string archivePath,
        IReadOnlyList<string> entries,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            return EngineMissingResult();
        }

        if (!File.Exists(archivePath))
        {
            return SevenZipResult.Fail(SevenZipStatus.FileNotFound, $"找不到压缩包：{archivePath}");
        }

        if (entries.Count == 0)
        {
            return SevenZipResult.Fail(SevenZipStatus.Unknown, L10n.L("没有选中要删除的条目。"));
        }

        var arguments = new List<string> { "d", "-sccUTF-8", "-y" };
        var passwordProvided = !string.IsNullOrEmpty(password);
        arguments.Add(passwordProvided ? "-p" + password : "-p-");
        arguments.Add(archivePath);
        foreach (var entry in entries)
        {
            var normalized = ArchivePathUtil.Normalize(entry);
            if (!string.IsNullOrEmpty(normalized))
            {
                arguments.Add(normalized);
            }
        }

        var run = await RunAsync(arguments, Path.GetDirectoryName(archivePath), null, L10n.L("正在删除条目"), null, cancellationToken)
            .ConfigureAwait(false);
        var result = Classify(run, passwordProvided);

        return result.Succeeded
            ? SevenZipResult.Ok(run.ExitCode, $"已从压缩包删除 {entries.Count} 个条目。")
            : result;
    }

    /// <summary>向已有压缩包追加或更新条目（7z u）。</summary>
    public async Task<SevenZipResult> UpdateAsync(
        UpdateRequest request,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            return EngineMissingResult();
        }

        if (!File.Exists(request.ArchivePath))
        {
            return SevenZipResult.Fail(SevenZipStatus.FileNotFound, $"找不到压缩包：{request.ArchivePath}");
        }

        if (request.Sources.Count == 0)
        {
            return SevenZipResult.Fail(SevenZipStatus.FileNotFound, L10n.L("没有需要添加的文件或文件夹。"));
        }

        foreach (var source in request.Sources)
        {
            if (!File.Exists(source) && !Directory.Exists(source))
            {
                return SevenZipResult.Fail(SevenZipStatus.FileNotFound, $"源路径不存在：{source}");
            }
        }

        // u 命令沿用归档已有格式，不能再传 -t，否则可能把 zip 改写成 7z。
        var arguments = new List<string> { "u", "-bsp1", "-sccUTF-8", "-y", "-mx" + (int)request.Level };
        var passwordProvided = !string.IsNullOrEmpty(request.Password);
        arguments.Add(passwordProvided ? "-p" + request.Password : "-p-");
        arguments.Add(request.ArchivePath);

        string? listFile = null;
        try
        {
            var sourcesLength = request.Sources.Sum(s => s.Length + 3);
            if (sourcesLength > MaxDirectArgumentLength)
            {
                listFile = await WriteListFileAsync(request.Sources, cancellationToken).ConfigureAwait(false);
                arguments.Add("-scsUTF-8");
                arguments.Add("@" + listFile);
            }
            else
            {
                arguments.AddRange(request.Sources);
            }

            var run = await RunAsync(
                    arguments,
                    request.WorkingDirectory ?? Path.GetDirectoryName(request.Sources[0]),
                    progress,
                    L10n.L("正在更新压缩包"),
                    null,
                    cancellationToken)
                .ConfigureAwait(false);
            var result = Classify(run, passwordProvided);

            return result.Succeeded
                ? SevenZipResult.Ok(run.ExitCode, $"已向压缩包追加 {request.Sources.Count} 个来源。")
                : result;
        }
        finally
        {
            if (listFile is not null)
            {
                TryDelete(listFile);
            }
        }
    }

    /// <summary>计算文件校验和（7z h -scrc*），支持 CRC32 / SHA-1 / SHA-256。</summary>
    public async Task<HashResult> ComputeHashesAsync(
        string filePath,
        HashAlgorithmKind algorithm = HashAlgorithmKind.Sha256,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            return new HashResult
            {
                Algorithm = algorithm,
                Message = L10n.L("未检测到 7z.exe，无法计算校验和。")
            };
        }

        if (!File.Exists(filePath))
        {
            return new HashResult { Algorithm = algorithm, Message = $"找不到文件：{filePath}" };
        }

        var algorithmSwitch = algorithm switch
        {
            HashAlgorithmKind.Crc32 => "-scrcCRC32",
            HashAlgorithmKind.Sha1 => "-scrcSHA1",
            _ => "-scrcSHA256"
        };

        var arguments = new List<string> { "h", algorithmSwitch, "-sccUTF-8", "-y", filePath };
        var run = await RunAsync(arguments, Path.GetDirectoryName(filePath), null, L10n.L("正在计算校验和"), null, cancellationToken)
            .ConfigureAwait(false);

        if (run.StartError is not null)
        {
            return new HashResult { Algorithm = algorithm, Message = run.StartError, RawOutput = run.Output };
        }

        if (run.ExitCode != 0)
        {
            return new HashResult
            {
                Algorithm = algorithm,
                Message = FirstDiagnostic(run.Output) ?? $"7z 退出码 {run.ExitCode}。",
                RawOutput = run.Output
            };
        }

        var (hash, size) = ParseHashLine(run.Output, algorithm);
        return hash is null
            ? new HashResult
            {
                Algorithm = algorithm,
                Message = L10n.L("未能从 7z 输出中解析出校验和。"),
                RawOutput = run.Output
            }
            : new HashResult
            {
                Succeeded = true,
                Algorithm = algorithm,
                Hash = hash,
                Size = size,
                Message = $"{AlgorithmDisplayName(algorithm)} 计算完成。",
                RawOutput = run.Output
            };
    }

    private async Task<ProcessRunResult> RunAsync(
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        IProgress<ArchiveProgress>? progress,
        string? operation,
        long? totalEntries,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // 重定向 stdin 并在启动后立刻关闭：7z 遇到加密头会等待用户输入密码，
            // 无人值守的进程里会永久阻塞，关掉 stdin 让它直接报错返回。
            RedirectStandardInput = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };

        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        if (!string.IsNullOrEmpty(workingDirectory) && Directory.Exists(workingDirectory))
        {
            psi.WorkingDirectory = workingDirectory;
        }

        var output = new StringBuilder();
        var gate = new object();

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // 进程已经退出。
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // 无权终止时忽略，等待自然退出。
            }
        });

        try
        {
            if (!process.Start())
            {
                return ProcessRunResult.FromStartError(L10n.L("无法启动 7z 进程。"));
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return ProcessRunResult.FromStartError($"无法启动 7z：{ex.Message}");
        }

        try
        {
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // stdin 已经关闭时忽略。
        }

        var stdOut = PumpAsync(process.StandardOutput, output, gate, progress, operation, totalEntries);
        var stdErr = PumpAsync(process.StandardError, output, gate, progress, null, null);

        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        await Task.WhenAll(stdOut, stdErr).ConfigureAwait(false);

        LastRawOutput = output.ToString();

        return new ProcessRunResult
        {
            ExitCode = process.ExitCode,
            Output = output.ToString(),
            Cancelled = cancellationToken.IsCancellationRequested
        };
    }

    /// <summary>
    /// 逐字符读取输出流：把 '\b' 当作退格处理，'\r'/'\n' 作为行结束，
    /// 从而把 7z 的“原地刷新”进度清洗成可解析的独立行。
    /// </summary>
    private static async Task PumpAsync(
        StreamReader reader,
        StringBuilder output,
        object gate,
        IProgress<ArchiveProgress>? progress,
        string? operation,
        long? totalEntries)
    {
        var buffer = new char[4096];
        var line = new StringBuilder(256);

        void Flush()
        {
            var text = line.ToString();
            line.Clear();
            HandleLine(text, output, gate, progress, operation, totalEntries);
        }

        int read;
        var lastWasCarriageReturn = false;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), CancellationToken.None).ConfigureAwait(false)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                var ch = buffer[i];
                switch (ch)
                {
                    case '\r':
                        // 7z 的进度用 '\r' 回到行首，因此 '\r' 也要当作行结束。
                        Flush();
                        lastWasCarriageReturn = true;
                        break;
                    case '\n':
                        // 紧跟在 '\r' 之后的 '\n' 属于同一个 CRLF，不重复断行。
                        if (!lastWasCarriageReturn)
                        {
                            Flush();
                        }

                        lastWasCarriageReturn = false;
                        break;
                    case '\b':
                        if (line.Length > 0)
                        {
                            line.Length--;
                        }

                        lastWasCarriageReturn = false;
                        break;
                    case '\0':
                        break;
                    default:
                        lastWasCarriageReturn = false;
                        line.Append(ch);
                        if (line.Length > 8192)
                        {
                            Flush();
                        }

                        break;
                }
            }
        }

        Flush();
    }

    private static void HandleLine(
        string text,
        StringBuilder output,
        object gate,
        IProgress<ArchiveProgress>? progress,
        string? operation,
        long? totalEntries)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            // 关键：-slt 的条目以空行分隔，空行必须保留，否则所有块会粘连成一条记录。
            lock (gate)
            {
                var len = output.Length;
                if (len < 2 || output[len - 1] != '\n' || output[len - 2] != '\n')
                {
                    output.AppendLine();
                }
            }

            return;
        }

        // 仅在调用方关心进度时才做进度解析，避免文件名/参数中的 '%' 被误吞。
        if (progress is not null && TryParseProgress(trimmed, out var percent, out var processed, out var currentFile))
        {
            progress?.Report(new ArchiveProgress
            {
                Percent = Math.Clamp(percent, 0, 100),
                ProcessedEntries = processed,
                CurrentFile = currentFile,
                Operation = operation,
                TotalEntries = totalEntries
            });
            return;
        }

        lock (gate)
        {
            output.AppendLine(trimmed);
            if (output.Length > MaxOutputLength)
            {
                output.Remove(0, output.Length - (MaxOutputLength * 3 / 4));
            }
        }
    }

    /// <summary>
    /// 解析 7z -bsp1 的进度片段。7z 会用 '\b' 原地擦除，若擦除不彻底，
    /// 一行里可能残留多个百分比，此时以“最后一个百分比”为准。
    /// </summary>
    private static bool TryParseProgress(string text, out double percent, out long? processed, out string? currentFile)
    {
        percent = -1;
        processed = null;
        currentFile = null;

        var percentIndex = text.LastIndexOf('%');
        if (percentIndex <= 0)
        {
            return false;
        }

        var start = percentIndex;
        while (start > 0 && char.IsDigit(text[start - 1]))
        {
            start--;
        }

        var digits = text[start..percentIndex];
        if (digits.Length is 0 or > 3 || !int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pct))
        {
            return false;
        }

        percent = pct;

        var rest = text[(percentIndex + 1)..].Trim();
        var match = ProgressTailRegex.Match(rest);
        if (!match.Success)
        {
            return true;
        }

        if (match.Groups["done"].Success
            && long.TryParse(match.Groups["done"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var done))
        {
            processed = done;
        }

        if (match.Groups["name"].Success)
        {
            var name = match.Groups["name"].Value.Trim();
            currentFile = name.Length == 0 ? null : name;
        }

        return true;
    }

    private static SevenZipResult Classify(ProcessRunResult run, bool passwordProvided = false)
    {
        if (run.StartError is not null)
        {
            return SevenZipResult.Fail(SevenZipStatus.EngineMissing, run.StartError);
        }

        if (run.Cancelled)
        {
            return SevenZipResult.Fail(SevenZipStatus.Cancelled, L10n.L("操作已取消。"), run.ExitCode, run.Output);
        }

        var output = run.Output;

        // 关闭 stdin 后 7z 会打印 "Enter password (will not be echoed):" 并以 255 退出，
        // 这与用户主动取消不可混为一谈，必须识别成“需要密码”。
        if (ContainsAny(output, "Enter password", "Wrong password", "Can not open encrypted archive", "Data Error in encrypted file", L10n.L("密码错误")))
        {
            // 同一个提示对应两种语义：没给过密码 = 需要密码；给过密码 = 密码错误。
            return passwordProvided
                ? SevenZipResult.Fail(SevenZipStatus.WrongPassword, L10n.L("密码错误，请重试。"), run.ExitCode, output)
                : SevenZipResult.Fail(SevenZipStatus.PasswordRequired, L10n.L("该压缩包已加密，请输入密码。"), run.ExitCode, output);
        }

        if (ContainsAny(output, "not enough space", "disk is full", "No space left", L10n.L("磁盘空间不足"), "There is not enough space"))
        {
            return SevenZipResult.Fail(SevenZipStatus.DiskFull, L10n.L("磁盘空间不足，请清理目标磁盘后重试。"), run.ExitCode, output);
        }

        if (ContainsAny(output, "being used by another process", "Access is denied", L10n.L("拒绝访问"), L10n.L("无法访问"), "Permission denied"))
        {
            return SevenZipResult.Fail(SevenZipStatus.AccessDenied, L10n.L("文件被占用或没有访问权限，请关闭相关程序后重试。"), run.ExitCode, output);
        }

        if (ContainsAny(output, "cannot find the file", "cannot find archive", "The system cannot find", L10n.L("找不到指定的文件"), "No more files"))
        {
            return SevenZipResult.Fail(SevenZipStatus.FileNotFound, L10n.L("找不到文件或压缩包。"), run.ExitCode, output);
        }

        if (ContainsAny(output, "Can not open the file as archive", "Is not archive", "Cannot open the file as archive", L10n.L("不是归档")))
        {
            return SevenZipResult.Fail(SevenZipStatus.NotAnArchive, L10n.L("该文件不是受支持的压缩包，或已损坏。"), run.ExitCode, output);
        }

        return run.ExitCode switch
        {
            0 => SevenZipResult.Ok(0, L10n.L("操作完成")),
            1 => new SevenZipResult
            {
                Status = SevenZipStatus.Warning,
                ExitCode = 1,
                Message = FirstDiagnostic(output) ?? L10n.L("操作完成，但有部分文件被跳过（警告）。"),
                RawOutput = output
            },
            255 => SevenZipResult.Fail(SevenZipStatus.Cancelled, L10n.L("操作已取消。"), 255, output),
            2 => SevenZipResult.Fail(SevenZipStatus.Unknown, FirstDiagnostic(output) ?? L10n.L("7z 报告了致命错误。"), 2, output),
            7 => SevenZipResult.Fail(SevenZipStatus.Unknown, FirstDiagnostic(output) ?? L10n.L("7z 命令行参数错误。"), 7, output),
            8 => SevenZipResult.Fail(SevenZipStatus.Unknown, L10n.L("内存不足，无法完成操作。"), 8, output),
            _ => SevenZipResult.Fail(SevenZipStatus.Unknown, FirstDiagnostic(output) ?? $"7z 退出码 {run.ExitCode}。", run.ExitCode, output)
        };
    }

    private static string? FirstDiagnostic(string output)
    {
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.Contains("ERROR", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Can not", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Cannot", StringComparison.OrdinalIgnoreCase)
                || line.Contains(L10n.L("错误"), StringComparison.Ordinal))
            {
                return line.Length > 300 ? line[..300] : line;
            }
        }

        return null;
    }

    private static bool ContainsAny(string haystack, params string[] needles) =>
        needles.Any(n => haystack.Contains(n, StringComparison.OrdinalIgnoreCase));

    private static SevenZipResult EngineMissingResult() => SevenZipResult.Fail(
        SevenZipStatus.EngineMissing,
        L10n.L("未检测到 7z.exe。请确认 Assets/7z/7z.exe 与 7z.dll 存在，或在本机安装 7-Zip 后重新启动。"));

    private static string OverwriteSwitch(ExtractionOverwriteMode mode) => mode switch
    {
        ExtractionOverwriteMode.Skip => "-aos",
        ExtractionOverwriteMode.Rename => "-aou",
        _ => "-aoa"
    };

    private static string BuildTestSummary(string output)
    {
        string? files = null;
        string? folders = null;
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("Files:", StringComparison.OrdinalIgnoreCase))
            {
                files = line;
            }
            else if (line.StartsWith("Folders:", StringComparison.OrdinalIgnoreCase))
            {
                folders = line;
            }
        }

        if (files is null && folders is null)
        {
            return L10n.L("压缩包完整，未发现错误。");
        }

        var detail = string.Join("，", new[] { files, folders }.Where(s => !string.IsNullOrEmpty(s)));
        return $"测试通过：{detail}";
    }

    private static (string? Hash, long Size) ParseHashLine(string output, HashAlgorithmKind algorithm)
    {
        var expectedLength = algorithm switch
        {
            HashAlgorithmKind.Crc32 => 8,
            HashAlgorithmKind.Sha1 => 40,
            _ => 64
        };

        string? hash = null;
        long size = 0;

        foreach (var rawLine in output.Split('\n'))
        {
            var parts = rawLine.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2
                || parts[0].Length != expectedLength
                || !parts[0].All(Uri.IsHexDigit)
                || !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSize))
            {
                continue;
            }

            hash = parts[0];
            size = parsedSize;
        }

        return (hash, size);
    }

    private static string AlgorithmDisplayName(HashAlgorithmKind algorithm) => algorithm switch
    {
        HashAlgorithmKind.Crc32 => "CRC32",
        HashAlgorithmKind.Sha1 => "SHA-1",
        _ => "SHA-256"
    };

    /// <summary>压缩成功后删除源文件 / 文件夹（失败不影响压缩结果）。</summary>
    private static void DeleteSources(IEnumerable<string> sources)
    {
        foreach (var path in sources)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                else if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch (IOException)
            {
                // 忽略删除失败。
            }
            catch (UnauthorizedAccessException)
            {
                // 同上。
            }
        }
    }

    private static string NormalizeVolumeSize(string volume)
    {
        var value = volume.Trim().ToLowerInvariant()
            .Replace(" ", string.Empty)
            .Replace("b", string.Empty);
        return value;
    }

    private static string SanitizeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrEmpty(cleaned) ? L10n.L("解压文件") : cleaned;
    }

    private static async Task<string> WriteListFileAsync(IEnumerable<string> paths, CancellationToken cancellationToken)
    {
        var file = Path.Combine(Path.GetTempPath(), $"md3zip-list-{Guid.NewGuid():N}.txt");
        var content = new StringBuilder();
        foreach (var path in paths)
        {
            // 7z 列表文件：包含空格或特殊字符的路径使用双引号包裹。
            content.Append('"').Append(path.Replace("\"", "\"\"")).Append('"').Append('\n');
        }

        await File.WriteAllTextAsync(file, content.ToString(), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        return file;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // 临时文件清理失败不影响主流程。
        }
        catch (UnauthorizedAccessException)
        {
            // 同上。
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // 忽略清理失败。
        }
        catch (UnauthorizedAccessException)
        {
            // 忽略清理失败。
        }
    }

    private sealed class ProcessRunResult
    {
        public int ExitCode { get; init; }

        public string Output { get; init; } = string.Empty;

        public bool Cancelled { get; init; }

        public string? StartError { get; init; }

        public static ProcessRunResult FromStartError(string message) => new() { StartError = message, ExitCode = -1 };
    }
}
