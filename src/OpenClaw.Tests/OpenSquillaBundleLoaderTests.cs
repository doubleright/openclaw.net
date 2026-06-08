using OpenClaw.Gateway.Routing;
using Xunit;

namespace OpenClaw.Tests;

public sealed class OpenSquillaBundleLoaderTests
{
    [Fact]
    public void Load_RuntimeConfigDimensions_TakePrecedenceOverManifest()
    {
        var bundlePath = CreateTempBundleDirectory();

        try
        {
            File.WriteAllText(
                Path.Combine(bundlePath, "runtime-config.json"),
                "{\"routing\":{\"embeddingDimensions\":512}}"
            );
            File.WriteAllText(
                Path.Combine(bundlePath, "manifest.json"),
                "{\"embedding\":{\"dimensions\":256}}"
            );

            var loader = new OpenSquillaBundleLoader();
            var bundle = loader.Load(bundlePath);

            Assert.Equal(512, bundle.Assets.Dimensions);
        }
        finally
        {
            Directory.Delete(bundlePath, recursive: true);
        }
    }

    [Fact]
    public void Load_UsesManifestDimensions_WhenRuntimeConfigMissing()
    {
        var bundlePath = CreateTempBundleDirectory();

        try
        {
            File.WriteAllText(
                Path.Combine(bundlePath, "manifest.json"),
                "{\"metadata\":{\"embedding_size\":448}}"
            );

            var loader = new OpenSquillaBundleLoader();
            var bundle = loader.Load(bundlePath);

            Assert.Equal(448, bundle.Assets.Dimensions);
        }
        finally
        {
            Directory.Delete(bundlePath, recursive: true);
        }
    }

    [Fact]
    public void Load_FallsBackToDefaultDimensions_WhenMetadataMissing()
    {
        var bundlePath = CreateTempBundleDirectory();

        try
        {
            var loader = new OpenSquillaBundleLoader();
            var bundle = loader.Load(bundlePath);

            Assert.Equal(384, bundle.Assets.Dimensions);
        }
        finally
        {
            Directory.Delete(bundlePath, recursive: true);
        }
    }

    [Fact]
    public void Load_UsesManifestDeclaredAssetPaths_WhenPresent()
    {
        var bundlePath = CreateTempBundleDirectory();

        try
        {
            Directory.CreateDirectory(Path.Combine(bundlePath, "models"));
            Directory.CreateDirectory(Path.Combine(bundlePath, "tokenizers"));
            Directory.CreateDirectory(Path.Combine(bundlePath, "config"));
            File.WriteAllText(
                Path.Combine(bundlePath, "manifest.json"),
                """
                {
                  "assets": {
                    "classifierModelPath": "models/router-classifier.onnx",
                    "embeddingModelPath": "models/router-embeddings.onnx",
                    "tokenizerPath": "tokenizers/router-tokenizer.json",
                    "runtimeConfigPath": "config/runtime.json"
                  }
                }
                """);

            var loader = new OpenSquillaBundleLoader();
            var bundle = loader.Load(bundlePath);

            Assert.Equal(Path.Combine(bundlePath, "models", "router-classifier.onnx"), bundle.Assets.ClassifierModelPath);
            Assert.Equal(Path.Combine(bundlePath, "models", "router-embeddings.onnx"), bundle.Assets.EmbeddingModelPath);
            Assert.Equal(Path.Combine(bundlePath, "tokenizers", "router-tokenizer.json"), bundle.Assets.TokenizerPath);
            Assert.Equal(Path.Combine(bundlePath, "config", "runtime.json"), bundle.Assets.RuntimeConfigPath);
        }
        finally
        {
            Directory.Delete(bundlePath, recursive: true);
        }
    }

    private static string CreateTempBundleDirectory()
    {
        var bundlePath = Path.Combine(Path.GetTempPath(), "openclaw-bundle-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bundlePath);
        File.WriteAllText(Path.Combine(bundlePath, "classifier.onnx"), string.Empty);
        File.WriteAllText(Path.Combine(bundlePath, "embeddings.onnx"), string.Empty);
        File.WriteAllText(Path.Combine(bundlePath, "tokenizer.json"), "{}");
        return bundlePath;
    }
}
