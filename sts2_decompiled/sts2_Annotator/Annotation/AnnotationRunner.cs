using System;
using System.IO;
using System.Threading;
using Sts2Extractor.Annotation.Providers;
using Sts2Extractor.Cli;

namespace Sts2Extractor.Annotation;

internal sealed class AnnotationRunner
{
    public AnnotationResult Run(CliOptions options)
    {
        string prompt = ReadPrompt(options.PromptText, options.PromptFilePath, "prompt");
        string systemPrompt = ReadPrompt(options.SystemPromptText, options.SystemPromptFilePath, "system prompt");
        if (string.IsNullOrWhiteSpace(systemPrompt))
        {
            systemPrompt = "You are an expert Slay the Spire 2 mechanics annotator. Return compact JSON only.";
        }

        string model = string.IsNullOrWhiteSpace(options.ModelOverride)
            ? ModelRouter.Resolve(options.Provider, options.AnnotationTask)
            : options.ModelOverride;

        ILlmProvider provider = LlmProviderFactory.Create(options.Provider);

        AnnotationRequest request = new AnnotationRequest
        {
            Model = model,
            Prompt = prompt,
            SystemPrompt = systemPrompt,
            MaxTokens = options.MaxTokens
        };

        return provider.AnnotateAsync(request, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static string ReadPrompt(string inlineValue, string filePath, string fieldName)
    {
        if (!string.IsNullOrWhiteSpace(inlineValue))
        {
            return inlineValue;
        }

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return string.Empty;
        }

        if (!File.Exists(filePath))
        {
            throw new InvalidOperationException($"The {fieldName} file was not found: {filePath}");
        }

        return File.ReadAllText(filePath);
    }
}
