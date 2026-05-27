using System.Runtime.InteropServices;
using System.Text.Json;
using OpenClaw.Core.Abstractions;

namespace OpenClaw.Core.Skills;

/// <summary>
/// On-demand reader for skill auxiliary files (<c>references/</c> and <c>scripts/</c>) — implements
/// the <c>read_skill_resource</c> tool that powers L3 of the progressive disclosure pattern.
/// The resource manifest emitted by <see cref="SkillPromptBuilder.BuildIndex"/> only lists names
/// and paths; the model calls this tool to fetch a single resource's body when needed.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the behavior of <c>read_skill_resource</c> in Microsoft Agent Framework's
/// <c>AgentSkillsProvider</c>: lookup is by <c>(skillName, resourceName)</c> and accepts either the
/// bare file name (e.g. <c>"lookup.md"</c>) or the relative path (e.g. <c>"references/lookup.md"</c>).
/// Errors are returned as plain text rather than thrown so the model can recover gracefully.
/// </para>
/// </remarks>
public sealed class ReadSkillResourceTool : ITool
{
    /// <summary>Built-in default cap when no explicit limit is supplied (256 KB).</summary>
    public const long DefaultMaxResourceBytes = 256 * 1024;

    private readonly Func<IReadOnlyList<SkillDefinition>> _provider;
    private readonly long _maxResourceBytes;

