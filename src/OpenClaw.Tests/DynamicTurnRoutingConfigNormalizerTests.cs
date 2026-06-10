using OpenClaw.Core.Models;
using OpenClaw.Gateway.Routing;
using Xunit;

namespace OpenClaw.Tests;

public sealed class DynamicTurnRoutingConfigNormalizerTests
{
    [Fact]
    public void Normalize_DirectRoutingConfig_MapsIntoResolvedModel()
    {
        var config = new DynamicTurnRoutingConfig
        {
            Enabled = true,
            Assets = new DynamicTurnRoutingAssetsConfig
            {
                ClassifierModelPath = "classifier.onnx",
                EmbeddingModelPath = "embeddings.onnx",
                TokenizerPath = "tokenizer.json",
                Dimensions = 384
            },
            Policy = new DynamicTurnRoutingPolicyConfig
            {
                Tiers = new DynamicTurnRoutingTierMap
                {
                    T0 = new DynamicTurnRoutingTierTarget
                    {
                        ModelProfileId = "local-freeform",
                        DirectModelFallbackProfileId = "local-freeform-fallback",
                        ReasoningLevel = "low",
                        ResponsePolicy = "concise",
                        ImageCapableModelProfileId = "vision-local",
                        CacheContinuitySafeguards = new CacheContinuitySafeguardsConfig
                        {
                            Enabled = true,
                            MaxConversationTurns = 40,
                            ResetOnProfileSwitch = false
                        },
                        DisableTools = true
                    },
                    T1 = new DynamicTurnRoutingTierTarget { ModelProfileId = "mini-readonly", AllowedTools = ["read_file"] },
                    T2 = new DynamicTurnRoutingTierTarget { ModelProfileId = "frontier-tools" },
                    T3 = new DynamicTurnRoutingTierTarget { ModelProfileId = "frontier-deep" }
                }
            }
        };

        var resolved = DynamicTurnRoutingConfigNormalizer.Normalize(config, NullBundleLoader.Instance);

        Assert.True(resolved.Enabled);
        Assert.Equal("classifier.onnx", resolved.Assets.ClassifierModelPath);
        Assert.Equal("embeddings.onnx", resolved.Assets.EmbeddingModelPath);
        Assert.Equal("tokenizer.json", resolved.Assets.TokenizerPath);
        Assert.Equal(384, resolved.Assets.EmbeddingDimensions);
        Assert.Equal("local-freeform", resolved.Tiers.T0.ModelProfileId);
        Assert.Equal("local-freeform-fallback", resolved.Tiers.T0.DirectModelFallbackProfileId);
        Assert.Equal("low", resolved.Tiers.T0.ReasoningLevel);
        Assert.Equal("concise", resolved.Tiers.T0.ResponsePolicy);
        Assert.Equal("vision-local", resolved.Tiers.T0.ImageCapableModelProfileId);
        Assert.True(resolved.Tiers.T0.CacheContinuitySafeguards.Enabled);
        Assert.Equal(40, resolved.Tiers.T0.CacheContinuitySafeguards.MaxConversationTurns);
        Assert.False(resolved.Tiers.T0.CacheContinuitySafeguards.ResetOnProfileSwitch);
        Assert.Equal("direct", resolved.Source);
    }

