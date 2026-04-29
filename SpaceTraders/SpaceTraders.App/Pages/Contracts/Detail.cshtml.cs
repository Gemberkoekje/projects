using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.App;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.App.Pages.Contracts;

public sealed class DetailModel : PageModel
{
    private readonly SpaceTradersDbContext _dbContext;

    public DetailModel(SpaceTradersDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public CachedContract Contract { get; private set; } = new() { Id = string.Empty, FactionSymbol = string.Empty, Type = string.Empty };

    public IReadOnlyList<ContractDeliverableViewModel> Deliverables { get; private set; } = [];

    public IReadOnlyList<ShipAssignmentRecord> AssignedShips { get; private set; } = [];

    public bool NotFound { get; private set; }

    public async Task<IActionResult> OnGetAsync(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return RedirectToPage("/Contracts/Index");
        }

        var selectedToken = await AgentViewSelection.ResolveSelectedTokenAsync(_dbContext, HttpContext, cancellationToken);

        var contract = await _dbContext.Contracts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.AgentToken == selectedToken && c.Id == id, cancellationToken);

        if (contract is null)
        {
            NotFound = true;
            return Page();
        }

        Contract = contract;
        Deliverables = ParseDeliverables(contract.DeliverablesJson);

        AssignedShips = await _dbContext.ShipAssignments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(a => a.AgentToken == selectedToken && a.ContractId == id && a.CompletedAt == null)
            .OrderBy(a => a.ShipSymbol)
            .ToListAsync(cancellationToken);

        return Page();
    }

    private static IReadOnlyList<ContractDeliverableViewModel> ParseDeliverables(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var elements = JsonSerializer.Deserialize<List<ContractDeliverableJson>>(json) ?? [];
            return elements
                .Where(d => !string.IsNullOrWhiteSpace(d.TradeSymbol) && !string.IsNullOrWhiteSpace(d.DestinationSymbol))
                .Select(d => new ContractDeliverableViewModel(
                    d.TradeSymbol.Trim().ToUpperInvariant(),
                    d.DestinationSymbol.Trim().ToUpperInvariant(),
                    d.UnitsRequired,
                    d.UnitsFulfilled))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public sealed record ContractDeliverableViewModel(string TradeSymbol, string DestinationSymbol, int UnitsRequired, int UnitsFulfilled)
    {
        public int RemainingUnits => Math.Max(0, UnitsRequired - UnitsFulfilled);

        public int CompletionPercent => UnitsRequired > 0 ? (int)Math.Round((double)UnitsFulfilled / UnitsRequired * 100) : 0;
    }

    private sealed class ContractDeliverableJson
    {
        public string TradeSymbol { get; init; } = string.Empty;

        public string DestinationSymbol { get; init; } = string.Empty;

        public int UnitsRequired { get; init; }

        public int UnitsFulfilled { get; init; }
    }
}
