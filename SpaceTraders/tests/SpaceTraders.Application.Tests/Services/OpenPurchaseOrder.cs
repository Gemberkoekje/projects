using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Services;

namespace SpaceTraders.Application.Tests.Services;

/// <summary>
/// For the plans' tests: a purchase order (slice 6.10b, D43) that keeps what each plan said it needs, and lets it buy
/// unless <see cref="Allows"/> says otherwise.
/// </summary>
internal sealed class OpenPurchaseOrder : IPurchaseOrder
{
    /// <summary>What each plan said it needs, as it last said it.</summary>
    public Dictionary<AutomationPlan, PurchaseNeed> Needs { get; } = [];

    /// <summary>Whether a plan that needs something may buy it: false when something comes first.</summary>
    public bool Allows { get; set; } = true;

    /// <inheritdoc />
    public Task<bool> ReportAsync(AutomationPlan plan, PurchaseNeed need, CancellationToken cancellationToken)
    {
        Needs[plan] = need;
        return Task.FromResult(Allows && need.Tier != PurchaseTier.None);
    }

    /// <summary>What the plan said it needs; <see cref="PurchaseNeed.None"/> when it said nothing.</summary>
    /// <param name="plan">The plan.</param>
    /// <returns>Its need.</returns>
    public PurchaseNeed Of(AutomationPlan plan) => Needs.GetValueOrDefault(plan, PurchaseNeed.None);
}
