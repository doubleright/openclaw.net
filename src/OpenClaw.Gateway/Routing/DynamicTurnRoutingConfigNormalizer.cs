using OpenClaw.Core.Models;

namespace OpenClaw.Gateway.Routing;

internal interface IOpenSquillaBundleLoader
{
    BundleRoutingConfig Load(string bundlePath);
}

internal sealed class NullBundleLoader : IOpenSquillaBundleLoader
{
    public static NullBundleLoader Instance { get; } = new();

    public BundleRoutingConfig Load(string bundlePath)
        => throw new InvalidOperationException($"Bundle loading not available for '{bundlePath}'.");
}

internal sealed class BundleRoutingConfig
{
    public DynamicTurnRoutingAssetsConfig Assets { get; init; } = new();
    public DynamicTurnRoutingPolicyConfig Policy { get; init; } = new();
}

internal static class DynamicTurnRoutingConfigNormalizer
{
    private static readonly DynamicTurnRoutingAssetsConfig DefaultAssets = new();
    private static readonly DynamicTurnRoutingPolicyConfig DefaultPolicy = new();

    public static ResolvedDynamicTurnRoutingConfig Normalize(DynamicTurnRoutingConfig config, IOpenSquillaBundleLoader bundleLoader)
    {
        var bundle = string.IsNullOrWhiteSpace(config.BundlePath)
            ? new BundleRoutingConfig()
            : bundleLoader.Load(config.BundlePath);

        var classifierPath = FirstNonEmpty(
            config.Assets.ClassifierModelPath,
            bundle.Assets.ClassifierModelPath,
            config.Classifier.ModelPath);

        var embeddingPath = FirstNonEmpty(
            config.Assets.EmbeddingModelPath,
            bundle.Assets.EmbeddingModelPath,
            config.Embeddings.ModelPath);

        var tokenizerPath = FirstNonEmpty(
            config.Assets.TokenizerPath,
            bundle.Assets.TokenizerPath,
            config.Embeddings.TokenizerPath);

        var tiers = HasAnyConfiguredTier(config.Policy.Tiers)
            ? config.Policy.Tiers
            : HasAnyConfiguredTier(bundle.Policy.Tiers)
                ? bundle.Policy.Tiers
                : config.Tiers;

        var hasBundleAssetsOverrides = HasConfiguredModernAssets(bundle.Assets);

        var dimensions = HasConfiguredModernAssets(config.Assets)
            ? config.Assets.Dimensions
            : hasBundleAssetsOverrides
                ? bundle.Assets.Dimensions
                : config.Embeddings.Dimensions;

        var enableStickyTier = ChoosePolicyBool(
            config.Policy.EnableStickyTier,
            bundle.Policy.EnableStickyTier,
            DefaultPolicy.EnableStickyTier);

        var enableMarginUpgrade = ChoosePolicyBool(
            config.Policy.EnableMarginUpgrade,
            bundle.Policy.EnableMarginUpgrade,
            DefaultPolicy.EnableMarginUpgrade);

        var enableUnderRoutingSafety = ChoosePolicyBool(
            config.Policy.EnableUnderRoutingSafety,
            bundle.Policy.EnableUnderRoutingSafety,
            DefaultPolicy.EnableUnderRoutingSafety);

        var marginUpgradeThreshold = ChoosePolicyFloat(
            config.Policy.MarginUpgradeThreshold,
            bundle.Policy.MarginUpgradeThreshold,
            DefaultPolicy.MarginUpgradeThreshold);

        var r1RescueThreshold = ChoosePolicyFloat(
            config.Policy.R1RescueThreshold,
            bundle.Policy.R1RescueThreshold,
            DefaultPolicy.R1RescueThreshold);

        var underRoutingSafetyThreshold = ChoosePolicyFloat(
            config.Policy.UnderRoutingSafetyThreshold,
            bundle.Policy.UnderRoutingSafetyThreshold,
            DefaultPolicy.UnderRoutingSafetyThreshold);

        var deepConversationTurnIndexThreshold = ChoosePolicyInt(
            config.Policy.DeepConversationTurnIndexThreshold,
            bundle.Policy.DeepConversationTurnIndexThreshold,
            DefaultPolicy.DeepConversationTurnIndexThreshold);

        return new ResolvedDynamicTurnRoutingConfig
        {
            Enabled = config.Enabled,
            Source = !string.IsNullOrWhiteSpace(config.BundlePath) ? "bundle" : "legacy",
            Assets = new ResolvedDynamicTurnRoutingAssets
            {
                ClassifierModelPath = classifierPath,
                EmbeddingModelPath = embeddingPath,
                TokenizerPath = tokenizerPath,
                ManifestPath = FirstNonEmpty(config.Assets.ManifestPath, bundle.Assets.ManifestPath),
                RuntimeConfigPath = FirstNonEmpty(config.Assets.RuntimeConfigPath, bundle.Assets.RuntimeConfigPath),
                EmbeddingDimensions = dimensions
            },
            Policy = new DynamicTurnRoutingPolicyConfig
            {
                Tiers = tiers,
                EnableStickyTier = enableStickyTier,
                EnableMarginUpgrade = enableMarginUpgrade,
                EnableUnderRoutingSafety = enableUnderRoutingSafety,
                MarginUpgradeThreshold = marginUpgradeThreshold,
                R1RescueThreshold = r1RescueThreshold,
                UnderRoutingSafetyThreshold = underRoutingSafetyThreshold,
                DeepConversationTurnIndexThreshold = deepConversationTurnIndexThreshold
            },
            Tiers = tiers
        };
    }

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private static bool HasAnyConfiguredTier(DynamicTurnRoutingTierMap tiers)
        => IsConfigured(tiers.T0)
        || IsConfigured(tiers.T1)
        || IsConfigured(tiers.T2)
        || IsConfigured(tiers.T3);

    private static bool IsConfigured(DynamicTurnRoutingTierTarget tier)
        => !string.IsNullOrWhiteSpace(tier.ModelProfileId)
        || tier.AllowedTools.Length > 0
        || tier.PreferredTags.Length > 0
        || !string.Equals(tier.PromptMode, "full", StringComparison.OrdinalIgnoreCase)
        || tier.DisableTools;

    private static bool HasConfiguredModernAssets(DynamicTurnRoutingAssetsConfig assets)
        => !string.IsNullOrWhiteSpace(assets.ClassifierModelPath)
        || !string.IsNullOrWhiteSpace(assets.EmbeddingModelPath)
        || !string.IsNullOrWhiteSpace(assets.TokenizerPath)
        || !string.IsNullOrWhiteSpace(assets.ManifestPath)
        || !string.IsNullOrWhiteSpace(assets.RuntimeConfigPath)
        || assets.Dimensions != DefaultAssets.Dimensions;

    private static bool ChoosePolicyBool(bool configured, bool bundled, bool defaultValue)
        => configured != defaultValue ? configured : bundled;

    private static float ChoosePolicyFloat(float configured, float bundled, float defaultValue)
        => !NearlyEqual(configured, defaultValue) ? configured : bundled;

    private static int ChoosePolicyInt(int configured, int bundled, int defaultValue)
        => configured != defaultValue ? configured : bundled;

    private static bool NearlyEqual(float left, float right)
        => MathF.Abs(left - right) < 0.0001f;
}