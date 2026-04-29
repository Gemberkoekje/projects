using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace SpaceTraders.App.Services;

public sealed class ShipCommandClient(HttpClient httpClient, IOptions<InternalApiOptions> options) : IShipCommandClient
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly InternalApiOptions _options = options.Value;

    public async Task EnqueueDockAsync(string shipSymbol, CancellationToken cancellationToken = default)
    {
        await PostAsync($"control/ships/{Uri.EscapeDataString(shipSymbol)}/dock", false, new StringContent(string.Empty), cancellationToken);
    }

    public async Task EnqueueOrbitAsync(string shipSymbol, CancellationToken cancellationToken = default)
    {
        await PostAsync($"control/ships/{Uri.EscapeDataString(shipSymbol)}/orbit", false, new StringContent(string.Empty), cancellationToken);
    }

    public async Task EnqueueRefuelAsync(string shipSymbol, bool fromCargo, CancellationToken cancellationToken = default)
    {
        await PostAsync(
            $"control/ships/{Uri.EscapeDataString(shipSymbol)}/refuel",
            true,
            JsonContent.Create(new { FromCargo = fromCargo }),
            cancellationToken);
    }

    public async Task EnqueueNavigateAsync(string shipSymbol, string destinationWaypoint, CancellationToken cancellationToken = default)
    {
        await PostAsync(
            $"control/ships/{Uri.EscapeDataString(shipSymbol)}/navigate",
            true,
            JsonContent.Create(new { DestinationWaypoint = destinationWaypoint }),
            cancellationToken);
    }

    public async Task EnqueueSetFlightModeAsync(string shipSymbol, string flightMode, CancellationToken cancellationToken = default)
    {
        await PostAsync(
            $"control/ships/{Uri.EscapeDataString(shipSymbol)}/flight-mode",
            true,
            JsonContent.Create(new { FlightMode = flightMode }),
            cancellationToken);
    }

    public async Task EnqueueRepairAsync(string shipSymbol, CancellationToken cancellationToken = default)
    {
        await PostAsync($"control/ships/{Uri.EscapeDataString(shipSymbol)}/repair", false, new StringContent(string.Empty), cancellationToken);
    }

    public async Task EnqueueScrapAsync(string shipSymbol, CancellationToken cancellationToken = default)
    {
        await PostAsync($"control/ships/{Uri.EscapeDataString(shipSymbol)}/scrap", false, new StringContent(string.Empty), cancellationToken);
    }

    public async Task EnqueueInstallMountAsync(string shipSymbol, string mountSymbol, CancellationToken cancellationToken = default)
    {
        await PostAsync(
            $"control/ships/{Uri.EscapeDataString(shipSymbol)}/mounts/install",
            true,
            JsonContent.Create(new { Symbol = mountSymbol }),
            cancellationToken);
    }

    public async Task EnqueueRemoveMountAsync(string shipSymbol, string mountSymbol, CancellationToken cancellationToken = default)
    {
        await PostAsync(
            $"control/ships/{Uri.EscapeDataString(shipSymbol)}/mounts/remove",
            true,
            JsonContent.Create(new { Symbol = mountSymbol }),
            cancellationToken);
    }

    public async Task EnqueueInstallModuleAsync(string shipSymbol, string moduleSymbol, CancellationToken cancellationToken = default)
    {
        await PostAsync(
            $"control/ships/{Uri.EscapeDataString(shipSymbol)}/modules/install",
            true,
            JsonContent.Create(new { Symbol = moduleSymbol }),
            cancellationToken);
    }

    public async Task EnqueueRemoveModuleAsync(string shipSymbol, string moduleSymbol, CancellationToken cancellationToken = default)
    {
        await PostAsync(
            $"control/ships/{Uri.EscapeDataString(shipSymbol)}/modules/remove",
            true,
            JsonContent.Create(new { Symbol = moduleSymbol }),
            cancellationToken);
    }

    private async Task PostAsync(string relativePath, bool hasBody, HttpContent content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, relativePath);

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            request.Headers.Add("X-Api-Key", _options.ApiKey);
        }

        if (hasBody)
        {
            request.Content = content;
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"Internal API command call failed ({(int)response.StatusCode}): {payload}");
    }
}
