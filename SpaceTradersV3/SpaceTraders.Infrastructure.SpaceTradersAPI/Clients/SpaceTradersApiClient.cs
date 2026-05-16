using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Configuration;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Exceptions;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Accounts;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Agents;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Common;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Contracts;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Factions;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Fleet;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Markets;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Shipyards;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Status;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Systems;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;

public sealed class SpaceTradersApiClient(
    HttpClient httpClient,
    IOptions<SpaceTradersApiOptions> options,
    IAgentTokenProvider agentTokenProvider,
    IApiEndpointUsageRecorder endpointUsageRecorder) : ISpaceTradersApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient = httpClient;
    private readonly SpaceTradersApiOptions _options = options.Value;
    private readonly IAgentTokenProvider _agentTokenProvider = agentTokenProvider;
    private readonly IApiEndpointUsageRecorder _endpointUsageRecorder = endpointUsageRecorder;

    public Task<ServerStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        => GetAsync<ServerStatus>(string.Empty, AuthMode.None, cancellationToken);

    public Task<PagedApiResponse<Faction>> GetFactionsAsync(int page = 1, int limit = 20, CancellationToken cancellationToken = default)
        => GetAsync<PagedApiResponse<Faction>>($"factions?page={page}&limit={limit}", AuthMode.None, cancellationToken);

    public Task<Faction> GetFactionAsync(string factionSymbol, CancellationToken cancellationToken = default)
        => GetWrappedAsync<Faction>($"factions/{Uri.EscapeDataString(factionSymbol)}", AuthMode.None, cancellationToken);

    public Task<PagedApiResponse<PublicAgent>> GetAgentsAsync(int page = 1, int limit = 20, CancellationToken cancellationToken = default)
        => GetAsync<PagedApiResponse<PublicAgent>>($"agents?page={page}&limit={limit}", AuthMode.None, cancellationToken);

    public Task<PublicAgent> GetAgentAsync(string agentSymbol, CancellationToken cancellationToken = default)
        => GetWrappedAsync<PublicAgent>($"agents/{Uri.EscapeDataString(agentSymbol)}", AuthMode.None, cancellationToken);

    public Task<PagedApiResponse<SystemInfo>> GetSystemsAsync(int page = 1, int limit = 20, CancellationToken cancellationToken = default)
        => GetAsync<PagedApiResponse<SystemInfo>>($"systems?page={page}&limit={limit}", AuthMode.None, cancellationToken);

    public Task<SystemInfo> GetSystemAsync(string systemSymbol, CancellationToken cancellationToken = default)
        => GetWrappedAsync<SystemInfo>($"systems/{Uri.EscapeDataString(systemSymbol)}", AuthMode.None, cancellationToken);

    public Task<PagedApiResponse<Waypoint>> GetWaypointsAsync(string systemSymbol, int page = 1, int limit = 20, CancellationToken cancellationToken = default)
        => GetAsync<PagedApiResponse<Waypoint>>($"systems/{Uri.EscapeDataString(systemSymbol)}/waypoints?page={page}&limit={limit}", AuthMode.None, cancellationToken);

    public Task<Waypoint> GetWaypointAsync(string systemSymbol, string waypointSymbol, CancellationToken cancellationToken = default)
        => GetWrappedAsync<Waypoint>($"systems/{Uri.EscapeDataString(systemSymbol)}/waypoints/{Uri.EscapeDataString(waypointSymbol)}", AuthMode.None, cancellationToken);

    public Task<RegisterResponseData> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
        => PostWrappedAsync<RegisterRequest, RegisterResponseData>("register", request, AuthMode.AccountToken, cancellationToken);

    public Task<Agent> GetMyAgentAsync(CancellationToken cancellationToken = default)
        => GetWrappedAsync<Agent>("my/agent", AuthMode.AgentToken, cancellationToken);

    public Task<PagedApiResponse<Ship>> GetMyShipsAsync(int page = 1, int limit = 20, CancellationToken cancellationToken = default)
        => GetAsync<PagedApiResponse<Ship>>($"my/ships?page={page}&limit={limit}", AuthMode.AgentToken, cancellationToken);

    public Task<Ship> GetMyShipAsync(string shipSymbol, CancellationToken cancellationToken = default)
        => GetWrappedAsync<Ship>($"my/ships/{Uri.EscapeDataString(shipSymbol)}", AuthMode.AgentToken, cancellationToken);

    public Task<PagedApiResponse<Contract>> GetMyContractsAsync(int page = 1, int limit = 20, CancellationToken cancellationToken = default)
        => GetAsync<PagedApiResponse<Contract>>($"my/contracts?page={page}&limit={limit}", AuthMode.AgentToken, cancellationToken);

    public Task<NavigateResult> NavigateShipAsync(string shipSymbol, string waypointSymbol, CancellationToken cancellationToken = default)
        => PostWrappedAsync<object, NavigateResult>(
            $"my/ships/{Uri.EscapeDataString(shipSymbol)}/navigate",
            new { waypointSymbol },
            AuthMode.AgentToken,
            cancellationToken);

    public Task<ShipNavResult> DockShipAsync(string shipSymbol, CancellationToken cancellationToken = default)
        => PostWrappedAsync<object?, ShipNavResult>(
            $"my/ships/{Uri.EscapeDataString(shipSymbol)}/dock",
            null,
            AuthMode.AgentToken,
            cancellationToken);

    public Task<ShipNavResult> OrbitShipAsync(string shipSymbol, CancellationToken cancellationToken = default)
        => PostWrappedAsync<object?, ShipNavResult>(
            $"my/ships/{Uri.EscapeDataString(shipSymbol)}/orbit",
            null,
            AuthMode.AgentToken,
            cancellationToken);

    public Task<ExtractResult> ExtractResourcesAsync(string shipSymbol, CancellationToken cancellationToken = default)
        => PostWrappedAsync<object?, ExtractResult>(
            $"my/ships/{Uri.EscapeDataString(shipSymbol)}/extract",
            null,
            AuthMode.AgentToken,
            cancellationToken);

    public Task<SellCargoResult> SellCargoAsync(string shipSymbol, string tradeSymbol, int units, CancellationToken cancellationToken = default)
        => PostWrappedAsync<object, SellCargoResult>(
            $"my/ships/{Uri.EscapeDataString(shipSymbol)}/sell",
            new { symbol = tradeSymbol, units },
            AuthMode.AgentToken,
            cancellationToken);

    public Task<BuyCargoResult> BuyCargoAsync(string shipSymbol, string tradeSymbol, int units, CancellationToken cancellationToken = default)
        => PostWrappedAsync<object, BuyCargoResult>(
            $"my/ships/{Uri.EscapeDataString(shipSymbol)}/purchase",
            new { symbol = tradeSymbol, units },
            AuthMode.AgentToken,
            cancellationToken);

    public Task<RefuelResult> RefuelShipAsync(string shipSymbol, bool fromCargo = false, CancellationToken cancellationToken = default)
        => PostWrappedAsync<object?, RefuelResult>(
            $"my/ships/{Uri.EscapeDataString(shipSymbol)}/refuel",
            fromCargo ? new { fromCargo = true } : null,
            AuthMode.AgentToken,
            cancellationToken);

    public Task<PurchaseShipResult> PurchaseShipAsync(string shipType, string waypointSymbol, CancellationToken cancellationToken = default)
        => PostWrappedAsync<object, PurchaseShipResult>(
            "my/ships",
            new { shipType, waypointSymbol },
            AuthMode.AgentToken,
            cancellationToken);

    public Task<AcceptContractResult> AcceptContractAsync(string contractId, CancellationToken cancellationToken = default)
        => PostWrappedAsync<object?, AcceptContractResult>(
            $"my/contracts/{Uri.EscapeDataString(contractId)}/accept",
            null,
            AuthMode.AgentToken,
            cancellationToken);

    public Task<DeliverContractResult> DeliverContractAsync(string contractId, DeliverContractRequest request, CancellationToken cancellationToken = default)
        => PostWrappedAsync<DeliverContractRequest, DeliverContractResult>(
            $"my/contracts/{Uri.EscapeDataString(contractId)}/deliver",
            request,
            AuthMode.AgentToken,
            cancellationToken);

    public Task<FulfillContractResult> FulfillContractAsync(string contractId, CancellationToken cancellationToken = default)
        => PostWrappedAsync<object?, FulfillContractResult>(
            $"my/contracts/{Uri.EscapeDataString(contractId)}/fulfill",
            null,
            AuthMode.AgentToken,
            cancellationToken);

    public Task<Market> GetMarketAsync(string systemSymbol, string waypointSymbol, CancellationToken cancellationToken = default)
        => GetWrappedAsync<Market>(
            $"systems/{Uri.EscapeDataString(systemSymbol)}/waypoints/{Uri.EscapeDataString(waypointSymbol)}/market",
            AuthMode.AgentToken,
            cancellationToken);

    public Task<Shipyard> GetShipyardAsync(string systemSymbol, string waypointSymbol, CancellationToken cancellationToken = default)
        => GetWrappedAsync<Shipyard>(
            $"systems/{Uri.EscapeDataString(systemSymbol)}/waypoints/{Uri.EscapeDataString(waypointSymbol)}/shipyard",
            AuthMode.AgentToken,
            cancellationToken);

    public Task<ShipCargo> GetShipCargoAsync(string shipSymbol, CancellationToken cancellationToken = default)
        => GetWrappedAsync<ShipCargo>(
            $"my/ships/{Uri.EscapeDataString(shipSymbol)}/cargo",
            AuthMode.AgentToken,
            cancellationToken);

    public Task<JettisonResult> JettisonCargoAsync(string shipSymbol, string tradeSymbol, int units, CancellationToken cancellationToken = default)
        => PostWrappedAsync<object, JettisonResult>(
            $"my/ships/{Uri.EscapeDataString(shipSymbol)}/jettison",
            new { symbol = tradeSymbol, units },
            AuthMode.AgentToken,
            cancellationToken);

    public Task<NegotiateContractResult> NegotiateContractAsync(string shipSymbol, CancellationToken cancellationToken = default)
        => PostWrappedAsync<object?, NegotiateContractResult>(
            $"my/ships/{Uri.EscapeDataString(shipSymbol)}/negotiate/contract",
            null,
            AuthMode.AgentToken,
            cancellationToken);

    public Task<PatchShipNavResult> PatchShipNavAsync(string shipSymbol, string flightMode, CancellationToken cancellationToken = default)
        => PatchWrappedAsync<object, PatchShipNavResult>(
            $"my/ships/{Uri.EscapeDataString(shipSymbol)}/nav",
            new { flightMode },
            AuthMode.AgentToken,
            cancellationToken);

    public Task<SurveyResult> SurveyAsync(string shipSymbol, CancellationToken cancellationToken = default)
        => PostWrappedAsync<object?, SurveyResult>(
            $"my/ships/{Uri.EscapeDataString(shipSymbol)}/survey",
            null,
            AuthMode.AgentToken,
            cancellationToken);

    public Task<ExtractResult> ExtractWithSurveyAsync(string shipSymbol, Survey survey, CancellationToken cancellationToken = default)
        => PostWrappedAsync<ExtractWithSurveyRequest, ExtractResult>(
            $"my/ships/{Uri.EscapeDataString(shipSymbol)}/extract/survey",
            new ExtractWithSurveyRequest
            {
                Signature = survey.Signature,
                Symbol = survey.Symbol,
                Deposits = survey.Deposits,
                Expiration = survey.Expiration,
                Size = survey.Size,
            },
            AuthMode.AgentToken,
            cancellationToken);

    public Task<SiphonResult> SiphonResourcesAsync(string shipSymbol, CancellationToken cancellationToken = default)
        => PostWrappedAsync<object?, SiphonResult>(
            $"my/ships/{Uri.EscapeDataString(shipSymbol)}/siphon",
            null,
            AuthMode.AgentToken,
            cancellationToken);

    public Task<WarpResult> WarpShipAsync(string shipSymbol, string waypointSymbol, CancellationToken cancellationToken = default)
        => PostWrappedAsync<object, WarpResult>(
            $"my/ships/{Uri.EscapeDataString(shipSymbol)}/warp",
            new { waypointSymbol },
            AuthMode.AgentToken,
            cancellationToken);

    public Task<JumpResult> JumpShipAsync(string shipSymbol, string systemSymbol, CancellationToken cancellationToken = default)
        => PostWrappedAsync<object, JumpResult>(
            $"my/ships/{Uri.EscapeDataString(shipSymbol)}/jump",
            new { systemSymbol },
            AuthMode.AgentToken,
            cancellationToken);

    public Task<ChartResult> CreateChartAsync(string shipSymbol, CancellationToken cancellationToken = default)
        => PostWrappedAsync<object?, ChartResult>(
            $"my/ships/{Uri.EscapeDataString(shipSymbol)}/chart",
            null,
            AuthMode.AgentToken,
            cancellationToken);

    public Task<JumpGate> GetJumpGateAsync(string systemSymbol, string waypointSymbol, CancellationToken cancellationToken = default)
        => GetWrappedAsync<JumpGate>(
            $"systems/{Uri.EscapeDataString(systemSymbol)}/waypoints/{Uri.EscapeDataString(waypointSymbol)}/jump-gate",
            AuthMode.AgentToken,
            cancellationToken);

    public Task<RepairQuoteResult> GetRepairQuoteAsync(string shipSymbol, CancellationToken cancellationToken = default)
        => GetWrappedAsync<RepairQuoteResult>(
            $"my/ships/{Uri.EscapeDataString(shipSymbol)}/repair",
            AuthMode.AgentToken,
            cancellationToken);

    public Task<RepairResult> RepairShipAsync(string shipSymbol, CancellationToken cancellationToken = default)
        => PostWrappedAsync<object?, RepairResult>(
            $"my/ships/{Uri.EscapeDataString(shipSymbol)}/repair",
            null,
            AuthMode.AgentToken,
            cancellationToken);

    public Task<ScrapQuoteResult> GetScrapQuoteAsync(string shipSymbol, CancellationToken cancellationToken = default)
        => GetWrappedAsync<ScrapQuoteResult>(
            $"my/ships/{Uri.EscapeDataString(shipSymbol)}/scrap",
            AuthMode.AgentToken,
            cancellationToken);

    public Task<ScrapResult> ScrapShipAsync(string shipSymbol, CancellationToken cancellationToken = default)
        => PostWrappedAsync<object?, ScrapResult>(
            $"my/ships/{Uri.EscapeDataString(shipSymbol)}/scrap",
            null,
            AuthMode.AgentToken,
            cancellationToken);

    public Task<InstallMountResult> InstallMountAsync(string shipSymbol, string mountSymbol, CancellationToken cancellationToken = default)
        => PostWrappedAsync<object, InstallMountResult>(
            $"my/ships/{Uri.EscapeDataString(shipSymbol)}/mounts/install",
            new { symbol = mountSymbol },
            AuthMode.AgentToken,
            cancellationToken);

    public Task<RemoveMountResult> RemoveMountAsync(string shipSymbol, string mountSymbol, CancellationToken cancellationToken = default)
        => PostWrappedAsync<object, RemoveMountResult>(
            $"my/ships/{Uri.EscapeDataString(shipSymbol)}/mounts/remove",
            new { symbol = mountSymbol },
            AuthMode.AgentToken,
            cancellationToken);

    public Task<InstallModuleResult> InstallModuleAsync(string shipSymbol, string moduleSymbol, CancellationToken cancellationToken = default)
        => PostWrappedAsync<object, InstallModuleResult>(
            $"my/ships/{Uri.EscapeDataString(shipSymbol)}/modules/install",
            new { symbol = moduleSymbol },
            AuthMode.AgentToken,
            cancellationToken);

    public Task<RemoveModuleResult> RemoveModuleAsync(string shipSymbol, string moduleSymbol, CancellationToken cancellationToken = default)
        => PostWrappedAsync<object, RemoveModuleResult>(
            $"my/ships/{Uri.EscapeDataString(shipSymbol)}/modules/remove",
            new { symbol = moduleSymbol },
            AuthMode.AgentToken,
            cancellationToken);

    // Phase 10 additions
    public Task<ConstructionSite> GetConstructionSiteAsync(string systemSymbol, string waypointSymbol, CancellationToken cancellationToken = default)
        => GetWrappedAsync<ConstructionSite>(
            $"systems/{Uri.EscapeDataString(systemSymbol)}/waypoints/{Uri.EscapeDataString(waypointSymbol)}/construction",
            AuthMode.AgentToken,
            cancellationToken);

    public Task<SupplyConstructionData> SupplyConstructionAsync(string systemSymbol, string waypointSymbol, SupplyConstructionRequest request, CancellationToken cancellationToken = default)
        => PostWrappedAsync<SupplyConstructionRequest, SupplyConstructionData>(
            $"systems/{Uri.EscapeDataString(systemSymbol)}/waypoints/{Uri.EscapeDataString(waypointSymbol)}/construction/supply",
            request,
            AuthMode.AgentToken,
            cancellationToken);

    private async Task<T> GetWrappedAsync<T>(string endpoint, AuthMode authMode, CancellationToken cancellationToken)
    {
        var response = await GetAsync<ApiResponse<T>>(endpoint, authMode, cancellationToken);
        return response.Data;
    }

    private Task<T> GetAsync<T>(string endpoint, AuthMode authMode, CancellationToken cancellationToken)
        => SendAsync<T>(HttpMethod.Get, endpoint, authMode, body: null, cancellationToken);

    private async Task<TResponse> PostWrappedAsync<TRequest, TResponse>(string endpoint, TRequest request, AuthMode authMode, CancellationToken cancellationToken)
    {
        var response = await SendAsync<ApiResponse<TResponse>>(HttpMethod.Post, endpoint, authMode, request, cancellationToken);
        return response.Data;
    }

    private async Task<TResponse> PatchWrappedAsync<TRequest, TResponse>(string endpoint, TRequest request, AuthMode authMode, CancellationToken cancellationToken)
    {
        var response = await SendAsync<ApiResponse<TResponse>>(HttpMethod.Patch, endpoint, authMode, request, cancellationToken);
        return response.Data;
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string endpoint, AuthMode authMode, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, endpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var token = GetToken(authMode);
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var currentAgentToken = _agentTokenProvider.Token ?? _options.AgentToken;
        if (!string.IsNullOrWhiteSpace(currentAgentToken))
        {
            await _endpointUsageRecorder.RecordAsync(method.Method, endpoint, currentAgentToken, cancellationToken);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: SerializerOptions);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await SpaceTradersApiException.CreateAsync(response, endpoint, cancellationToken);
        }

        try
        {
            var result = await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken);
            return result ?? throw new SpaceTradersApiException(
                $"SpaceTraders API request to '{endpoint}' returned an empty response body.",
                response.StatusCode,
                endpoint,
                null);
        }
        catch (JsonException exception)
        {
            var responseBody = response.Content is null
                ? null
                : await response.Content.ReadAsStringAsync(cancellationToken);

            throw new SpaceTradersApiException(
                $"SpaceTraders API request to '{endpoint}' returned content that could not be deserialized.",
                response.StatusCode,
                endpoint,
                responseBody,
                innerException: exception);
        }
    }

    private string? GetToken(AuthMode authMode) => authMode switch
    {
        AuthMode.None => null,
        AuthMode.AgentToken => RequireToken(_agentTokenProvider.Token ?? _options.AgentToken, nameof(_options.AgentToken)),
        AuthMode.AccountToken => RequireToken(_options.AccountToken, nameof(_options.AccountToken)),
        _ => null
    };

    private static string RequireToken(string? token, string propertyName)
        => string.IsNullOrWhiteSpace(token)
            ? throw new InvalidOperationException($"SpaceTraders API option '{propertyName}' must be configured for this request.")
            : token;

    private enum AuthMode
    {
        None,
        AgentToken,
        AccountToken
    }
}
