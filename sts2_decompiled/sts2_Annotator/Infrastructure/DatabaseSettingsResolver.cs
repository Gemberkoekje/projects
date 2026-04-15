using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Sts2Extractor.Cli;
using Sts2Extractor.Configuration;

namespace Sts2Extractor.Infrastructure;

internal static class DatabaseSettingsResolver
{
    public static string ResolveConnectionString(CliOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return options.ConnectionString;
        }

        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appconfig.json", optional: true, reloadOnChange: false)
            .AddUserSecrets<UserSecretsMarker>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        string configured = configuration["Database:ConnectionString"] ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        string envValue = Environment.GetEnvironmentVariable("STS2_POSTGRES_CONNECTION_STRING") ?? string.Empty;
        return envValue;
    }

    public static string RedactConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return string.Empty;
        }

        int atIndex = connectionString.IndexOf('@');
        if (atIndex <= 0)
        {
            return "postgres://configured";
        }

        return "postgres://***" + connectionString.Substring(atIndex, connectionString.Length - atIndex);
    }
}
