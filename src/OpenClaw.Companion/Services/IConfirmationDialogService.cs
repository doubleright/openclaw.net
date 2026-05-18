namespace OpenClaw.Companion.Services;

public interface IConfirmationDialogService
{
    Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText, CancellationToken cancellationToken);
}

public sealed class DenyConfirmationDialogService : IConfirmationDialogService
{
    public Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText, CancellationToken cancellationToken)
        => Task.FromResult(false);
}