    public ReadSkillResourceTool(Func<IReadOnlyList<SkillDefinition>> provider, long maxResourceBytes = DefaultMaxResourceBytes)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _maxResourceBytes = maxResourceBytes > 0 ? maxResourceBytes : DefaultMaxResourceBytes;
    }

    public ReadSkillResourceTool(IReadOnlyList<SkillDefinition> skills, long maxResourceBytes = DefaultMaxResourceBytes)
        : this(() => skills ?? [], maxResourceBytes)
    {
    }

    public string Name => "read_skill_resource";

    public string Description =>
        "Read the contents of a single auxiliary resource (reference document or script) "
        + "associated with a skill. Resource names are listed in the <skill-resources> manifest "
        + "either inside the index or alongside a loaded skill body. "
        + "Never call this tool with 'SKILL.md' — that is the skill body itself, fetch it via `load_skill`. "
        + "Cross-skill paths (e.g. '../other-skill/...') and absolute paths are not accepted.";

    public string ParameterSchema =>
        """{"type":"object","properties":{"skill":{"type":"string","description":"Skill name (as listed in <available-skills>)"},"resource":{"type":"string","description":"Resource name — either bare file name (e.g. \"lookup.md\") or relative path (e.g. \"references/lookup.md\"). Must be listed in this skill's own <resources> manifest; do not pass \"SKILL.md\" (use load_skill) or paths containing \"..\""}},"required":["skill","resource"]}""";

    public async ValueTask<string> ExecuteAsync(string argumentsJson, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!TryParseArguments(argumentsJson, out var skillName, out var resourceName, out var parseError))
            return parseError!;

        var skills = _provider() ?? [];
        var skill = FindSkill(skills, skillName!);
        if (skill is null)
        {
            var available = string.Join(", ",
                skills.Where(s => !s.DisableModelInvocation).Select(s => s.Name));
            return $"Error: skill '{skillName}' not found. Available: {(string.IsNullOrEmpty(available) ? "(none)" : available)}.";
        }

        if (skill.DisableModelInvocation)
            return $"Error: skill '{skill.Name}' is not available for model invocation.";

        var resource = FindResource(skill, resourceName!);
        if (resource is null)
        {
            // (a) Model treated SKILL.md (the L2 body) as an L3 resource — explicitly redirect to load_skill,
            // and if the path embeds a sibling skill name (e.g. "../lark-shared/SKILL.md"), name that skill
            // so the model can switch in one step instead of guessing.
            if (LooksLikeSkillBody(resourceName!))
            {
                var crossSkill = TryExtractCrossSkillName(resourceName!, skills);
                if (crossSkill is not null && !string.Equals(crossSkill, skill.Name, StringComparison.OrdinalIgnoreCase))
                    return $"Error: 'SKILL.md' is the body of skill '{crossSkill}', not an L3 resource of '{skill.Name}'. "
                         + $"Use `load_skill` with skill='{crossSkill}' to fetch it, not `read_skill_resource`.";
                return $"Error: 'SKILL.md' is the skill body itself, not an L3 resource. "
                     + $"Use `load_skill` with skill='{skill.Name}' to fetch it, not `read_skill_resource`.";
            }

            // (b) Path-traversal or absolute-path attempt — the tool is scoped to the current skill's
            // <resources> manifest; reject and steer cross-skill intent toward load_skill.
            if (resourceName!.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(resourceName))
                return $"Error: cross-skill or absolute paths are not allowed for `read_skill_resource`. "
                     + $"It only accepts paths listed in '{skill.Name}'s own <resources> manifest. "
                     + $"If you want another skill's body, call `load_skill` with that skill's name instead.";

            var available = skill.Resources.Count == 0
                ? "(none)"
                : string.Join(", ", skill.Resources.Select(r => r.RelativePath));
            return $"Error: resource '{resourceName}' not found in skill '{skill.Name}'. Available: {available}.";
        }

        try
        {
            // Defense-in-depth: although ScanSkillResources only enumerates files under the skill
            // root, re-verify the resolved path still sits within the skill directory before reading
            // (guards against symlinks introduced after discovery).
            if (!IsPathWithinSkillRoot(resource.AbsolutePath, skill))
                return $"Error: resource '{resource.RelativePath}' resolves outside skill root and was rejected.";

            var info = new FileInfo(resource.AbsolutePath);
            if (!info.Exists)
                return $"Error: resource '{resource.RelativePath}' no longer exists on disk.";

            if (info.Length > _maxResourceBytes)
                return $"Error: resource '{resource.RelativePath}' is {info.Length} bytes (max {_maxResourceBytes}). Read it via the workspace file tools instead.";

            return await File.ReadAllTextAsync(resource.AbsolutePath, System.Text.Encoding.UTF8, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return $"Error: failed to read resource '{resource.RelativePath}' from skill '{skill.Name}': {ex.Message}";
        }
    }

    private static bool TryParseArguments(string argumentsJson, out string? skillName, out string? resourceName, out string? error)
    {
        skillName = null;
        resourceName = null;
        error = null;

        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            error = "Error: missing required arguments 'skill' and 'resource'.";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "Error: invalid JSON arguments. Expected an object like {\"skill\":\"<name>\",\"resource\":\"<name>\"}.";
                return false;
            }

            if (TryReadString(doc.RootElement, "skill", out var s)
                || TryReadString(doc.RootElement, "skill_name", out s)
                || TryReadString(doc.RootElement, "name", out s))
            {
                skillName = s;
            }

            if (TryReadString(doc.RootElement, "resource", out var r)
                || TryReadString(doc.RootElement, "resource_name", out r)
                || TryReadString(doc.RootElement, "path", out r))
            {
                resourceName = r;
            }
        }
        catch (JsonException)
        {
            error = "Error: invalid JSON arguments. Expected {\"skill\":\"<name>\",\"resource\":\"<name>\"}.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(skillName))
        {
            error = "Error: missing required argument 'skill'.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(resourceName))
        {
            error = "Error: missing required argument 'resource'.";
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

    private static SkillResource? FindResource(SkillDefinition skill, string requested)
    {
        // Normalize backslashes so callers can pass either POSIX or Windows separators.
        var normalized = requested.Replace('\\', '/').Trim();

        // 1) Exact relative-path match (POSIX-form), case-insensitive.
        foreach (var resource in skill.Resources)
        {
            if (string.Equals(resource.RelativePath, normalized, StringComparison.OrdinalIgnoreCase))
                return resource;
        }

        // 2) Bare file name match (e.g. "FAQ.md" matches "references/FAQ.md").
        foreach (var resource in skill.Resources)
        {
            if (string.Equals(resource.Name, normalized, StringComparison.OrdinalIgnoreCase))
                return resource;
        }

        // 3) Suffix match — useful when caller drops the leading "references/" segment.
        foreach (var resource in skill.Resources)
        {
            if (resource.RelativePath.EndsWith("/" + normalized, StringComparison.OrdinalIgnoreCase))
                return resource;
        }

        return null;
    }

    /// <summary>
    /// Detects whether the requested path refers to a SKILL.md body file (with or without leading
    /// directories). Used to redirect the model from <c>read_skill_resource</c> back to <c>load_skill</c>.
    /// </summary>
    private static bool LooksLikeSkillBody(string requested)
    {
        var normalized = requested.Replace('\\', '/').Trim().TrimEnd('/');
        if (normalized.Length == 0) return false;
        var lastSlash = normalized.LastIndexOf('/');
        var leaf = lastSlash < 0 ? normalized : normalized[(lastSlash + 1)..];
        return string.Equals(leaf, "SKILL.md", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// When the requested path embeds a sibling skill name (e.g. <c>"../lark-shared/SKILL.md"</c> or
    /// <c>"lark-shared/SKILL.md"</c>), return that skill's canonical name so the error response can
    /// suggest the exact <c>load_skill</c> argument. Returns <c>null</c> if no matching skill is found.
    /// </summary>
    private static string? TryExtractCrossSkillName(string requested, IReadOnlyList<SkillDefinition> skills)
    {
        var normalized = requested.Replace('\\', '/').Trim();
        var lastSlash = normalized.LastIndexOf('/');
        if (lastSlash <= 0) return null;

        var parent = normalized[..lastSlash];
        var prevSlash = parent.LastIndexOf('/');
        var lastSeg = prevSlash < 0 ? parent : parent[(prevSlash + 1)..];
        if (string.IsNullOrEmpty(lastSeg) || lastSeg == "..") return null;

        foreach (var s in skills)
        {
            if (s.DisableModelInvocation) continue;
            if (string.Equals(s.Name, lastSeg, StringComparison.OrdinalIgnoreCase))
                return s.Name;
            if (s.Metadata.SkillKey is { Length: > 0 } key
                && string.Equals(key, lastSeg, StringComparison.OrdinalIgnoreCase))
                return s.Name;
        }
        return null;
    }

    private static bool IsPathWithinSkillRoot(string resourceAbsolutePath, SkillDefinition skill)
    {
        if (string.IsNullOrEmpty(skill.Location))
            return true; // Inline / virtual skills have no root to compare against.

        try
        {
            var skillRoot = Path.GetFullPath(skill.Location);
            var resolved = Path.GetFullPath(resourceAbsolutePath);
            var rootWithSep = skillRoot.EndsWith(Path.DirectorySeparatorChar)
                ? skillRoot
                : skillRoot + Path.DirectorySeparatorChar;
            return resolved.StartsWith(rootWithSep,
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }
}
