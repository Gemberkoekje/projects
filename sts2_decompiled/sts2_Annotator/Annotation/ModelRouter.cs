using System;

namespace Sts2Extractor.Annotation;

internal static class ModelRouter
{
    public static string Resolve(LlmProviderKind provider, AnnotationTaskKind task)
    {
        if (provider == LlmProviderKind.Anthropic)
        {
            if (task == AnnotationTaskKind.Synergy || task == AnnotationTaskKind.ArchetypeDiscovery)
            {
                return "claude-sonnet-4-6";
            }

            return "claude-haiku-4-5-20251001";
        }

        if (provider == LlmProviderKind.OpenAI)
        {
            if (task == AnnotationTaskKind.Synergy || task == AnnotationTaskKind.ArchetypeDiscovery)
            {
                return "gpt-4o";
            }

            return "gpt-4o-mini";
        }

        throw new InvalidOperationException("Unsupported provider for model routing.");
    }
}
