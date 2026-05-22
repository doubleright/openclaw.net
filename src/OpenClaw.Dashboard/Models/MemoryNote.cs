namespace OpenClaw.Dashboard.Models;

public record MemoryNote(
    string Key,
    string DisplayKey,
    string? MemoryClass,
    string? ProjectId,
    string Preview,
    string? Content,
    DateTimeOffset UpdatedAtUtc
);

public record MemoryNoteListResponse(
    string? Prefix,
    string? Query,
    string? MemoryClass,
    string? ProjectId,
    IReadOnlyList<MemoryNote> Items
);

public record MemoryNoteDetailResponse(
    MemoryNote? Note
);
