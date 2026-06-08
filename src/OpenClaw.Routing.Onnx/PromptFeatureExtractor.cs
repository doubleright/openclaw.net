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
    private const int HandcraftedDims = 51;
    private const int LexicalDims = 102;
    private const int ContextDims = 10;
    private const int HistoryDims = 16;
    private const int ReducedEmbeddingDims = 64;
    private const int EmbeddingTripletDims = ReducedEmbeddingDims * 3;
    private const int AssistantDims = 12;
    private const int ContinuationDims = 4;
    private const int ReasoningDims = 3;

    private static readonly string[] DebugKeywords = ["error", "bug", "exception", "traceback", "failed", "root cause", "报错", "根因", "修复", "stack trace", "debug"];
    private static readonly string[] ResearchKeywords = ["调研", "research", "对比", "compare", "survey", "分析报告", "competitive analysis", "综述"];
    private static readonly string[] ArchitectureKeywords = ["architecture", "架构", "重构", "refactor", "monorepo", "codebase", "module", "dependency"];
    private static readonly string[] CompareKeywords = ["对比", "compare", "audit", "审计", "review", "评估"];
    private static readonly string[] PlanningKeywords = ["plan", "planning", "方案", "计划", "roadmap", "milestone", "步骤", "实施"];
    private static readonly string[] StrictFormatKeywords = ["json", "yaml", "csv", "schema", "只返回", "不要解释", "按格式", "only return", "no explanation"];
    private static readonly string[] HighRiskKeywords = ["deploy", "rollback", "migration", "delete", "overwrite", "production", "生产", "部署", "删除", "客户", "法务", "财务"];
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
        var features = new float[
            HandcraftedDims + LexicalDims + ContextDims + HistoryDims + EmbeddingTripletDims + AssistantDims + ContinuationDims + ReasoningDims];

        var offset = 0;
        ExtractHandcrafted(input.CurrentUserText).CopyTo(features.AsSpan(offset, HandcraftedDims));
        offset += HandcraftedDims;

        ExtractLexical(input.CurrentUserText).CopyTo(features.AsSpan(offset, LexicalDims));
        offset += LexicalDims;

        ExtractContext(input).CopyTo(features.AsSpan(offset, ContextDims));
        offset += ContextDims;

        ExtractHistory(input).CopyTo(features.AsSpan(offset, HistoryDims));
        offset += HistoryDims;

        ReduceEmbedding(currentEmbedding).CopyTo(features.AsSpan(offset, ReducedEmbeddingDims));
        offset += ReducedEmbeddingDims;
        ReduceEmbedding(historyEmbedding).CopyTo(features.AsSpan(offset, ReducedEmbeddingDims));
        offset += ReducedEmbeddingDims;
        ReduceEmbedding(assistantEmbedding).CopyTo(features.AsSpan(offset, ReducedEmbeddingDims));
        offset += ReducedEmbeddingDims;

        ExtractAssistantFeatures(input.PreviousAssistantText).CopyTo(features.AsSpan(offset, AssistantDims));
        offset += AssistantDims;

        ExtractContinuationFeatures(input.CurrentUserText, input.PreviousAssistantText).CopyTo(features.AsSpan(offset, ContinuationDims));
        offset += ContinuationDims;

        ExtractReasoningFeatures(input.CurrentUserText, input.PreviousAssistantText).CopyTo(features.AsSpan(offset, ReasoningDims));
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
        var highRisk = KeywordCount(text, HighRiskKeywords) > 0 || (KeywordCount(text, ProductionKeywords) > 0 && KeywordCount(text, DeleteKeywords) > 0) || KeywordCount(text, CustomerKeywords) > 0;
        var strictFormat = KeywordCount(text, StrictFormatKeywords) > 0 || KeywordCount(text, ConstraintKeywords) > 1;
        var deepConversation = turnIndex >= 4;
        return new RoutingSignals(debug, repoArch, highRisk, longContext, strictFormat, hasCodeBlock, hasFileReference, hasUrl, deepConversation);
    }

    private static float[] ExtractHandcrafted(string text)
    {
        var features = new float[HandcraftedDims];
        var words = SplitWords(text);
        var lines = text.Split('\n');

        features[0] = text.Length;
        features[1] = words.Length;
        features[2] = lines.Length;
        features[3] = (float)text.Length / Math.Max(lines.Length, 1);

        var (zh, en, code) = CharTypeRatios(text);
        features[4] = zh;
        features[5] = en;
        features[6] = code;
        features[7] = zh > 0.1f && en > 0.1f ? 1f : 0f;

        var codeBlocks = CodeBlockRegex().Matches(text);
        features[8] = codeBlocks.Count > 0 ? 1f : 0f;
        features[9] = codeBlocks.Count;
        features[10] = codeBlocks.Sum(static block => block.Length);
        features[11] = JsonRegex().IsMatch(text) ? 1f : 0f;
        features[12] = YamlRegex().IsMatch(text) ? 1f : 0f;
        features[13] = CsvRegex().IsMatch(text) ? 1f : 0f;
        features[14] = TableRegex().IsMatch(text) ? 1f : 0f;

        features[15] = text.Count(static c => c is '?' or '？');
        features[16] = text.Count(static c => c is '!' or '！');
        features[17] = BulletRegex().Matches(text).Count;
        features[18] = NumberedRegex().Matches(text).Count;

        features[22] = KeywordCount(text, DebugKeywords);
        features[23] = KeywordCount(text, ResearchKeywords);
        features[24] = KeywordCount(text, ArchitectureKeywords);
        features[25] = KeywordCount(text, CompareKeywords);
        features[26] = KeywordCount(text, PlanningKeywords);
        features[27] = KeywordCount(text, StrictFormatKeywords);

        features[28] = KeywordCount(text, HighRiskKeywords);
        features[29] = KeywordCount(text, ProductionKeywords);
        features[30] = KeywordCount(text, CustomerKeywords);
        features[31] = KeywordCount(text, DeleteKeywords);
        features[32] = KeywordCount(text, FormalKeywords);

        features[33] = UrlRegex().Matches(text).Count;
        features[34] = FilePathRegex().Matches(text).Count;
        features[35] = LogRegex().IsMatch(text) ? 1f : 0f;
        features[36] = ShellRegex().IsMatch(text) ? 1f : 0f;
        features[37] = TracebackRegex().IsMatch(text) ? 1f : 0f;

        features[38] = KeywordCount(text, ConstraintKeywords);
        var quoted = QuotedSegmentRegex().Matches(text);
        features[39] = quoted.Sum(static match => match.Length) / Math.Max((float)text.Length, 1f);
        features[40] = words.Length == 0 ? 0f : words.Select(static word => word.ToLowerInvariant()).Distinct(StringComparer.Ordinal).Count() / (float)words.Length;

        features[41] = KeywordCount(text, TeachingKeywords);
        features[42] = KeywordCount(text, ImplementKeywords);
        var fileRefs = FilePathRegex().Matches(text).Select(static match => match.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        features[43] = fileRefs == 0 ? 1f : 0f;
        features[44] = fileRefs is >= 1 and <= 2 ? 1f : 0f;
        features[45] = fileRefs >= 3 ? 1f : 0f;
        features[46] = codeBlocks.Count > 0 && features[22] == 0 ? 1f : 0f;
        features[47] = KeywordCount(text, ComplaintKeywords);
        features[48] = text.Contains("table", StringComparison.OrdinalIgnoreCase) || text.Contains("表格", StringComparison.Ordinal) ? 1f : 0f;
        features[49] = text.Contains("json", StringComparison.OrdinalIgnoreCase) || text.Contains("yaml", StringComparison.OrdinalIgnoreCase) ? 1f : 0f;
        features[50] = text.Contains("architecture", StringComparison.OrdinalIgnoreCase) || text.Contains("架构", StringComparison.Ordinal) ? 1f : 0f;
        return features;
    }

    private static float[] ExtractLexical(string text)
    {
        var features = new float[LexicalDims];
        var words = SplitWords(text);
        if (words.Length == 0)
            return features;

        foreach (var word in words)
        {
            var bucket = (int)(Fnv1aHash(word) % (uint)LexicalDims);
            features[bucket] += 1f;
        }

        var scale = words.Length;
        for (var i = 0; i < features.Length; i++)
            features[i] /= scale;

        return features;
    }

    private static float[] ExtractContext(RoutingFeatureInput input)
    {
        var features = new float[ContextDims];
        var text = input.CurrentUserText;
        var signals = ExtractSignals(text, input.TurnIndex);

        features[0] = Math.Min(Math.Max(input.TurnIndex, 0), 20) / 20f;
        features[1] = Math.Min(Math.Max(input.ContextTextLength, 0), 20000) / 20000f;
        features[2] = Math.Min(Math.Max(input.ToolCount, 0), 8) / 8f;
        features[3] = Math.Min(input.PreviousAssistantText?.Length ?? 0, 5000) / 5000f;
        features[4] = signals.HasCodeBlock ? 1f : 0f;
        features[5] = signals.HasFileReference ? 1f : 0f;
        features[6] = signals.HasUrl ? 1f : 0f;
        features[7] = input.PreviousAssistantText is not null ? 1f : 0f;
        features[8] = signals.DeepConversation ? 1f : 0f;
        features[9] = input.ContextTextLength > 2000 ? 1f : 0f;
        return features;
    }

    private static float[] ExtractHistory(RoutingFeatureInput input)
    {
        var features = new float[HistoryDims];
        var previousTierIndex = TierIndex(input.PreviousTier);

        features[0] = previousTierIndex;
        features[1] = previousTierIndex < 0 ? 0f : previousTierIndex;
        features[2] = 0f;
        features[3] = previousTierIndex;
        features[4] = input.TurnIndex + 1;
        features[5] = input.TurnIndex;
        features[6] = previousTierIndex;
        features[7] = input.TurnIndex > 1 ? 1f : 0f;

        var trajectoryIndex = input.TurnIndex switch
        {
            <= 0 => 0,
            1 => 1,
            <= 3 => 2,
            _ => 3
        };
        features[8 + trajectoryIndex] = 1f;
        return features;
    }

    private static float[] ExtractAssistantFeatures(string? previousAssistantText)
    {
        var features = new float[AssistantDims];
        if (string.IsNullOrWhiteSpace(previousAssistantText))
            return features;

        features[0] = previousAssistantText.Length;
        features[1] = previousAssistantText.Contains("?", StringComparison.Ordinal) ? 1f : 0f;
        features[2] = previousAssistantText.Contains("cannot", StringComparison.OrdinalIgnoreCase) || previousAssistantText.Contains("can't", StringComparison.OrdinalIgnoreCase) ? 1f : 0f;
        features[3] = previousAssistantText.Contains("need more", StringComparison.OrdinalIgnoreCase) || previousAssistantText.Contains("clarify", StringComparison.OrdinalIgnoreCase) ? 1f : 0f;
        features[4] = previousAssistantText.Contains("tool", StringComparison.OrdinalIgnoreCase) ? 1f : 0f;
        features[5] = previousAssistantText.Contains("```", StringComparison.Ordinal) ? 1f : 0f;
        features[6] = UrlRegex().Matches(previousAssistantText).Count;
        features[7] = FilePathRegex().Matches(previousAssistantText).Count;
        features[8] = LogRegex().IsMatch(previousAssistantText) ? 1f : 0f;
        features[9] = previousAssistantText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        features[10] = previousAssistantText.Contains("sorry", StringComparison.OrdinalIgnoreCase) ? 1f : 0f;
        features[11] = previousAssistantText.Contains("next", StringComparison.OrdinalIgnoreCase) ? 1f : 0f;
        return features;
    }

    private static float[] ExtractContinuationFeatures(string currentUserText, string? previousAssistantText)
    {
        var features = new float[ContinuationDims];
        var lower = currentUserText.ToLowerInvariant();
        features[0] = lower.Contains("continue") || currentUserText.Contains("继续", StringComparison.Ordinal) ? 1f : 0f;
        features[1] = lower.Contains("follow-up") || currentUserText.Contains("接着", StringComparison.Ordinal) ? 1f : 0f;
        features[2] = previousAssistantText is null ? 0f : 1f;
        features[3] = lower.Contains("thanks") || currentUserText.Contains("谢谢", StringComparison.Ordinal) ? 1f : 0f;
        return features;
    }

    private static float[] ExtractReasoningFeatures(string currentUserText, string? previousAssistantText)
    {
        var features = new float[ReasoningDims];
        features[0] = previousAssistantText is null ? 0f : Math.Min(previousAssistantText.Length, 4000) / 4000f;
        features[1] = currentUserText.Contains("why", StringComparison.OrdinalIgnoreCase) || currentUserText.Contains("为什么", StringComparison.Ordinal) ? 1f : 0f;
        features[2] = currentUserText.Contains("verify", StringComparison.OrdinalIgnoreCase) || currentUserText.Contains("验证", StringComparison.Ordinal) ? 1f : 0f;
        return features;
    }

    private static float[] ReduceEmbedding(ReadOnlySpan<float> embedding)
    {
        var reduced = new float[ReducedEmbeddingDims];
        if (embedding.IsEmpty)
            return reduced;

        for (var i = 0; i < ReducedEmbeddingDims; i++)
        {
            var start = i * embedding.Length / ReducedEmbeddingDims;
            var end = (i + 1) * embedding.Length / ReducedEmbeddingDims;
            if (end <= start)
                end = Math.Min(start + 1, embedding.Length);

            var sum = 0f;
            for (var j = start; j < end; j++)
                sum += embedding[j];

            reduced[i] = sum / Math.Max(end - start, 1);
        }

        return reduced;
    }

    private static (float Zh, float En, float Code) CharTypeRatios(string text)
    {
        if (string.IsNullOrEmpty(text))
            return (0f, 0f, 0f);

        float zh = 0;
        float en = 0;
        float code = 0;
        foreach (var character in text)
        {
            if (character is >= '\u4e00' and <= '\u9fff')
                zh += 1;
            else if (char.IsAscii(character) && char.IsLetter(character))
                en += 1;
            else if ("{}[]();<>_/\\`=$#".Contains(character))
                code += 1;
        }

        var total = Math.Max(text.Length, 1);
        return (zh / total, en / total, code / total);
    }

    private static int KeywordCount(string text, IEnumerable<string> keywords)
        => keywords.Count(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private static string[] SplitWords(string text)
        => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    // FNV-1a 32-bit deterministic hash — stable across process restarts, unlike
    // StringComparer.OrdinalIgnoreCase.GetHashCode which is randomised by default in .NET.
    private static uint Fnv1aHash(ReadOnlySpan<char> text)
    {
        const uint fnvPrime = 16777619u;
        var hash = 2166136261u;
        foreach (var c in text)
        {
            var lower = char.ToLowerInvariant(c);
            hash = (hash ^ (uint)(lower & 0xFF)) * fnvPrime;
            hash = (hash ^ (uint)(lower >> 8)) * fnvPrime;
        }
        return hash;
    }

    private static int TierIndex(string? tier)
        => tier?.Trim().ToUpperInvariant() switch
        {
            "T0" => 0,
            "T1" => 1,
            "T2" => 2,
            "T3" => 3,
            _ => -1
        };

    [GeneratedRegex(@"```[\s\S]*?```", RegexOptions.Multiline)]
    private static partial Regex CodeBlockRegex();

    [GeneratedRegex("\\{[\\s\\S]*?[\\\"'][\\w]+[\\\"']\\s*:", RegexOptions.Multiline)]
    private static partial Regex JsonRegex();

    [GeneratedRegex(@"^[\w_]+:\s+\S", RegexOptions.Multiline)]
    private static partial Regex YamlRegex();

    [GeneratedRegex(@"^[^,\n]+,[^,\n]+,[^,\n]+", RegexOptions.Multiline)]
    private static partial Regex CsvRegex();

    [GeneratedRegex(@"\|.*\|.*\|", RegexOptions.Multiline)]
    private static partial Regex TableRegex();

    [GeneratedRegex(@"(?:^|[\s""'`(])([a-zA-Z_][\w.-]*/[\w./-]+\.[\w]+)", RegexOptions.Multiline)]
    private static partial Regex FilePathRegex();

    [GeneratedRegex(@"https?://\S+", RegexOptions.Multiline)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"(\d{4}[-/]\d{2}[-/]\d{2}[\sT]\d{2}:\d{2}.*\n){3,}|(^\[?(INFO|WARN|ERROR|DEBUG)\]?\s.*\n){3,}", RegexOptions.Multiline)]
    private static partial Regex LogRegex();

    [GeneratedRegex(@"^\$\s+\w|^>\s+\w|```(?:bash|sh|shell)", RegexOptions.Multiline)]
    private static partial Regex ShellRegex();

    [GeneratedRegex(@"Traceback \(most recent|stderr:|\.py"", line \d+", RegexOptions.Multiline)]
    private static partial Regex TracebackRegex();

    [GeneratedRegex(@"^[\s]*[-*]\s", RegexOptions.Multiline)]
    private static partial Regex BulletRegex();

    [GeneratedRegex(@"^[\s]*\d+[.)]\s", RegexOptions.Multiline)]
    private static partial Regex NumberedRegex();

    [GeneratedRegex("[\"'`].*?[\"'`]")]
    private static partial Regex QuotedSegmentRegex();
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
    bool DeepConversation);