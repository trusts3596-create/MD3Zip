using Avalonia.Media;
using MD3Zip.Core.Models;

namespace MD3Zip.App.Services;

/// <summary>
/// 主窗口需要向用户弹出的所有交互，由视图层实现（便于 VM 保持可测试）。
/// </summary>
public interface IDialogService
{
    Task<string?> PickArchiveAsync();

    Task<IReadOnlyList<string>> PickFilesAsync();

    Task<IReadOnlyList<string>> PickFoldersAsync();

    Task<string?> PickFolderAsync(string title);

    Task<string?> PickSaveArchiveAsync(string suggestedFileName, string extension);

    /// <summary>
    /// 弹出密码输入框。validate 非空时，密码会先被验证，错误时弹窗保持打开并提示重试。
    /// </summary>
    Task<string?> AskPasswordAsync(string message, Func<string, Task<SevenZipStatus>>? validate = null);

    Task ShowCompressDialogAsync(IReadOnlyList<string> sources);

    Task ShowPreviewAsync(string archivePath, ArchiveEntry entry, string? password);

    Task ShowMessageAsync(string title, string message, string? detail = null);

    /// <summary>确认对话框（取消 / 确定），返回 true 表示用户确认。</summary>
    Task<bool> ConfirmAsync(string title, string message);
}
