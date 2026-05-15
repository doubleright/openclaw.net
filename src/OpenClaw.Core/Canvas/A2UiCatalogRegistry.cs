namespace OpenClaw.Core.Canvas;

public static class A2UiCatalogRegistry
{
    public const string OpenClawV08CatalogId = "urn:a2ui:catalog:openclaw_v0_8";
    public const string AGenUiCatalogId = "urn:a2ui:catalog:agenui_catalog";

    private static readonly string[] AGenUiComponentTypes =
    [
        "Text",
        "Image",
        "Icon",
        "Divider",
        "Video",
        "AudioPlayer",
        "Markdown",
        "Button",
        "TextField",
        "CheckBox",
        "Slider",
        "ChoicePicker",
        "DateTimeInput",
        "Row",
        "Column",
        "Card",
        "List",
        "Tabs",
        "Modal",
        "Table",
        "Carousel",
        "Web",
        "RichText",
        "Chart"
    ];

    private static readonly string[] OpenClawV08ComponentTypes =
    [
        "Text",
        "Markdown",
        "Card",
        "Button",
        "TextField",
        "ChoicePicker",
        "CheckBox",
        "Table",
        "Image",
        "Slider",
        "Chart"
    ];

    private static readonly IReadOnlyDictionary<string, string> V08Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["text"] = "Text",
        ["markdown"] = "Markdown",
        ["card"] = "Card",
        ["button"] = "Button",
        ["input"] = "TextField",
        ["select"] = "ChoicePicker",
        ["checklist"] = "CheckBox",
        ["table"] = "Table",
        ["image"] = "Image",
        ["progress"] = "Slider",
        ["chart"] = "Chart"
    };

    private static readonly IReadOnlyDictionary<string, A2UiCatalogDescriptor> Catalogs =
        new Dictionary<string, A2UiCatalogDescriptor>(StringComparer.OrdinalIgnoreCase)
        {
            [OpenClawV08CatalogId] = Create(OpenClawV08CatalogId, OpenClawV08ComponentTypes, V08Aliases, "OpenClaw A2UI v0.8"),
            [AGenUiCatalogId] = Create(AGenUiCatalogId, AGenUiComponentTypes, V08Aliases, "AGenUI Catalog")
        };

    public static IReadOnlyCollection<A2UiCatalogDescriptor> BuiltInCatalogs => Catalogs.Values.ToArray();

    public static bool TryGetCatalog(string? catalogId, out A2UiCatalogDescriptor catalog)
    {
        if (!string.IsNullOrWhiteSpace(catalogId) && Catalogs.TryGetValue(catalogId, out catalog!))
            return true;

        catalog = null!;
        return false;
    }

    public static bool TryChooseCatalog(IEnumerable<string>? supportedCatalogIds, string? requestedCatalogId, out A2UiCatalogDescriptor catalog)
    {
        if (!string.IsNullOrWhiteSpace(requestedCatalogId))
            return TryGetCatalog(requestedCatalogId, out catalog);

        if (supportedCatalogIds is not null)
        {
            var supported = supportedCatalogIds.ToArray();
            if (supported.Any(static catalogId => string.Equals(catalogId, AGenUiCatalogId, StringComparison.OrdinalIgnoreCase)))
                return TryGetCatalog(AGenUiCatalogId, out catalog);

            foreach (var catalogId in supported)
            {
                if (TryGetCatalog(catalogId, out catalog))
                    return true;
            }
        }

        return TryGetCatalog(AGenUiCatalogId, out catalog);
    }

    public static string? ResolveComponentType(A2UiCatalogDescriptor catalog, string? componentType)
    {
        if (string.IsNullOrWhiteSpace(componentType))
            return null;

        if (catalog.ComponentTypes.Contains(componentType))
            return componentType;

        if (catalog.Aliases.TryGetValue(componentType, out var canonical) && catalog.ComponentTypes.Contains(canonical))
            return canonical;

        return null;
    }

    public static bool IsSupportedComponentType(A2UiCatalogDescriptor catalog, string? componentType)
        => ResolveComponentType(catalog, componentType) is not null;

    private static A2UiCatalogDescriptor Create(
        string catalogId,
        IEnumerable<string> componentTypes,
        IReadOnlyDictionary<string, string> aliases,
        string displayName)
        => new(
            catalogId,
            new HashSet<string>(componentTypes, StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            aliases,
            displayName);
}
