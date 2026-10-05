using System.Diagnostics;
using Microsoft.VisualBasic.FileIO;
using PhotoFastRater.Core.Abstractions;

namespace PhotoFastRater.UI.Services;

/// <summary>Executes validated Windows shell operations.</summary>
public sealed class WindowsPlatformShell : IPlatformShell
{
    /// <inheritdoc />
    public Task MoveToRecycleBinAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = ValidateExistingFile(filePath);
        return Task.Run(() => FileSystem.DeleteFile(
            fullPath,
            UIOption.OnlyErrorDialogs,
            RecycleOption.SendToRecycleBin,
            UICancelOption.ThrowException), cancellationToken);
    }

    /// <inheritdoc />
    public void OpenFile(string filePath)
    {
        var fullPath = ValidateExistingFile(filePath);
        Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true });
    }

    /// <inheritdoc />
    public void ShowInFileBrowser(string filePath)
    {
        var fullPath = ValidateExistingFile(filePath);
        var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
        startInfo.ArgumentList.Add("/select,");
        startInfo.ArgumentList.Add(fullPath);
        Process.Start(startInfo);
    }

    /// <inheritdoc />
    public void SetClipboardText(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        System.Windows.Application.Current.Dispatcher.Invoke(() => System.Windows.Clipboard.SetText(text));
    }

    private static string ValidateExistingFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The requested photo no longer exists.", fullPath);
        return fullPath;
    }
}
