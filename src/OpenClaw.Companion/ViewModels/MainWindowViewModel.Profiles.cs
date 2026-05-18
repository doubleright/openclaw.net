using System.Collections.ObjectModel;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenClaw.Core.Models;

namespace OpenClaw.Companion.ViewModels;

public sealed partial class MainWindowViewModel
{
    [ObservableProperty]
    private bool _isProfilesBusy;

    [ObservableProperty]
    private string _profilesStatus = "Memory and profiles not loaded.";

    [ObservableProperty]
    private string _memorySearchText = "";

    [ObservableProperty]
    private ProfileRow? _selectedProfile;

    [ObservableProperty]
    private string _selectedProfileDetail = "Select a profile to inspect detail.";

    public ObservableCollection<ProfileRow> ProfileRows { get; } = [];
    public ObservableCollection<MemoryNoteRow> MemoryNoteRows { get; } = [];
    public ObservableCollection<LearningProposalRow> LearningProposalRows { get; } = [];
    public bool HasProfileRows => ProfileRows.Count > 0;
    public bool HasMemoryNoteRows => MemoryNoteRows.Count > 0;
    public bool HasLearningProposalRows => LearningProposalRows.Count > 0;

    partial void OnSelectedProfileChanged(ProfileRow? value)
    {
        if (value is null)
        {
            SelectedProfileDetail = "Select a profile to inspect detail.";
            return;
        }

        _ = LoadProfileDetailAsync(value.ActorId);
    }

    [RelayCommand]
    private async Task LoadProfilesAsync()
    {
        if (IsProfilesBusy)
            return;

        IsProfilesBusy = true;
        try
        {
            using var client = RequireIntegrationClient(status => ProfilesStatus = status);
            if (client is null)
                return;

            var profiles = await client.ListProfilesAsync(CancellationToken.None);
            var notes = await client.ListMemoryNotesAsync(prefix: null, memoryClass: null, projectId: null, limit: 50, CancellationToken.None);
            var proposals = await client.ListLearningProposalsAsync(status: null, kind: null, CancellationToken.None);
            ReplaceItems(ProfileRows, profiles.Items.Select(ProfileRow.FromProfile));
            ReplaceItems(MemoryNoteRows, notes.Items.Select(MemoryNoteRow.FromItem));
            ReplaceItems(LearningProposalRows, proposals.Items.Select(LearningProposalRow.FromProposal));
            OnPropertyChanged(nameof(HasProfileRows));
            OnPropertyChanged(nameof(HasMemoryNoteRows));
            OnPropertyChanged(nameof(HasLearningProposalRows));
            ProfilesStatus = $"Loaded {ProfileRows.Count} profile(s), {MemoryNoteRows.Count} memory note(s), and {LearningProposalRows.Count} proposal(s).";
        }
        catch (OperationCanceledException ex)
        {
            ProfilesStatus = $"Memory and profiles load canceled: {ex.Message}";
        }
        catch (HttpRequestException ex)
        {
            ProfilesStatus = $"Memory and profiles load failed: {ex.Message}";
        }
        catch (InvalidOperationException ex)
        {
            ProfilesStatus = $"Memory and profiles load failed: {ex.Message}";
        }
        finally
        {
            IsProfilesBusy = false;
        }
    }

    [RelayCommand]
    private async Task SearchMemoryNotesAsync()
    {
        if (IsProfilesBusy || string.IsNullOrWhiteSpace(MemorySearchText))
            return;

        IsProfilesBusy = true;
        try
        {
            using var client = RequireIntegrationClient(status => ProfilesStatus = status);
            if (client is null)
                return;

            var notes = await client.SearchMemoryNotesAsync(MemorySearchText, memoryClass: null, projectId: null, limit: 50, CancellationToken.None);
            ReplaceItems(MemoryNoteRows, notes.Items.Select(MemoryNoteRow.FromItem));
            OnPropertyChanged(nameof(HasMemoryNoteRows));
            ProfilesStatus = MemoryNoteRows.Count == 0 ? "No memory notes matched the search." : $"{MemoryNoteRows.Count} memory note(s) matched.";
        }
        catch (OperationCanceledException ex)
        {
            ProfilesStatus = $"Memory search canceled: {ex.Message}";
        }
        catch (HttpRequestException ex)
        {
            ProfilesStatus = $"Memory search failed: {ex.Message}";
        }
        catch (InvalidOperationException ex)
        {
            ProfilesStatus = $"Memory search failed: {ex.Message}";
        }
        finally
        {
            IsProfilesBusy = false;
        }
    }

