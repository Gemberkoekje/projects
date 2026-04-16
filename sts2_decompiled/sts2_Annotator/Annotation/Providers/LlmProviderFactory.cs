using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Sts2Extractor.Cli;
using Sts2Extractor.Configuration;

namespace Sts2Extractor.Annotation.Providers;

internal static class LlmProviderFactory
{
    public static ILlmProvider Create(CliOptions options, string model)
    {
        IConfiguration configuration = BuildConfiguration();
        LlmRateLimitSettings rateLimitSettings = ResolveRateLimitSettings(options.Provider, model, options);

        if (options.Provider == LlmProviderKind.Anthropic)
        {
            string apiKey = ResolveSetting(configuration, "Llm:AnthropicApiKey", "ANTHROPIC_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("Missing Anthropic API key. Set Llm:AnthropicApiKey in appconfig.json/user secrets or ANTHROPIC_API_KEY environment variable.");
            }

            return new AnthropicProvider(apiKey, rateLimitSettings);
        }

        if (options.Provider == LlmProviderKind.OpenAI)
        {
            string apiKey = ResolveSetting(configuration, "Llm:OpenAiApiKey", "OPENAI_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("Missing OpenAI API key. Set Llm:OpenAiApiKey in appconfig.json/user secrets or OPENAI_API_KEY environment variable.");
            }

            return new OpenAiProvider(apiKey, rateLimitSettings);
        }

        throw new InvalidOperationException("Unsupported LLM provider.");
    }

    private static LlmRateLimitSettings ResolveRateLimitSettings(LlmProviderKind provider, string model, CliOptions options)
    {
        if (options.RequestsPerMinute > 0 || options.InputTokensPerMinute > 0 || options.OutputTokensPerMinute > 0)
        {
            return new LlmRateLimitSettings
            {
                RequestsPerMinute = options.RequestsPerMinute,
                InputTokensPerMinute = options.InputTokensPerMinute,
                OutputTokensPerMinute = options.OutputTokensPerMinute
            };
        }

        if (provider == LlmProviderKind.Anthropic)
        {
            if (model.IndexOf("haiku", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new LlmRateLimitSettings
                {
                    RequestsPerMinute = 50,
                    InputTokensPerMinute = 50000,
                    OutputTokensPerMinute = 10000
                };
            }

            return new LlmRateLimitSettings
            {
                RequestsPerMinute = 50,
                InputTokensPerMinute = 30000,
                OutputTokensPerMinute = 8000
            };
        }

        return new LlmRateLimitSettings
        {
            RequestsPerMinute = 0,
            InputTokensPerMinute = 0,
            OutputTokensPerMinute = 0
        };
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
