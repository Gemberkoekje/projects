using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.App;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.App.Pages.Fleet;

public sealed class IndexModel : PageModel
{
    private readonly SpaceTradersDbContext _dbContext;

    public IndexModel(SpaceTradersDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public string RoleFilter { get; set; } = "ALL";

    [BindProperty(SupportsGet = true)]
    public string StateFilter { get; set; } = "ALL";

    [BindProperty(SupportsGet = true)]
    public string CargoFilter { get; set; } = "ALL";

    [BindProperty(SupportsGet = true)]
    public string FuelFilter { get; set; } = "ALL";

    [BindProperty(SupportsGet = true)]
    public string CooldownFilter { get; set; } = "ALL";

    [BindProperty(SupportsGet = true)]
    public string AssignmentFilter { get; set; } = "ALL";

    public IReadOnlyList<FleetShipViewModel> Ships { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var selectedToken = await AgentViewSelection.ResolveSelectedTokenAsync(_dbContext, HttpContext, cancellationToken);

        var ships = await _dbContext.Ships
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(ship => ship.AgentToken == selectedToken)
            .OrderBy(ship => ship.Symbol)
            .ToListAsync(cancellationToken);

        var assignments = await _dbContext.ShipAssignments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(a => a.AgentToken == selectedToken && a.CompletedAt == null)
            .ToDictionaryAsync(a => a.ShipSymbol, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var now = TimeProvider.System.GetUtcNow();

        var rows = ships
            .Select(ship =>
            {
                var hasAssignment = assignments.TryGetValue(ship.Symbol, out var assignment);
                var role = hasAssignment ? assignment?.Type ?? "UNASSIGNED" : "UNASSIGNED";
                var assignmentSummary = hasAssignment
                    ? BuildAssignmentSummary(assignment)
                    : "Unassigned";

                var cargoPercent = ship.CargoCapacity > 0
                    ? (int)Math.Round((double)ship.CargoCurrent / ship.CargoCapacity * 100)
                    : 0;

                var fuelPercent = ship.FuelCapacity > 0
                    ? (int)Math.Round((double)ship.FuelCurrent / ship.FuelCapacity * 100)
                    : 0;

                var isCoolingDown = ship.CooldownExpiresAt.HasValue && ship.CooldownExpiresAt.Value > now;

                return new FleetShipViewModel(
                    ship.Symbol,
                    role,
                    ship.Status ?? "UNKNOWN",
                    ship.FlightMode ?? "UNKNOWN",
                    ship.SystemSymbol ?? string.Empty,
                    ship.WaypointSymbol ?? string.Empty,
                    ship.CargoCurrent,
                    ship.CargoCapacity,
                    cargoPercent,
                    ship.FuelCurrent,
                    ship.FuelCapacity,
                    fuelPercent,
                    isCoolingDown,
                    ship.CooldownExpiresAt,
                    assignmentSummary,
                    hasAssignment,
                    ship.ArrivesAt,
                    ship.LastSyncedAt);
            })
            .Where(ApplyFilters)
            .ToList();

        Ships = rows;
    }

    private bool ApplyFilters(FleetShipViewModel ship)
    {
        if (!RoleFilter.Equals("ALL", StringComparison.OrdinalIgnoreCase) &&
            !ship.Role.Equals(RoleFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!StateFilter.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            if (StateFilter.Equals("IN_TRANSIT", StringComparison.OrdinalIgnoreCase) && !ship.IsInTransit)
            {
                return false;
            }

            if (!StateFilter.Equals("IN_TRANSIT", StringComparison.OrdinalIgnoreCase) &&
                !ship.Status.Equals(StateFilter, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (!CargoFilter.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            if (CargoFilter.Equals("EMPTY", StringComparison.OrdinalIgnoreCase) && ship.CargoCurrent != 0)
            {
                return false;
            }

            if (CargoFilter.Equals("LOADED", StringComparison.OrdinalIgnoreCase) && ship.CargoCurrent == 0)
            {
                return false;
            }

            if (CargoFilter.Equals("FULL", StringComparison.OrdinalIgnoreCase) && ship.CargoCurrent < ship.CargoCapacity)
            {
                return false;
            }
        }

        if (!FuelFilter.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            if (FuelFilter.Equals("LOW", StringComparison.OrdinalIgnoreCase) && ship.FuelPercent >= 25)
            {
                return false;
            }

            if (FuelFilter.Equals("MEDIUM", StringComparison.OrdinalIgnoreCase) && (ship.FuelPercent < 25 || ship.FuelPercent > 75))
            {
                return false;
            }

            if (FuelFilter.Equals("HIGH", StringComparison.OrdinalIgnoreCase) && ship.FuelPercent <= 75)
            {
                return false;
            }
        }

        if (!CooldownFilter.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            if (CooldownFilter.Equals("READY", StringComparison.OrdinalIgnoreCase) && ship.IsCoolingDown)
            {
                return false;
            }

            if (CooldownFilter.Equals("COOLING", StringComparison.OrdinalIgnoreCase) && !ship.IsCoolingDown)
            {
                return false;
            }
        }

        if (!AssignmentFilter.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            if (AssignmentFilter.Equals("ASSIGNED", StringComparison.OrdinalIgnoreCase) && !ship.HasAssignment)
            {
                return false;
            }

            if (AssignmentFilter.Equals("UNASSIGNED", StringComparison.OrdinalIgnoreCase) && ship.HasAssignment)
            {
                return false;
            }
        }

        return true;
    }

    private static string BuildAssignmentSummary(ShipAssignmentRecord assignment)
    {
        var destination = string.IsNullOrWhiteSpace(assignment.DestWaypoint) ? "" : $" -> {assignment.DestWaypoint}";
        var cargo = string.IsNullOrWhiteSpace(assignment.CargoSymbol) ? "" : $" [{assignment.CargoSymbol}]";
        return $"{assignment.Type}{destination}{cargo}";
    }

    public static string FuelClass(FleetShipViewModel ship)
        => ship.FuelPercent < 25 ? "text-danger" : ship.FuelPercent > 75 ? "text-success" : "text-muted";

    public static string CargoClass(FleetShipViewModel ship)
        => ship.CargoPercent >= 90 ? "text-danger" : ship.CargoPercent == 0 ? "text-muted" : "text-primary";

    public sealed record FleetShipViewModel(
        string Symbol,
        string Role,
        string Status,
        string FlightMode,
        string SystemSymbol,
        string WaypointSymbol,
        int CargoCurrent,
        int CargoCapacity,
        int CargoPercent,
        int FuelCurrent,
        int FuelCapacity,
        int FuelPercent,
        bool IsCoolingDown,
        DateTimeOffset? CooldownExpiresAt,
        string Assignment,
        bool HasAssignment,
        DateTimeOffset? ArrivesAt,
        DateTimeOffset LastSyncedAt)
    {
        public bool IsInTransit => ArrivesAt.HasValue && ArrivesAt.Value > TimeProvider.System.GetUtcNow();
    }
}
