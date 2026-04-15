using System;
using System.Net.Http;
using Microsoft.Extensions.Configuration;

namespace Sts2Viewer.Llm;

public sealed class LlmProviderFactory
{
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;

    public LlmProviderFactory(IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
    }

    public ILlmProvider Create(LlmProviderKind providerKind)
    {
        if (providerKind == LlmProviderKind.Anthropic)
        {
            string apiKey = ResolveSetting("Llm:AnthropicApiKey", "ANTHROPIC_API_KEY");
            return new AnthropicProvider(_httpClientFactory.CreateClient("llm"), apiKey);
        }

        if (providerKind == LlmProviderKind.OpenAI)
        {
            string apiKey = ResolveSetting("Llm:OpenAiApiKey", "OPENAI_API_KEY");
            return new OpenAiProvider(_httpClientFactory.CreateClient("llm"), apiKey);
        }

        throw new InvalidOperationException("Unsupported LLM provider.");
    }

    private string ResolveSetting(string configKey, string environmentKey)
    {
        string configuredValue = _configuration[configKey] ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(configuredValue))
        {
            return configuredValue;
        }

        string envValue = Environment.GetEnvironmentVariable(environmentKey) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(envValue))
        {
            return envValue;
        }

        throw new InvalidOperationException("Missing required LLM API key.");
    }
}