    [Fact]
    public void Normalize_BundlePath_UsesBundleAssetsAndPolicy()
    {
        var config = new DynamicTurnRoutingConfig
        {
            Enabled = true,
            BundlePath = "artifacts/tmp/router-bundle"
        };

        var bundle = new BundleRoutingConfig
        {
            Assets = new DynamicTurnRoutingAssetsConfig
            {
                ClassifierModelPath = "bundle/classifier.onnx",
                EmbeddingModelPath = "bundle/embeddings.onnx",
                TokenizerPath = "bundle/tokenizer.json",
                Dimensions = 390
            },
            Policy = new DynamicTurnRoutingPolicyConfig
            {
                EnableDiagnostics = true,
                EnableStickyTier = false,
                EnableMarginUpgrade = false,
                EnableR1Rescue = false,
                EnableUnderRoutingSafety = false,
                Tiers = new DynamicTurnRoutingTierMap
                {
                    T0 = new DynamicTurnRoutingTierTarget { ModelProfileId = "bundle-t0" },
                    T1 = new DynamicTurnRoutingTierTarget
                    {
                        ModelProfileId = "bundle-t1",
                        DirectModelFallbackProfileId = "bundle-t1-fallback",
                        ReasoningLevel = "medium",
                        ResponsePolicy = "balanced",
                        ImageCapableModelProfileId = "bundle-vision",
                        CacheContinuitySafeguards = new CacheContinuitySafeguardsConfig
                        {
                            Enabled = true,
                            MaxConversationTurns = 72,
                            ResetOnProfileSwitch = false
                        }
                    },
                    T2 = new DynamicTurnRoutingTierTarget { ModelProfileId = "bundle-t2" },
                    T3 = new DynamicTurnRoutingTierTarget { ModelProfileId = "bundle-t3" }
                }
            }
        };

        var resolved = DynamicTurnRoutingConfigNormalizer.Normalize(config, new StubBundleLoader(bundle));

        Assert.Equal("bundle", resolved.Source);
        Assert.Equal("bundle/classifier.onnx", resolved.Assets.ClassifierModelPath);
        Assert.Equal(390, resolved.Assets.EmbeddingDimensions);
        Assert.True(resolved.Policy.EnableDiagnostics);
        Assert.False(resolved.Policy.EnableStickyTier);
        Assert.False(resolved.Policy.EnableMarginUpgrade);
        Assert.False(resolved.Policy.EnableR1Rescue);
        Assert.False(resolved.Policy.EnableUnderRoutingSafety);
        Assert.Equal("bundle-t1-fallback", resolved.Tiers.T1.DirectModelFallbackProfileId);
        Assert.Equal("medium", resolved.Tiers.T1.ReasoningLevel);
        Assert.Equal("balanced", resolved.Tiers.T1.ResponsePolicy);
        Assert.Equal("bundle-vision", resolved.Tiers.T1.ImageCapableModelProfileId);
        Assert.True(resolved.Tiers.T1.CacheContinuitySafeguards.Enabled);
        Assert.Equal(72, resolved.Tiers.T1.CacheContinuitySafeguards.MaxConversationTurns);
        Assert.False(resolved.Tiers.T1.CacheContinuitySafeguards.ResetOnProfileSwitch);
        Assert.Equal("bundle-t2", resolved.Tiers.T2.ModelProfileId);
    }

    [Fact]
    public void Normalize_ExplicitAssetsOverrideBundleValues()
    {
        var config = new DynamicTurnRoutingConfig
        {
            Enabled = true,
            BundlePath = "artifacts/tmp/router-bundle",
            Assets = new DynamicTurnRoutingAssetsConfig
            {
                ClassifierModelPath = "override/classifier.onnx"
            }
        };

        var bundle = new BundleRoutingConfig
        {
            Assets = new DynamicTurnRoutingAssetsConfig
            {
                ClassifierModelPath = "bundle/classifier.onnx",
                EmbeddingModelPath = "bundle/embeddings.onnx",
                TokenizerPath = "bundle/tokenizer.json"
            },
            Policy = new DynamicTurnRoutingPolicyConfig { Tiers = BuildTierMap() }
        };

        var resolved = DynamicTurnRoutingConfigNormalizer.Normalize(config, new StubBundleLoader(bundle));

        Assert.Equal("override/classifier.onnx", resolved.Assets.ClassifierModelPath);
        Assert.Equal("bundle/embeddings.onnx", resolved.Assets.EmbeddingModelPath);
    }

    [Fact]
    public void Normalize_DirectAssetsDimensions_ArePreserved()
    {
        var config = new DynamicTurnRoutingConfig
        {
            Enabled = true,
            Assets = new DynamicTurnRoutingAssetsConfig
            {
                EmbeddingModelPath = "embeddings.onnx",
                TokenizerPath = "tokenizer.json",
                Dimensions = 256
            }
        };

        var resolved = DynamicTurnRoutingConfigNormalizer.Normalize(config, NullBundleLoader.Instance);

        Assert.Equal(256, resolved.Assets.EmbeddingDimensions);
    }

