namespace OpenClaw.Dashboard.Models;

/// <summary>
/// Per-skill cost breakdown returned by <c>GET /admin/skills/cost-estimate</c>.
/// Mirrors <c>OpenClaw.Core.Models.SkillCostBreakdown</c>; kept in this assembly so the
/// WASM bundle does not have to drag in the full Core project.
/// </summary>
public sealed class SkillCostBreakdown
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int EagerCharacters { get; set; }
    public int IndexCharacters { get; set; }
    public int ResourceCount { get; set; }
    public int InstructionsLength { get; set; }
    public bool ExcludedFromModel { get; set; }
}

/// <summary>
/// Aggregate response for <c>GET /admin/skills/cost-estimate</c> — compares the eager
/// system-prompt budget against the progressive-disclosure index budget for the
/// currently loaded skill set.
/// </summary>
public sealed class SkillCostEstimateResponse
{
    public int TotalSkills { get; set; }
    public int ModelInvocableSkills { get; set; }
    public int EagerCharacters { get; set; }
    public int IndexCharacters { get; set; }
    public int CharactersSaved { get; set; }
    public double SavedRatio { get; set; }
    public int EagerTokensEstimate { get; set; }
    public int IndexTokensEstimate { get; set; }
    public List<SkillCostBreakdown> Items { get; set; } = [];
    public DateTimeOffset GeneratedAt { get; set; }
}
