using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace OpenClaw.Companion.Services;

public sealed class WindowConfirmationDialogService(Window owner) : IConfirmationDialogService
{
    public async Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmText,
        string cancelText,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return false;

        var dialog = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var result = false;
        var confirmButton = new Button
        {
            Content = confirmText,
            MinWidth = 96,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        var cancelButton = new Button
        {
            Content = cancelText,
            MinWidth = 96,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };

        confirmButton.Click += (_, _) =>
        {
            result = true;
            dialog.Close();
        };
        cancelButton.Click += (_, _) =>
        {
            result = false;
            dialog.Close();
        };

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancelButton, confirmButton }
                }
            }
        };

        var cancellationRequested = false;
        dialog.Opened += (_, _) =>
        {
            if (cancellationRequested)
                dialog.Close();
        };

        using var cancellationRegistration = cancellationToken.Register(() =>
            Dispatcher.UIThread.Post(() =>
            {
                result = false;
                cancellationRequested = true;
                if (dialog.IsVisible)
                    dialog.Close();
            }));

        if (cancellationToken.IsCancellationRequested)
            return false;

        try
        {
            await dialog.ShowDialog(owner).WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            result = false;
            if (dialog.IsVisible)
                dialog.Close();
            return false;
        }

        return result && !cancellationToken.IsCancellationRequested;
    }
}
