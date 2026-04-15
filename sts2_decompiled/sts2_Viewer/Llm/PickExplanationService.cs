using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Sts2Viewer.Data;

namespace Sts2Viewer.Llm;

public sealed class PickExplanationService
{
    private readonly IConfiguration _configuration;
    private readonly LlmProviderFactory _factory;

    public PickExplanationService(IConfiguration configuration, LlmProviderFactory factory)
    {
        _configuration = configuration;
        _factory = factory;
    }

    public async Task<string> ExplainAsync(IReadOnlyList<PickScoreRow> scores, CancellationToken cancellationToken)
    {
        if (scores.Count == 0)
        {
            return string.Empty;
        }

        LlmProviderKind providerKind = ResolveProvider(_configuration["Llm:Provider"] ?? string.Empty);
        string model = ResolveModel(providerKind);
        ILlmProvider provider = _factory.Create(providerKind);

        StringBuilder promptBuilder = new StringBuilder();
        promptBuilder.AppendLine("Given these scored options, explain why the top-ranked pick is best. Keep it concise (2-4 sentences).");
        promptBuilder.AppendLine("Focus on deck-fit, archetype affinity, anti-synergy penalties, and flexibility.");
        promptBuilder.AppendLine();
        for (int i = 0; i < scores.Count; i++)
        {
            PickScoreRow score = scores[i];
            promptBuilder.AppendLine(
                $"Rank {score.Rank}: {score.EntityId} (score={score.CompositeScore * 10:F2}, deck={score.EdgeOverlapScore * 10:F1}, arch={score.ArchetypeFitScore * 10:F1}, anti={score.AntiSynergyPenalty * 10:F1}, flex={score.FlexibilityScore * 10:F1})");
            if (score.SynergyDrivers.Count > 0)
            {
                promptBuilder.AppendLine($"  Drivers: {string.Join("; ", score.SynergyDrivers)}");
            }
        }

        LlmRequest request = new LlmRequest
        {
            Model = model,
            SystemPrompt = "You are an expert Slay the Spire 2 pick advisor. Be concise and specific.",
            Prompt = promptBuilder.ToString(),
            MaxTokens = 350
        };

        LlmResult result = await provider.CompleteAsync(request, cancellationToken);
        return result.Content;
    }

    private static LlmProviderKind ResolveProvider(string configured)
    {
        if (string.Equals(configured, "openai", System.StringComparison.OrdinalIgnoreCase))
        {
            return LlmProviderKind.OpenAI;
        }

        return LlmProviderKind.Anthropic;
    }

    private static string ResolveModel(LlmProviderKind providerKind)
    {
        if (providerKind == LlmProviderKind.OpenAI)
        {
            return "gpt-4o";
        }

        return "claude-sonnet-4-6";
    }
}
