using System.Security;
using System.Text.Json;
using OpenClaw.Core.Abstractions;

namespace OpenClaw.Core.Skills;

/// <summary>
/// On-demand loader for SKILL.md bodies — implements the <c>load_skill</c> tool that powers
/// progressive disclosure. The system prompt only contains the skill index produced by
/// <see cref="SkillPromptBuilder.BuildIndex"/>; when the model decides a skill is relevant,
/// it calls this tool to pull in the full instructions and resource manifest for that one skill.
/// </summary>
/// <remarks>
/// Construct with a <see cref="Func{TResult}"/> when the loaded skill set may change at runtime
/// (e.g. hot reload via <c>AgentRuntime.ReloadSkillsAsync</c>); pass a static list otherwise.
/// </remarks>
public sealed class LoadSkillTool : ITool
{
    private readonly Func<IReadOnlyList<SkillDefinition>> _provider;

    public LoadSkillTool(Func<IReadOnlyList<SkillDefinition>> provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public LoadSkillTool(IReadOnlyList<SkillDefinition> skills)
        : this(() => skills ?? [])
    {
    }

    public string Name => "load_skill";

    public string Description =>
        "Load the full instructions of a named skill on demand. The system prompt only lists "
        + "skill metadata and a resource manifest; call this tool when a specific skill is "
        + "relevant to fetch its complete SKILL.md body. "
        + "Always use this tool (never `read_skill_resource`) to fetch any skill's SKILL.md, "
        + "including when another skill's body is referenced by a sibling skill.";

    public string ParameterSchema =>
        """{"type":"object","properties":{"skill":{"type":"string","description":"Skill name to load (as listed in <available-skills>)"}},"required":["skill"]}""";

    public ValueTask<string> ExecuteAsync(string argumentsJson, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!TryParseSkillName(argumentsJson, out var requested, out var parseError))
            return ValueTask.FromResult(parseError!);

        var skills = _provider() ?? [];
        var match = FindSkill(skills, requested!);

        if (match is null)
        {
            var available = string.Join(", ",
                skills.Where(s => !s.DisableModelInvocation).Select(s => s.Name));
            return ValueTask.FromResult(
                $"Error: skill '{requested}' not found. Available: {(string.IsNullOrEmpty(available) ? "(none)" : available)}.");
        }

        if (match.DisableModelInvocation)
            return ValueTask.FromResult($"Error: skill '{match.Name}' is not available for model invocation.");

        var body = SkillPromptBuilder.BuildSkillBody(match);
        if (body.Length == 0)
            return ValueTask.FromResult($"Skill '{match.Name}' has no instructions body.");

        if (match.Resources.Count == 0)
            return ValueTask.FromResult(body);

        // Re-emit the resource manifest alongside the body so the model knows what it can
        // still reach for via subsequent reads — without re-loading every other skill's index.
        var withManifest = body.TrimEnd() + "\n\n" + RenderResourceManifest(match) + "\n";
        return ValueTask.FromResult(withManifest);
    }

    private static bool TryParseSkillName(string argumentsJson, out string? skillName, out string? error)
    {
        skillName = null;
        error = null;

        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            error = "Error: missing required argument 'skill'.";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "Error: invalid JSON arguments. Expected an object like {\"skill\":\"<name>\"}.";
                return false;
            }

            // Accept "skill", "skill_name" or "name" for ergonomics.
            if (TryReadString(doc.RootElement, "skill", out var v)
                || TryReadString(doc.RootElement, "skill_name", out v)
                || TryReadString(doc.RootElement, "name", out v))
            {
                skillName = v;
            }
        }
        catch (JsonException)
        {
            error = "Error: invalid JSON arguments. Expected {\"skill\":\"<name>\"}.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(skillName))
        {
            error = "Error: missing required argument 'skill'.";
            return false;
        }

        return true;
    }

    private static bool TryReadString(JsonElement element, string property, out string? value)
    {
        if (element.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String)
        {
            value = v.GetString();
            return !string.IsNullOrWhiteSpace(value);
        }
        value = null;
        return false;
    }

    private static SkillDefinition? FindSkill(IReadOnlyList<SkillDefinition> skills, string requested)
    {
        // Try exact match first (case-insensitive), then SkillKey alias.
        foreach (var skill in skills)
        {
            if (string.Equals(skill.Name, requested, StringComparison.OrdinalIgnoreCase))
                return skill;
        }
        foreach (var skill in skills)
        {
            if (skill.Metadata.SkillKey is { Length: > 0 } key
                && string.Equals(key, requested, StringComparison.OrdinalIgnoreCase))
                return skill;
        }
        return null;
    }

    private static string RenderResourceManifest(SkillDefinition skill)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<skill-resources>");
        foreach (var resource in skill.Resources)
        {
            var kind = resource.Kind == SkillResourceKind.Reference ? "reference" : "script";
            sb.Append("  <resource kind=\"");
            sb.Append(kind);
            sb.Append("\" path=\"");
            sb.Append(SecurityElement.Escape(resource.RelativePath));
            sb.AppendLine("\" />");
        }
        sb.Append("</skill-resources>");
        return sb.ToString();
    }
}
