using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using TheCuratool.Application;
using TheCuratool.Infrastructure.Data;
using TheCuratool.Web.Api;

namespace TheCuratool.UnitTests;

public sealed class Phase8ApiTests : IClassFixture<Phase8ApiTests.CuratoolWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly CuratoolWebApplicationFactory _factory;

    public Phase8ApiTests(CuratoolWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PostScripts_ShouldPersistScriptAndReturnMetadata()
    {
        using var client = _factory.CreateClient();
        var rawJson = File.ReadAllText("NoVortox.json");

        using var response = await client.PostAsJsonAsync("/api/scripts", new ScriptUploadRequest { RawJson = rawJson });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ScriptApiResponse>(JsonOptions);
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload.Id);
        Assert.Equal("No Vortox", payload.Name);
        Assert.Equal(27, payload.CharacterCount);
    }

    [Fact]
    public async Task SessionEndpoints_ShouldSupportCreateReadAndSuggestionsFlow()
    {
        using var client = _factory.CreateClient();
        var scriptId = await UploadScriptAsync(client);

        using var createResponse = await client.PostAsJsonAsync("/api/sessions", new CreateSessionRequest
        {
            ScriptId = scriptId,
            PlayerCount = 7,
            ActiveLorics = new[] { "sentinel" },
            UseMarionette = false,
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<SessionApiResponse>(JsonOptions);
        Assert.NotNull(created);
        Assert.Equal(7, created.PlayerCount);
        Assert.Equal(GameStatus.Drafting, created.Status);
        Assert.Contains("sentinel", created.ActiveLorics);

        var sessionResponse = await client.GetAsync($"/api/sessions/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, sessionResponse.StatusCode);

        var suggestionsResponse = await client.GetAsync($"/api/sessions/{created.Id}/suggestions");
        Assert.Equal(HttpStatusCode.OK, suggestionsResponse.StatusCode);
        var suggestions = await suggestionsResponse.Content.ReadFromJsonAsync<List<CharacterApiResponse>>(JsonOptions);
        Assert.NotNull(suggestions);
        Assert.InRange(suggestions.Count, 1, 3);

        var remainingResponse = await client.GetAsync($"/api/sessions/{created.Id}/remaining");
        Assert.Equal(HttpStatusCode.OK, remainingResponse.StatusCode);
        var remaining = await remainingResponse.Content.ReadFromJsonAsync<List<CharacterApiResponse>>(JsonOptions);
        Assert.NotNull(remaining);
        Assert.True(remaining.Count >= suggestions.Count);
    }

    [Fact]
    public async Task MutationEndpoints_ShouldPersistCuratedOfferAndChoice()
    {
        using var client = _factory.CreateClient();
        var scriptId = await UploadScriptAsync(client);
        var sessionId = await CreateSessionAsync(client, scriptId);

        var suggestions = await client.GetFromJsonAsync<List<CharacterApiResponse>>($"/api/sessions/{sessionId}/suggestions", JsonOptions);
        Assert.NotNull(suggestions);
        Assert.NotEmpty(suggestions);

        var sessionBeforeMutation = await client.GetFromJsonAsync<SessionApiResponse>($"/api/sessions/{sessionId}", JsonOptions);
        Assert.NotNull(sessionBeforeMutation);

        using var curateResponse = await client.PostAsJsonAsync($"/api/sessions/{sessionId}/curated-offer", new CuratedOfferRequest
        {
            PlayerSlot = sessionBeforeMutation.CurrentPlayerSlot,
            OfferedIds = suggestions.Select(character => character.Id).ToArray(),
        });
        Assert.Equal(HttpStatusCode.OK, curateResponse.StatusCode);

        var chosenId = suggestions[0].Id;
        using var choiceResponse = await client.PostAsJsonAsync($"/api/sessions/{sessionId}/choices", new RecordChoiceRequest
        {
            PlayerSlot = sessionBeforeMutation.CurrentPlayerSlot,
            ChosenCharacterId = chosenId,
            OfferedIds = suggestions.Select(character => character.Id).ToArray(),
            HiddenFlags = HiddenFlagsRequest.Empty,
        });
        Assert.Equal(HttpStatusCode.OK, choiceResponse.StatusCode);

        var updated = await choiceResponse.Content.ReadFromJsonAsync<SessionApiResponse>(JsonOptions);
        Assert.NotNull(updated);
        Assert.NotEqual(sessionBeforeMutation.CurrentPlayerSlot, updated.CurrentPlayerSlot);
        var chosenSlot = Assert.Single(updated.Players.Where(slot => slot.DraftOrder == sessionBeforeMutation.CurrentPlayerSlot));
        Assert.True(chosenSlot.IsChosen);
        Assert.Equal(chosenId, chosenSlot.Choice.CharacterId);
    }

    [Fact]
    public async Task SwaggerEndpoint_ShouldBeAvailableAtSwagger()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<Guid> UploadScriptAsync(HttpClient client)
    {
        var rawJson = File.ReadAllText("NoVortox.json");
        using var response = await client.PostAsJsonAsync("/api/scripts", new ScriptUploadRequest { RawJson = rawJson });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ScriptApiResponse>(JsonOptions);
        Assert.NotNull(payload);
        return payload.Id;
    }

    private static async Task<Guid> CreateSessionAsync(HttpClient client, Guid scriptId)
    {
        using var response = await client.PostAsJsonAsync("/api/sessions", new CreateSessionRequest
        {
            ScriptId = scriptId,
            PlayerCount = 7,
            ActiveLorics = Array.Empty<string>(),
            UseMarionette = false,
        });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<SessionApiResponse>(JsonOptions);
        Assert.NotNull(payload);
        return payload.Id;
    }

    public sealed class CuratoolWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string _databaseName = $"curatool-api-{Guid.NewGuid()}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                var registrationsToRemove = services
                    .Where(service => service.ServiceType == typeof(DbContextOptions<CuratoolDbContext>)
                        || service.ServiceType == typeof(CuratoolDbContext)
                        || (service.ServiceType.IsGenericType
                            && service.ServiceType.GetGenericTypeDefinition() == typeof(IDbContextOptionsConfiguration<>)
                            && service.ServiceType.GenericTypeArguments[0] == typeof(CuratoolDbContext)))
                    .ToList();

                foreach (var registration in registrationsToRemove)
                {
                    services.Remove(registration);
                }

                services.AddDbContext<CuratoolDbContext>(options => options.UseInMemoryDatabase(_databaseName));

                var serviceProvider = services.BuildServiceProvider();
                using var scope = serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<CuratoolDbContext>();
                dbContext.Database.EnsureCreated();
            });
        }
    }
}
