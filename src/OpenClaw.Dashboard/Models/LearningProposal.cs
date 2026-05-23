using System.Text.Json;

namespace OpenClaw.Dashboard.Models;

public record LearningProposal(
    string? Id,
    string? Kind,
    string? Status,
    string? Title,
    string? Summary,
    string? SkillName,
    string? DraftContent,
    string? DraftPreview,
    JsonElement? ProfileUpdate,
    JsonElement? AutomationDraft,
    string? RiskLevel,
    float Confidence,
    string? CreatedReason,
    string? ValidationStatus,
    IReadOnlyList<string>? ValidationWarnings,
    IReadOnlyList<string>? ValidationErrors,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc
);

public record LearningProposalListResponse(
    List<LearningProposal> Items
);

public record LearningProposalDetailResponse(
    LearningProposal? Proposal,
    IReadOnlyList<ProfileDiffEntry>? ProfileDiff,
    LearningProposalProvenance? Provenance,
    bool CanRollback
);

public record ProfileDiffEntry(
    string? Path,
    string? ChangeType,
    string? Before,
    string? After
);

public record LearningProposalProvenance(
    string? ActorId,
    IReadOnlyList<string>? SourceSessionIds,
    IReadOnlyList<string>? SourceTurnIds,
    IReadOnlyList<string>? ToolNames,
    IReadOnlyList<string>? ToolSequence,
    int RepeatedCount,
    string? CreatedReason,
    float Confidence,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ReviewedAtUtc
);
