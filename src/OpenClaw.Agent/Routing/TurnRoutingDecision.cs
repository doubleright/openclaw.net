using Microsoft.Extensions.AI;
using OpenClaw.Core.Models;

namespace OpenClaw.Agent.Routing;

public sealed class TurnRoutingRequest
{
    public required Session Session { get; init; }
    public required IReadOnlyList<ChatMessage> Messages { get; init; }
    public required string UserMessage { get; init; }
    public required ChatOptions BaseOptions { get; init; }
}

public sealed class TurnRoutingDecision
{
    public string Tier { get; init; } = "T2";
    public string? ModelProfileId { get; init; }
    public string? DirectModelFallbackProfileId { get; init; }
    public bool DisableTools { get; init; }
    public string[] AllowedTools { get; init; } = [];
    public string[] PreferredTags { get; init; } = [];
    public string? ReasoningLevel { get; init; }
    public string? ResponsePolicy { get; init; }
    public string? ImageCapableModelProfileId { get; init; }
    public bool CacheContinuitySafeguardsEnabled { get; init; }
    public int CacheContinuityMaxConversationTurns { get; init; }
    public bool CacheContinuityResetOnProfileSwitch { get; init; } = true;
    public string? SystemPromptSuffix { get; init; }
    public string Reason { get; init; } = "default";
}