using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.Tokenizers;
using OpenClaw.Routing.Onnx;
using Xunit;

namespace OpenClaw.Tests;

public sealed class LocalOnnxEmbeddingGeneratorTests
{
    [Fact]
    public async Task GenerateAsync_ReturnsBeforeInferenceCompletes()
    {
        using var tokenizerAssets = CreateBpeTokenizerAssets();
        using var runner = new BlockingEmbeddingRunner([0.25f, 0.75f]);
        using var generator = new LocalOnnxEmbeddingGenerator(
            runner,
            tokenizerAssets.Tokenizer,
            dimensions: 2,
            modelPath: "test-model.onnx",
            tokenizerPath: "test-tokenizer.json");

        ValueTask<float[]> valueTask = default;
        var callReturned = new ManualResetEventSlim();
        var caller = Task.Run(() =>
        {
            valueTask = generator.GenerateAsync("hello", CancellationToken.None);
            callReturned.Set();
        });

        Assert.True(runner.Started.Wait(TimeSpan.FromSeconds(5)));

        try
        {
            Assert.True(callReturned.Wait(TimeSpan.FromMilliseconds(250)), "GenerateAsync should return before model inference completes.");
            Assert.False(valueTask.IsCompleted);
        }
        finally
        {
            runner.Release.Set();
        }

        await caller;
        var embedding = await valueTask;
        Assert.Equal(new[] { 0.25f, 0.75f }, embedding);
    }

    [Fact]
    public async Task GenerateAsync_WhenCanceledDuringInference_ThrowsOperationCanceledException()
    {
        using var tokenizerAssets = CreateBpeTokenizerAssets();
        using var runner = new BlockingEmbeddingRunner([0.25f, 0.75f]);
        using var generator = new LocalOnnxEmbeddingGenerator(
            runner,
            tokenizerAssets.Tokenizer,
            dimensions: 2,
            modelPath: "test-model.onnx",
            tokenizerPath: "test-tokenizer.json");
        using var cts = new CancellationTokenSource();

        var task = generator.GenerateAsync("hello", cts.Token).AsTask();
        Assert.True(runner.Started.Wait(TimeSpan.FromSeconds(5)));

        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await task);
    }

    [Fact]
    public void HuggingFaceTokenizerLoader_WhenBpeTokenizerUsesByteLevelPreTokenizer_LoadsRobertaPreTokenizer()
    {
        using var tokenizerJson = CreateTokenizerJson("""
            {
              "model": {
                "type": "BPE",
                "vocab": { "hello": 0, "Ġhello": 1, "[UNK]": 2 },
                "merges": [],
                "unk_token": "[UNK]"
              },
              "pre_tokenizer": { "type": "ByteLevel", "add_prefix_space": true }
            }
            """);

        var (tokenizer, workingDirectory) = LocalOnnxEmbeddingGenerator.HuggingFaceTokenizerLoader.Load(tokenizerJson.Path);
        try
        {
            var bpeTokenizer = Assert.IsType<BpeTokenizer>(tokenizer);
            Assert.NotNull(bpeTokenizer.PreTokenizer);
            Assert.Equal(nameof(RobertaPreTokenizer), bpeTokenizer.PreTokenizer.GetType().Name);
            Assert.True(bpeTokenizer.ByteLevel);
            Assert.True(Directory.Exists(workingDirectory));
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [Fact]
    public void HuggingFaceTokenizerLoader_WhenBpeTokenizerUsesWhitespacePreTokenizer_LoadsWhitespacePreTokenizer()
    {
        using var tokenizerJson = CreateTokenizerJson("""
            {
              "model": {
                "type": "BPE",
                "vocab": { "hello": 0, "[UNK]": 1 },
                "merges": [],
                "unk_token": "[UNK]"
              },
              "pre_tokenizer": { "type": "Whitespace" }
            }
            """);

        var (tokenizer, workingDirectory) = LocalOnnxEmbeddingGenerator.HuggingFaceTokenizerLoader.Load(tokenizerJson.Path);
        try
        {
            var bpeTokenizer = Assert.IsType<BpeTokenizer>(tokenizer);
            Assert.NotNull(bpeTokenizer.PreTokenizer);
            Assert.Equal("RegexPreTokenizer", bpeTokenizer.PreTokenizer.GetType().Name);
            Assert.False(bpeTokenizer.ByteLevel);
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    private static BpeTokenizerAssets CreateBpeTokenizerAssets()
    {
                var tokenizerJson = CreateTokenizerJson("""
                        {
                            "model": {
                                "type": "BPE",
                                "vocab": { "hello": 0, "[UNK]": 1 },
                                "merges": [],
                                "unk_token": "[UNK]"
                            },
                            "pre_tokenizer": { "type": "Whitespace" }
                        }
                        """);

                var (tokenizer, workingDirectory) = LocalOnnxEmbeddingGenerator.HuggingFaceTokenizerLoader.Load(tokenizerJson.Path);
                return new BpeTokenizerAssets(tokenizer, workingDirectory, tokenizerJson);
    }

    private static TemporaryFile CreateTokenizerJson(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"openclaw-tokenizer-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        return new TemporaryFile(path);
    }

    private sealed class BlockingEmbeddingRunner(float[] embedding) : IEmbeddingModelRunner
    {
        public IReadOnlyCollection<string> InputNames { get; } = ["input_ids"];

        public ManualResetEventSlim Started { get; } = new();

        public ManualResetEventSlim Release { get; } = new();

        public float[] Run(IReadOnlyCollection<NamedOnnxValue> inputs, long[] attentionMask, CancellationToken cancellationToken)
        {
            Assert.NotEmpty(inputs);
            Assert.NotEmpty(attentionMask);
            Started.Set();
            Release.Wait(cancellationToken);
            return embedding;
        }

        public void Dispose()
        {
            Started.Dispose();
            Release.Dispose();
        }
    }

    private sealed class BpeTokenizerAssets(Tokenizer tokenizer, string directory, TemporaryFile tokenizerJson) : IDisposable
    {
        public Tokenizer Tokenizer { get; } = tokenizer;

        public void Dispose()
        {
            Directory.Delete(directory, recursive: true);
            tokenizerJson.Dispose();
        }
    }

    private sealed class TemporaryFile(string path) : IDisposable
    {
        public string Path { get; } = path;

        public void Dispose()
        {
            File.Delete(Path);
        }
    }
}