using System;
using AiUsageMonitor;
using AiUsageMonitor.Projections;
using JasperFx.Events.Projections;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((_, config) =>
    {
        config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
        config.AddEnvironmentVariables();
    })
    .ConfigureServices((context, services) =>
    {
        services.AddMarten(sp =>
        {
            var connectionString = context.Configuration.GetConnectionString("Marten")
                ?? throw new InvalidOperationException("Marten connection string not configured");

            var options = new StoreOptions();
            options.Connection(connectionString);

            options.Projections.Add(new UsageSummaryProjection(), ProjectionLifecycle.Inline);

            return options;
        });

        // Register HTTP clients and API clients via typed clients
        services.AddHttpClient<AiUsageMonitor.Clients.CopilotUsageClient>((_, client) =>
        {
            client.BaseAddress = new Uri("https://api.github.com/");
        });

        services.AddSingleton<AiUsageMonitor.Clients.ICopilotUsageClient>(sp => sp.GetRequiredService<AiUsageMonitor.Clients.CopilotUsageClient>());

        // Register burndown calculator
        services.AddSingleton<AiUsageMonitor.Services.IBurndownCalculator, AiUsageMonitor.Services.BurndownCalculator>();

        // Register spike detector
        services.AddSingleton<AiUsageMonitor.Services.ISpikeDetector, AiUsageMonitor.Services.SpikeDetector>();

        // Register daily report gate
        services.AddSingleton<AiUsageMonitor.Services.IDailyReportGate, AiUsageMonitor.Services.DailyReportGate>();

        // Register email service
        services.AddSingleton<AiUsageMonitor.Services.IEmailService, AiUsageMonitor.Services.EmailService>();

        services.AddHostedService<Worker>();
    })
    .ConfigureLogging(logging => logging.AddSimpleConsole())
    .Build();

await host.RunAsync();
