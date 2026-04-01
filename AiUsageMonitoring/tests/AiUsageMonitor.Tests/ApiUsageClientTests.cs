using AiUsageMonitor.Clients;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AiUsageMonitor.Tests;

public sealed class ApiUsageClientTests
{
    [Fact]
    public async Task CopilotClient_UsesUsernameEndpointAndAggregatesPremiumRequests()
    {
        Uri? requestedUri = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            requestedUri = request.RequestUri;

            const string json = """
                {
                  "timePeriod": { "year": 2026, "month": 3 },
                  "user": "gemberkoekje",
                  "usageItems": [
                    {
                      "product": "Copilot",
                      "sku": "Copilot Premium Request",
                      "model": "GPT-5",
                      "unitType": "requests",
                      "pricePerUnit": 0.04,
                      "grossQuantity": 120,
                      "grossAmount": 4.8,
                      "discountQuantity": 0,
                      "discountAmount": 0,
                      "netQuantity": 120,
                      "netAmount": 4.8
                    },
                    {
                      "product": "Copilot",
                      "sku": "Copilot Premium Request",
                      "model": "GPT-4",
                      "unitType": "requests",
                      "pricePerUnit": 0.04,
                      "grossQuantity": 80,
                      "grossAmount": 3.2,
                      "discountQuantity": 0,
                      "discountAmount": 0,
                      "netQuantity": 80,
                      "netAmount": 3.2
                    }
                  ]
                }
                """;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json),
            };
        });

        var client = new CopilotUsageClient(
            new HttpClient(handler),
            BuildConfiguration(new Dictionary<string, string?>
            {
                ["CoPilot:ApiToken"] = "ghp_test",
                ["CoPilot:Username"] = "gemberkoekje",
            }),
            NullLogger<CopilotUsageClient>.Instance);

        var snapshot = await client.GetLatestUsageAsync();

        Assert.Equal(new Uri("https://api.github.com/users/gemberkoekje/settings/billing/premium_request/usage"), requestedUri);
        Assert.Equal(200, snapshot.TokensUsed);
        Assert.Equal(1, snapshot.SeatsUsed);
        Assert.Equal(8.0m, snapshot.CostIncurred);
    }

    private static IConfiguration BuildConfiguration(IReadOnlyDictionary<string, string?> values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request));
    }
}
