using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenClaw.Core.Models;

namespace OpenClaw.Companion.ViewModels;

public sealed partial class MainWindowViewModel
{
    [ObservableProperty]
    private bool _isRuntimeEventsBusy;

    [ObservableProperty]
    private string _runtimeEventsStatus = "Runtime events not loaded.";

    [ObservableProperty]
    private string _runtimeEventsSessionId = "";

    [ObservableProperty]
    private string _runtimeEventsChannelId = "";

    [ObservableProperty]
    private string _runtimeEventsSenderId = "";

    [ObservableProperty]
    private string _runtimeEventsComponent = "";

    [ObservableProperty]
    private string _runtimeEventsAction = "";

    [ObservableProperty]
    private string _runtimeEventsFromUtc = "";

    [ObservableProperty]
    private string _runtimeEventsToUtc = "";

    [ObservableProperty]
    private int _runtimeEventsLimit = 100;

    [ObservableProperty]
    private RuntimeEventRow? _selectedRuntimeEvent;

    public ObservableCollection<RuntimeEventRow> RuntimeEventRows { get; } = [];
    public bool HasRuntimeEventRows => RuntimeEventRows.Count > 0;
    public string SelectedRuntimeEventJson => SelectedRuntimeEvent?.RawJson ?? "Select an event to inspect metadata.";

    partial void OnSelectedRuntimeEventChanged(RuntimeEventRow? value)
        => OnPropertyChanged(nameof(SelectedRuntimeEventJson));

    [RelayCommand]
    private async Task LoadRuntimeEventsAsync()
    {
        if (IsRuntimeEventsBusy)
            return;

        IsRuntimeEventsBusy = true;
        try
        {
            using var client = RequireIntegrationClient(status => RuntimeEventsStatus = status);
            if (client is null)
                return;

            var query = new RuntimeEventQuery
            {
                SessionId = EmptyToNull(RuntimeEventsSessionId),
                ChannelId = EmptyToNull(RuntimeEventsChannelId),
                SenderId = EmptyToNull(RuntimeEventsSenderId),
                Component = EmptyToNull(RuntimeEventsComponent),
                Action = EmptyToNull(RuntimeEventsAction),
                FromUtc = TryParseDate(RuntimeEventsFromUtc),
                ToUtc = TryParseDate(RuntimeEventsToUtc),
                Limit = Math.Clamp(RuntimeEventsLimit, 1, 500)
            };
            var response = await client.QueryRuntimeEventsAsync(query, CancellationToken.None);
            ReplaceItems(RuntimeEventRows, response.Items.Select(RuntimeEventRow.FromEntry));
            OnPropertyChanged(nameof(HasRuntimeEventRows));
            RuntimeEventsStatus = RuntimeEventRows.Count == 0
                ? "No runtime events match the current filters."
                : $"{RuntimeEventRows.Count} runtime event{(RuntimeEventRows.Count == 1 ? "" : "s")} loaded.";
        }
        catch (OperationCanceledException ex)
        {
            RuntimeEventsStatus = $"Runtime events load canceled: {ex.Message}";
        }
        catch (HttpRequestException ex)
        {
            RuntimeEventsStatus = $"Runtime events load failed: {ex.Message}";
        }
        catch (InvalidOperationException ex)
        {
            RuntimeEventsStatus = $"Runtime events load failed: {ex.Message}";
        }
        finally
        {
            IsRuntimeEventsBusy = false;
        }
    }

    private static DateTimeOffset? TryParseDate(string? value)
        => DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
}

public sealed class RuntimeEventRow
{
    public required string Id { get; init; }
    public required string Timestamp { get; init; }
    public required string Component { get; init; }
    public required string Action { get; init; }
    public required string Severity { get; init; }
    public required string SessionId { get; init; }
    public required string ChannelId { get; init; }
    public required string Actor { get; init; }
    public required string Summary { get; init; }
    public required string RawJson { get; init; }

    public static RuntimeEventRow FromEntry(RuntimeEventEntry entry)
        => new()
        {
            Id = entry.Id,
            Timestamp = entry.TimestampUtc.ToLocalTime().ToString("g"),
            Component = entry.Component,
            Action = entry.Action,
            Severity = entry.Severity,
            SessionId = entry.SessionId ?? "",
            ChannelId = entry.ChannelId ?? "",
            Actor = string.IsNullOrWhiteSpace(entry.SenderId) ? entry.ChannelId ?? "" : entry.SenderId!,
            Summary = entry.Summary,
            RawJson = JsonSerializer.Serialize(new
            {
                entry.Id,
                entry.TimestampUtc,
                entry.SessionId,
                entry.ChannelId,
                entry.SenderId,
                entry.CorrelationId,
                entry.Component,
                entry.Action,
                entry.Severity,
                entry.Summary,
                entry.Metadata
            }, new JsonSerializerOptions { WriteIndented = true })
        };
}