    [Fact]
    public void Normalize_ModernPolicyOverride_MergesWithBundlePolicyPerField()
    {
        var config = new DynamicTurnRoutingConfig
        {
            Enabled = true,
            BundlePath = "artifacts/tmp/router-bundle",
            Policy = new DynamicTurnRoutingPolicyConfig
            {
                EnableStickyTier = false
            }
        };

        var bundle = new BundleRoutingConfig
        {
            Assets = new DynamicTurnRoutingAssetsConfig
            {
                ClassifierModelPath = "bundle/classifier.onnx",
                EmbeddingModelPath = "bundle/embeddings.onnx",
                TokenizerPath = "bundle/tokenizer.json"
            },
            Policy = new DynamicTurnRoutingPolicyConfig
            {
                EnableStickyTier = false,
                EnableMarginUpgrade = false,
                EnableR1Rescue = false,
                EnableUnderRoutingSafety = false,
                Tiers = BuildTierMap()
            }
        };

        var resolved = DynamicTurnRoutingConfigNormalizer.Normalize(config, new StubBundleLoader(bundle));

        Assert.False(resolved.Policy.EnableStickyTier);
        Assert.False(resolved.Policy.EnableMarginUpgrade);
        Assert.False(resolved.Policy.EnableR1Rescue);
        Assert.False(resolved.Policy.EnableUnderRoutingSafety);
    }

    [Fact]
    public void Normalize_PolicyThresholds_ArePreserved()
    {
        var config = new DynamicTurnRoutingConfig
        {
            Enabled = true,
            Policy = new DynamicTurnRoutingPolicyConfig
            {
                MarginUpgradeThreshold = 0.27f,
                R1RescueThreshold = 0.31f,
                UnderRoutingSafetyThreshold = 0.52f,
                DeepConversationTurnIndexThreshold = 7
            }
        };

        var resolved = DynamicTurnRoutingConfigNormalizer.Normalize(config, NullBundleLoader.Instance);

        Assert.Equal(0.27f, resolved.Policy.MarginUpgradeThreshold);
        Assert.Equal(0.31f, resolved.Policy.R1RescueThreshold);
        Assert.Equal(0.52f, resolved.Policy.UnderRoutingSafetyThreshold);
        Assert.Equal(7, resolved.Policy.DeepConversationTurnIndexThreshold);
    }

    [Fact]
    public void Normalize_ThresholdOnlyOverride_WinsOverBundlePolicyThresholds()
    {
        var config = new DynamicTurnRoutingConfig
        {
            Enabled = true,
            BundlePath = "artifacts/tmp/router-bundle",
            Policy = new DynamicTurnRoutingPolicyConfig
            {
                MarginUpgradeThreshold = 0.10f,
                R1RescueThreshold = 0.15f,
                UnderRoutingSafetyThreshold = 0.35f,
                DeepConversationTurnIndexThreshold = 2
            }
        };

        var bundle = new BundleRoutingConfig
        {
            Assets = new DynamicTurnRoutingAssetsConfig
            {
                ClassifierModelPath = "bundle/classifier.onnx",
                EmbeddingModelPath = "bundle/embeddings.onnx",
                TokenizerPath = "bundle/tokenizer.json"
            },
            Policy = new DynamicTurnRoutingPolicyConfig
            {
                MarginUpgradeThreshold = 0.42f,
                R1RescueThreshold = 0.46f,
                UnderRoutingSafetyThreshold = 0.58f,
                DeepConversationTurnIndexThreshold = 9,
                Tiers = BuildTierMap()
            }
        };

        var resolved = DynamicTurnRoutingConfigNormalizer.Normalize(config, new StubBundleLoader(bundle));

        Assert.Equal(0.10f, resolved.Policy.MarginUpgradeThreshold);
        Assert.Equal(0.15f, resolved.Policy.R1RescueThreshold);
        Assert.Equal(0.35f, resolved.Policy.UnderRoutingSafetyThreshold);
        Assert.Equal(2, resolved.Policy.DeepConversationTurnIndexThreshold);
    }

    private static DynamicTurnRoutingTierMap BuildTierMap()
        => new()
        {
            T0 = new DynamicTurnRoutingTierTarget { ModelProfileId = "local-freeform", DisableTools = true },
            T1 = new DynamicTurnRoutingTierTarget { ModelProfileId = "mini-readonly", AllowedTools = ["read_file"] },
            T2 = new DynamicTurnRoutingTierTarget { ModelProfileId = "frontier-tools" },
            T3 = new DynamicTurnRoutingTierTarget { ModelProfileId = "frontier-deep" }
        };

    private sealed class StubBundleLoader(BundleRoutingConfig bundle) : IOpenSquillaBundleLoader
    {
        public BundleRoutingConfig Load(string bundlePath)
        {
            _ = bundlePath;
            return bundle;
        }
    }
}