    private async Task LoadProfileDetailAsync(string actorId)
    {
        try
        {
            using var client = RequireIntegrationClient(status => SelectedProfileDetail = status);
            if (client is null)
                return;

            var detail = await client.GetProfileAsync(actorId, CancellationToken.None);
            if (SelectedProfile?.ActorId != actorId)
                return;

            SelectedProfileDetail = detail.Profile is null
                ? "Profile not found."
                : string.Join(Environment.NewLine, new[]
                {
                    $"Actor: {detail.Profile.ActorId}",
                    $"Channel: {detail.Profile.ChannelId}",
                    $"Sender: {detail.Profile.SenderId}",
                    $"Updated: {FormatUtc(detail.Profile.UpdatedAtUtc)}",
                    $"Summary: {detail.Profile.Summary}",
                    $"Tone: {detail.Profile.Tone}",
                    $"Facts: {detail.Profile.Facts.Count}",
                    $"Preferences: {JoinCompact(detail.Profile.Preferences)}",
                    $"Active projects: {JoinCompact(detail.Profile.ActiveProjects)}"
                });
        }
        catch (OperationCanceledException ex) when (SelectedProfile?.ActorId == actorId)
        {
            SelectedProfileDetail = $"Profile detail load canceled: {ex.Message}";
        }
        catch (HttpRequestException ex) when (SelectedProfile?.ActorId == actorId)
        {
            SelectedProfileDetail = $"Profile detail load failed: {ex.Message}";
        }
        catch (InvalidOperationException ex) when (SelectedProfile?.ActorId == actorId)
        {
            SelectedProfileDetail = $"Profile detail load failed: {ex.Message}";
        }
    }
}

public sealed class ProfileRow
{
    public required string ActorId { get; init; }
    public required string Channel { get; init; }
    public required string Sender { get; init; }
    public required string Updated { get; init; }
    public required string Summary { get; init; }

    public static ProfileRow FromProfile(UserProfile item)
        => new()
        {
            ActorId = item.ActorId,
            Channel = item.ChannelId,
            Sender = item.SenderId,
            Updated = item.UpdatedAtUtc.ToLocalTime().ToString("g"),
            Summary = item.Summary
        };
}

public sealed class MemoryNoteRow
{
    public required string Key { get; init; }
    public required string Class { get; init; }
    public required string Project { get; init; }
    public required string Updated { get; init; }
    public required string Preview { get; init; }

    public static MemoryNoteRow FromItem(MemoryNoteItem item)
        => new()
        {
            Key = item.DisplayKey,
            Class = item.MemoryClass,
            Project = item.ProjectId ?? "",
            Updated = item.UpdatedAtUtc.ToLocalTime().ToString("g"),
            Preview = item.Preview
        };
}

public sealed class LearningProposalRow
{
    public required string ProposalId { get; init; }
    public required string Kind { get; init; }
    public required string Status { get; init; }
    public required string Risk { get; init; }
    public required string Title { get; init; }
    public required string Summary { get; init; }

    public static LearningProposalRow FromProposal(LearningProposal item)
        => new()
        {
            ProposalId = item.Id,
            Kind = item.Kind,
            Status = item.Status,
            Risk = item.RiskLevel,
            Title = item.Title,
            Summary = item.Summary
        };
}
