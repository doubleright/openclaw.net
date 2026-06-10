using System.Text.RegularExpressions;

namespace OpenClaw.Routing.Onnx;

internal sealed record RoutingFeatureInput(
    string CurrentUserText,
    string[] PriorUserTurns,
    string? PreviousAssistantText,
    int TurnIndex,
    string? PreviousTier,
    int ToolCount,
    int ContextTextLength);

internal static partial class PromptFeatureExtractor
{
    internal const int EmbeddingSegmentDimensions = 512;

    internal const int FeatureVectorDimensions = EmbeddingSegmentDimensions * 3;

    private static readonly string[] DebugKeywords = ["error", "bug", "exception", "traceback", "failed", "root cause", "报错", "根因", "修复", "stack trace", "debug"];
    private static readonly string[] ResearchKeywords = ["调研", "research", "对比", "compare", "survey", "分析报告", "competitive analysis", "综述"];
    private static readonly string[] ArchitectureKeywords = ["architecture", "架构", "重构", "refactor", "monorepo", "codebase", "module", "dependency"];
    private static readonly string[] CompareKeywords = ["对比", "compare", "audit", "审计", "review", "评估"];
    private static readonly string[] PlanningKeywords = ["plan", "planning", "方案", "计划", "roadmap", "milestone", "步骤", "实施"];
    private static readonly string[] StrictFormatKeywords = ["json", "yaml", "csv", "schema", "只返回", "不要解释", "按格式", "only return", "no explanation"];
    private static readonly string[] HighRiskKeywords = ["deploy", "rollback", "migration", "delete", "overwrite", "覆盖", "production", "生产", "部署", "删除", "客户", "法务", "财务"];
    private static readonly string[] ProductionKeywords = ["production", "生产", "prod", "线上", "正式环境"];
    private static readonly string[] CustomerKeywords = ["customer", "客户", "用户邮件", "client"];
    private static readonly string[] DeleteKeywords = ["delete", "remove", "drop", "truncate", "删除", "清空", "覆盖", "overwrite"];
    private static readonly string[] FormalKeywords = ["formal", "正式", "official", "公文", "合同", "法律"];
    private static readonly string[] ConstraintKeywords = ["必须", "不能", "不要", "只能", "must", "shall", "required", "forbidden", "不允许", "至少", "最多"];
    private static readonly string[] TeachingKeywords = ["how does", "explain", "what is", "why does", "how to", "教我", "解释", "为什么", "怎么", "是什么", "how can", "tell me about", "walk me through", "介绍", "说明"];
    private static readonly string[] ImplementKeywords = ["implement", "write function", "write a", "create a", "写个", "实现", "用法", "帮我写", "生成代码", "add a", "build a", "make a", "写一个", "编写"];
    private static readonly string[] ComplaintKeywords = ["不对", "太泛了", "重新写", "wrong", "too vague", "redo", "try again", "not right"];

    public static float[] BuildFeatureVector(
        RoutingFeatureInput input,
        ReadOnlySpan<float> currentEmbedding,
        ReadOnlySpan<float> historyEmbedding,
        ReadOnlySpan<float> assistantEmbedding)
    {
        var features = new float[FeatureVectorDimensions];

        var offset = 0;
        CopyEmbeddingSegment(currentEmbedding, features.AsSpan(offset, EmbeddingSegmentDimensions), nameof(currentEmbedding));
        offset += EmbeddingSegmentDimensions;

        CopyEmbeddingSegment(historyEmbedding, features.AsSpan(offset, EmbeddingSegmentDimensions), nameof(historyEmbedding));
        offset += EmbeddingSegmentDimensions;

        CopyEmbeddingSegment(assistantEmbedding, features.AsSpan(offset, EmbeddingSegmentDimensions), nameof(assistantEmbedding));
        return features;
    }

    public static RoutingSignals ExtractSignals(string text, int turnIndex)
    {
        var hasCodeBlock = CodeBlockRegex().IsMatch(text);
        var hasFileReference = FilePathRegex().IsMatch(text);
        var hasUrl = UrlRegex().IsMatch(text);
        var longContext = text.Length >= 6000 || CodeBlockRegex().Matches(text).Sum(static match => match.Length) >= 1500 || FilePathRegex().Matches(text).Count >= 2;
        var debug = KeywordCount(text, DebugKeywords) > 0 || TracebackRegex().IsMatch(text);
        var repoArch = KeywordCount(text, ArchitectureKeywords) > 0 || KeywordCount(text, CompareKeywords) > 0;
        var highRisk = KeywordCount(text, HighRiskKeywords) > 0 || (KeywordCount(text, ProductionKeywords) > 0 && KeywordCount(text, DeleteKeywords) > 0) || KeywordCount(text, CustomerKeywords) > 0 || (KeywordCount(text, ProductionKeywords) > 0 && debug);
        var strictFormat = KeywordCount(text, StrictFormatKeywords) > 0 || KeywordCount(text, ConstraintKeywords) > 1;
        var deepConversation = turnIndex >= 4;
        var research = KeywordCount(text, ResearchKeywords) > 0;
        var planning = KeywordCount(text, PlanningKeywords) > 0;
        return new RoutingSignals(debug, repoArch, highRisk, longContext, strictFormat, hasCodeBlock, hasFileReference, hasUrl, deepConversation, research, planning);
    }

    private static void CopyEmbeddingSegment(ReadOnlySpan<float> source, Span<float> destination, string parameterName)
    {
        if (source.Length != destination.Length)
        {
            throw new ArgumentException(
                $"Expected embedding length {destination.Length}, but received {source.Length}.",
                parameterName);
        }

        source.CopyTo(destination);
    }

    private static int KeywordCount(string text, IEnumerable<string> keywords)
        => keywords.Count(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"```[\s\S]*?```", RegexOptions.Multiline)]
    private static partial Regex CodeBlockRegex();

    [GeneratedRegex(@"(?:^|[\s""'`(])([a-zA-Z_][\w.-]*/[\w./-]+\.[\w]+)", RegexOptions.Multiline)]
    private static partial Regex FilePathRegex();

    [GeneratedRegex(@"https?://\S+", RegexOptions.Multiline)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"Traceback \(most recent|stderr:|\.py"", line \d+", RegexOptions.Multiline)]
    private static partial Regex TracebackRegex();
}

internal readonly record struct RoutingSignals(
    bool Debug,
    bool RepoArch,
    bool HighRisk,
    bool LongContext,
    bool StrictFormat,
    bool HasCodeBlock,
    bool HasFileReference,
    bool HasUrl,
    bool DeepConversation,
    bool Research,
    bool Planning);
