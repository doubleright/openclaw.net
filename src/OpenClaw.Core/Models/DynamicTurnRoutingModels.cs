namespace OpenClaw.Core.Models;

public sealed class DynamicTurnRoutingConfig
{
    public bool Enabled { get; set; }
    public string BundlePath { get; set; } = "";
    public DynamicTurnRoutingAssetsConfig Assets { get; set; } = new();
    public DynamicTurnRoutingPolicyConfig Policy { get; set; } = new();
    public DynamicTurnRoutingClassifierConfig Classifier { get; set; } = new();
    public DynamicTurnRoutingEmbeddingsConfig Embeddings { get; set; } = new();
    public DynamicTurnRoutingTierMap Tiers { get; set; } = new();
}

public sealed class DynamicTurnRoutingAssetsConfig
{
    public string ClassifierModelPath { get; set; } = "";
    public string EmbeddingModelPath { get; set; } = "";
    public string TokenizerPath { get; set; } = "";
    public string ManifestPath { get; set; } = "";
    public string RuntimeConfigPath { get; set; } = "";
    public int Dimensions { get; set; } = 384;
}

public sealed class DynamicTurnRoutingPolicyConfig
{
    public DynamicTurnRoutingTierMap Tiers { get; set; } = new();
    public bool EnableStickyTier { get; set; } = true;
    public bool EnableMarginUpgrade { get; set; } = true;
    public bool EnableUnderRoutingSafety { get; set; } = true;
    public float MarginUpgradeThreshold { get; set; } = 0.15f;
    public float R1RescueThreshold { get; set; } = 0.20f;
    public float UnderRoutingSafetyThreshold { get; set; } = 0.45f;
    public int DeepConversationTurnIndexThreshold { get; set; } = 4;
}

public sealed class DynamicTurnRoutingClassifierConfig
{
    public string ModelPath { get; set; } = "";
}

public sealed class DynamicTurnRoutingEmbeddingsConfig
{
    public string ModelPath { get; set; } = "";
    public string TokenizerPath { get; set; } = "";
    public int Dimensions { get; set; } = 384;
}

public sealed class DynamicTurnRoutingTierMap
{
    public DynamicTurnRoutingTierTarget T0 { get; set; } = new();
    public DynamicTurnRoutingTierTarget T1 { get; set; } = new();
    public DynamicTurnRoutingTierTarget T2 { get; set; } = new();
    public DynamicTurnRoutingTierTarget T3 { get; set; } = new();
}

public sealed class DynamicTurnRoutingTierTarget
{
    public string ModelProfileId { get; set; } = "";
    public string[] AllowedTools { get; set; } = [];
    public string[] PreferredTags { get; set; } = [];
    public string PromptMode { get; set; } = "full";
    public bool DisableTools { get; set; }
}

public sealed class ResolvedDynamicTurnRoutingConfig
{
    public bool Enabled { get; init; }
    public string Source { get; init; } = "disabled";
    public ResolvedDynamicTurnRoutingAssets Assets { get; init; } = new();
    public DynamicTurnRoutingPolicyConfig Policy { get; init; } = new();
    public DynamicTurnRoutingTierMap Tiers { get; init; } = new();
}

public sealed class ResolvedDynamicTurnRoutingAssets
{
    public string ClassifierModelPath { get; init; } = "";
    public string EmbeddingModelPath { get; init; } = "";
    public string TokenizerPath { get; init; } = "";
    public string ManifestPath { get; init; } = "";
    public string RuntimeConfigPath { get; init; } = "";
    public int EmbeddingDimensions { get; init; } = 384;
}