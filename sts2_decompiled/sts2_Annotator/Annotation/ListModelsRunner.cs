using System;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Sts2Extractor.Configuration;
using Sts2Extractor.Cli;

namespace Sts2Extractor.Annotation;

internal sealed class ListModelsRunner
{
    public int Run(CliOptions options)
    {
        IConfiguration configuration = BuildConfiguration();
        string apiKey = ResolveSetting(configuration, "Llm:AnthropicApiKey", "ANTHROPIC_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Console.Error.WriteLine("Missing Anthropic API key. Set Llm:AnthropicApiKey in appconfig.json/user secrets or ANTHROPIC_API_KEY environment variable.");
            return 2;
        }

        try
        {
            using var client = new System.Net.Http.HttpClient();
            client.DefaultRequestHeaders.Add("x-api-key", apiKey);
            client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
            var resp = client.GetAsync("https://api.anthropic.com/v1/models", CancellationToken.None).GetAwaiter().GetResult();
            string text = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"Anthropic request failed ({(int)resp.StatusCode}): {text}");
                return 3;
            }

            string outPath = string.IsNullOrWhiteSpace(options.OutputPath)
                ? Path.Combine(Directory.GetCurrentDirectory(), "anthropic_models.json")
                : options.OutputPath;

            File.WriteAllText(outPath, text);
            Console.WriteLine($"Models written to: {outPath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 4;
        }
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
