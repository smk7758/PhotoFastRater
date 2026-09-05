namespace PhotoFastRater.Core.Abstractions;

/// <summary>Represents user-facing decisions without coupling view models to WPF.</summary>
public interface IUserInteractionService
{
    /// <summary>Shows a non-blocking-compatible informational or error notification.</summary>
    Task NotifyAsync(string title, string message, UserNotificationKind kind, CancellationToken cancellationToken = default);

    /// <summary>Requests explicit confirmation for an operation with material effects.</summary>
    Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken = default);

    /// <summary>Lets the user choose one existing folder, returning <see langword="null"/> on cancellation.</summary>
    Task<string?> SelectFolderAsync(string description, CancellationToken cancellationToken = default);

    /// <summary>Requests a short text value, returning <see langword="null"/> when cancelled or empty.</summary>
    Task<string?> PromptTextAsync(string title, string message, CancellationToken cancellationToken = default);
}

/// <summary>Classifies a notification for accessible visual and spoken presentation.</summary>
public enum UserNotificationKind { Information, Warning, Error }

/// <summary>Isolates operating-system shell actions from view models and domain logic.</summary>
public interface IPlatformShell
{
    /// <summary>Moves an existing file to the operating-system recycle bin.</summary>
    Task MoveToRecycleBinAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>Opens a file with the registered application.</summary>
    void OpenFile(string filePath);

    /// <summary>Selects a file in the system file browser using structured arguments.</summary>
    void ShowInFileBrowser(string filePath);

    /// <summary>Copies plain text to the platform clipboard.</summary>
    void SetClipboardText(string text);
}
