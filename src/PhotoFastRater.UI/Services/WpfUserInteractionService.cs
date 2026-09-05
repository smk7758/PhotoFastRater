using PhotoFastRater.Core.Abstractions;

namespace PhotoFastRater.UI.Services;

/// <summary>Presents WPF dialogs on the application dispatcher.</summary>
public sealed class WpfUserInteractionService : IUserInteractionService
{
    /// <inheritdoc />
    public async Task NotifyAsync(
        string title,
        string message,
        UserNotificationKind kind,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            System.Windows.MessageBox.Show(
                message,
                title,
                System.Windows.MessageBoxButton.OK,
                kind switch
                {
                    UserNotificationKind.Error => System.Windows.MessageBoxImage.Error,
                    UserNotificationKind.Warning => System.Windows.MessageBoxImage.Warning,
                    _ => System.Windows.MessageBoxImage.Information
                }));
    }

    /// <inheritdoc />
    public async Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            System.Windows.MessageBox.Show(
                message,
                title,
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question));
        return result == System.Windows.MessageBoxResult.Yes;
    }

    /// <inheritdoc />
    public async Task<string?> SelectFolderAsync(string description, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = description };
            return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
        });
    }

    /// <inheritdoc />
    public async Task<string?> PromptTextAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            Microsoft.VisualBasic.Interaction.InputBox(message, title, string.Empty));
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }
}
