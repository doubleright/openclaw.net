namespace OpenClaw.Core.Canvas;

public sealed record A2UiCatalogDescriptor(
    string CatalogId,
    IReadOnlySet<string> ComponentTypes,
    IReadOnlySet<string> FunctionTypes,
    IReadOnlySet<string> SharedTypes,
    IReadOnlyDictionary<string, string> Aliases,
    string DisplayName);
