using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.App;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.App.Pages.Market;

public sealed class IndexModel : PageModel
{
    private readonly SpaceTradersDbContext _dbContext;

    public IndexModel(SpaceTradersDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public string MarketWaypoint { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string TradeSymbol { get; set; } = string.Empty;

    public IReadOnlyList<TradeOpportunity> TopRoutes { get; private set; } = [];

    public IReadOnlyList<CachedMarket> Markets { get; private set; } = [];

    public CachedMarket? SelectedMarket { get; private set; }

    public IReadOnlyList<MarketTradeGoodViewModel> SelectedMarketGoods { get; private set; } = [];

    public IReadOnlyList<TradeOpportunity> SelectedSymbolRoutes { get; private set; } = [];

    public IReadOnlyList<string> ProductionChainOutputs { get; private set; } = [];

    public static string GetProfitPerUnitClass(TradeOpportunity route)
    {
        if (route.ProfitPerUnit >= 500)
        {
            return "text-success";
        }

        return route.ProfitPerUnit >= 200 ? string.Empty : "text-danger";
    }

    public static string GetRouteScoreClass(TradeOpportunity route)
    {
        if (route.RouteScore >= 400)
        {
            return "text-success";
        }

        return route.RouteScore >= 200 ? string.Empty : "text-danger";
    }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var selectedToken = await AgentViewSelection.ResolveSelectedTokenAsync(_dbContext, HttpContext, cancellationToken);

        TopRoutes = await _dbContext.TradeOpportunities
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => t.AgentToken == selectedToken)
            .OrderByDescending(t => t.RouteScore)
            .ThenByDescending(t => t.SupportsSupplyChain)
            .ThenByDescending(t => t.SupplyChainDepth)
            .ThenByDescending(t => t.ProfitPerJump)
            .Take(20)
            .ToListAsync(cancellationToken);

        Markets = await _dbContext.Markets
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(m => m.AgentToken == selectedToken)
            .OrderBy(m => m.SystemSymbol)
            .ThenBy(m => m.WaypointSymbol)
            .ToListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(MarketWaypoint))
        {
            SelectedMarket = Markets.FirstOrDefault(m =>
                m.WaypointSymbol.Equals(MarketWaypoint, StringComparison.OrdinalIgnoreCase));
        }

        if (SelectedMarket is not null)
        {
            SelectedMarketGoods = ParseTradeGoods(SelectedMarket.TradeGoodsJson)
                .OrderByDescending(g => g.SellPrice - g.PurchasePrice)
                .ThenBy(g => g.Symbol)
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(TradeSymbol))
        {
            SelectedSymbolRoutes = await _dbContext.TradeOpportunities
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(t => t.AgentToken == selectedToken)
                .Where(t => t.TradeSymbol == TradeSymbol)
                .OrderByDescending(t => t.RouteScore)
                .ThenByDescending(t => t.ProfitPerJump)
                .Take(20)
                .ToListAsync(cancellationToken);

            ProductionChainOutputs = await BuildProductionChainOutputsAsync(selectedToken, TradeSymbol, cancellationToken);
        }
    }

    private async Task<IReadOnlyList<string>> BuildProductionChainOutputsAsync(
        string selectedToken,
        string symbol,
        CancellationToken cancellationToken)
    {
        var normalized = symbol.Trim().ToUpperInvariant();
        var markets = await _dbContext.Markets
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(m => m.AgentToken == selectedToken)
            .ToListAsync(cancellationToken);

        var outputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var market in markets)
        {
            var inputs = ParseTradeGoodSymbols(market.ImportsJson);
            if (!inputs.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var output in ParseTradeGoodSymbols(market.ExportsJson))
            {
                outputs.Add(output);
            }

            foreach (var output in ParseTradeGoodSymbols(market.ExchangeJson))
            {
                outputs.Add(output);
            }

            foreach (var output in ParseTradeGoods(market.TradeGoodsJson)
                .Where(g => g.Type.Equals("EXPORT", StringComparison.OrdinalIgnoreCase)
                            || g.Type.Equals("EXCHANGE", StringComparison.OrdinalIgnoreCase))
                .Select(g => g.Symbol))
            {
                outputs.Add(output);
            }
        }

        return outputs.OrderBy(o => o, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IReadOnlyList<MarketTradeGoodViewModel> ParseTradeGoods(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var goods = JsonSerializer.Deserialize<List<MarketTradeGoodJson>>(json);
            if (goods is null)
            {
                return [];
            }

            return goods
                .Where(g => !string.IsNullOrWhiteSpace(g.Symbol))
                .Select(g => new MarketTradeGoodViewModel(
                    g.Symbol?.Trim().ToUpperInvariant() ?? string.Empty,
                    g.Type?.Trim().ToUpperInvariant() ?? "UNKNOWN",
                    g.PurchasePrice,
                    g.SellPrice,
                    g.TradeVolume,
                    g.Supply?.Trim().ToUpperInvariant() ?? "",
                    g.Activity?.Trim().ToUpperInvariant() ?? ""))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<string> ParseTradeGoodSymbols(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<TradeGoodSymbolJson>>(json)
                ?.Select(x => x.Symbol?.Trim().ToUpperInvariant())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Cast<string>()
                .ToList()
                ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public sealed record MarketTradeGoodViewModel(
        string Symbol,
        string Type,
        int PurchasePrice,
        int SellPrice,
        int TradeVolume,
        string Supply,
        string Activity);

    private sealed class MarketTradeGoodJson
    {
        public string? Symbol { get; init; }
        public string? Type { get; init; }
        public int PurchasePrice { get; init; }
        public int SellPrice { get; init; }
        public int TradeVolume { get; init; }
        public string? Supply { get; init; }
        public string? Activity { get; init; }
    }

    private sealed class TradeGoodSymbolJson
    {
        public string? Symbol { get; init; }
    }
}
