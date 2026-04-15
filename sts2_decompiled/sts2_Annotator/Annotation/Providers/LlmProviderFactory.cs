using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Sts2Extractor.Configuration;

namespace Sts2Extractor.Annotation.Providers;

internal static class LlmProviderFactory
{
    public static ILlmProvider Create(LlmProviderKind provider)
    {
        IConfiguration configuration = BuildConfiguration();

        if (provider == LlmProviderKind.Anthropic)
        {
            string apiKey = ResolveSetting(configuration, "Llm:AnthropicApiKey", "ANTHROPIC_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("Missing Anthropic API key. Set Llm:AnthropicApiKey in appconfig.json/user secrets or ANTHROPIC_API_KEY environment variable.");
            }

            return new AnthropicProvider(apiKey);
        }

        if (provider == LlmProviderKind.OpenAI)
        {
            string apiKey = ResolveSetting(configuration, "Llm:OpenAiApiKey", "OPENAI_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("Missing OpenAI API key. Set Llm:OpenAiApiKey in appconfig.json/user secrets or OPENAI_API_KEY environment variable.");
            }

            return new OpenAiProvider(apiKey);
        }

        throw new InvalidOperationException("Unsupported LLM provider.");
    }

    private static IConfiguration BuildConfiguration()
    {
        string basePath = Directory.GetCurrentDirectory();

        return new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appconfig.json", optional: true, reloadOnChange: false)
            .AddUserSecrets<UserSecretsMarker>(optional: true)
            .AddEnvironmentVariables()
            .Build();
    }

    private static string ResolveSetting(IConfiguration configuration, string configKey, string environmentKey)
    {
        string configuredValue = configuration[configKey] ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(configuredValue))
        {
            return configuredValue;
        }

        string envValue = Environment.GetEnvironmentVariable(environmentKey) ?? string.Empty;
        return envValue;
    }
}
