namespace OpenClaw.Dashboard.Models;

public record SystemSettings(
    int? MaxConcurrentSessions,
    int? SessionTimeoutMinutes,
    string? UsageFooter,
    bool? AllowShell,
    bool? AllowFileWrite,
    bool? AllowFileRead,
    string? ReadRoot,
    string? WriteRoot,
    Dictionary<string, object>? ChannelPolicies,
    Dictionary<string, object>? Extra
);
