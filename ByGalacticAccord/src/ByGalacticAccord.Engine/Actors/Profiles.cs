using System.Collections.Generic;
using ByGalacticAccord.Engine.Domain;

namespace ByGalacticAccord.Engine.Actors;

/// <summary>
/// What a producer makes (§4.2.3). Output accrues to stock each heartbeat at a production cost
/// (a credit sink). Asking price falls as stock rises — oversupply depresses value (§4.2.2).
/// </summary>
public sealed record ProducerProfile(
    GoodType Good,
    decimal OutputPerHeartbeat,
    Credits UnitProductionCost,
    decimal StockCap,
    decimal Margin)
{
    /// <summary>How full the warehouse is, 0..1.</summary>
    public decimal Fullness(decimal currentStock) => StockCap <= 0m ? 1m : System.Math.Clamp(currentStock / StockCap, 0m, 1m);
}

/// <summary>
/// A station operator's terms (design §8.1): the storage rate per unit per day, and the float it keeps; rent
/// above the float goes on station upkeep (a sink, design §5.1).
/// </summary>
public sealed record StationProfile(Credits StorageRatePerUnitDay, Credits Float);

/// <summary>One good a consumer wants, with how fast it burns through it and the buffer it tries to keep.</summary>
public sealed record ConsumerDemand(
    GoodType Good,
    decimal ConsumptionPerHeartbeat,
    decimal TargetStock);

/// <summary>One input a recipe uses for each part it makes.</summary>
public sealed record RecipeInput(GoodType Good, decimal Quantity);

/// <summary>
/// How a factory makes one kind of part (design §15.4): what it uses of each input, how many hours a part takes, and how
/// many it keeps in stock at most.
/// </summary>
public sealed record PartRecipe(PartKind Kind, IReadOnlyList<RecipeInput> Inputs, long Hours, decimal StockCap);

/// <summary>
/// A part factory (design §4.1, §5.3, §15.4): it turns ordinary goods into ship parts, one part at a time. Its output grade
/// is its <see cref="Quality"/>, a grade lower for each input it makes do without. It prices a part from the part's base
/// price (design §15.4) at its grade, cheaper the fuller its stock: <see cref="UnitCost"/> is what a part costs it, inputs at
/// what they usually cost delivered and its labour (<see cref="Labour"/>, a sink), and its ask that with its margin.
/// </summary>
public sealed record FactoryProfile(Grade Quality, IReadOnlyList<PartRecipe> Recipes, decimal Margin)
{
    /// <summary>What the inputs of a part are usually bought for delivered, as a multiple of their base value.</summary>
    public const decimal DeliveredInputMarkup = 1.3m;

    /// <summary>The recipe for a kind of part, if it makes it.</summary>
    public PartRecipe? RecipeFor(PartKind kind)
    {
        foreach (PartRecipe recipe in Recipes)
        {
            if (recipe.Kind == kind)
            {
                return recipe;
            }
        }

        return null;
    }

    /// <summary>Whether it makes a good: any grade of a kind it has a recipe for.</summary>
    public bool Makes(GoodType good) => ShipPart.IsPart(good) && RecipeFor(ShipPart.Of(good).Kind) is not null;

    /// <summary>Every good it makes: each kind it has a recipe for, at grade A, B and C, in recipe order.</summary>
    public IReadOnlyList<GoodType> Products
    {
        get
        {
            var goods = new List<GoodType>();
            foreach (PartRecipe recipe in Recipes)
            {
                foreach (Grade grade in System.Enum.GetValues<Grade>())
                {
                    goods.Add(ShipPart.GoodOf(recipe.Kind, grade));
                }
            }

            return goods;
        }
    }

    /// <summary>The ordinary goods it uses, each once, in recipe order.</summary>
    public IReadOnlyList<GoodType> Inputs
    {
        get
        {
            var goods = new List<GoodType>();
            foreach (PartRecipe recipe in Recipes)
            {
                foreach (RecipeInput input in recipe.Inputs)
                {
                    if (!goods.Contains(input.Good))
                    {
                        goods.Add(input.Good);
                    }
                }
            }

            return goods;
        }
    }

    /// <summary>How much of an input it keeps in hand: enough for two of the part that uses most of it.</summary>
    public decimal InputTarget(GoodType input)
    {
        decimal most = 0m;
        foreach (PartRecipe recipe in Recipes)
        {
            foreach (RecipeInput i in recipe.Inputs)
            {
                if (i.Good == input && i.Quantity > most)
                {
                    most = i.Quantity;
                }
            }
        }

        return most * 2m;
    }

    /// <summary>What a part of this kind and grade costs it: its base price at the grade's share, less its margin and a little room.</summary>
    public Credits UnitCost(PartKind kind, Grade grade) =>
        Credits.Of(System.Math.Round(ShipPart.Info(kind).BasePrice.Amount * ShipPart.ValueShare(grade) / ((1m + Margin) * 1.05m), 2));

    /// <summary>Its labour on a part (a sink, design §5.1): the part's cost less its inputs at their usual delivered price, never under a tenth of the cost.</summary>
    public Credits Labour(PartKind kind, Grade grade)
    {
        decimal cost = UnitCost(kind, grade).Amount;
        decimal inputs = 0m;
        if (RecipeFor(kind) is { } recipe)
        {
            foreach (RecipeInput input in recipe.Inputs)
            {
                inputs += input.Quantity * Goods.Info(input.Good).BaseValue * DeliveredInputMarkup;
            }
        }

        return Credits.Of(System.Math.Round(System.Math.Max(cost * 0.1m, cost - inputs), 2));
    }

    /// <summary>A producer's profile for one good it makes, so the producer's pricing and selling apply to its parts.</summary>
    public ProducerProfile ProfileFor(GoodType good)
    {
        (PartKind kind, Grade grade) = ShipPart.Of(good);
        PartRecipe recipe = RecipeFor(kind) ?? throw new System.ArgumentException($"It doesn't make {good}.", nameof(good));
        return new ProducerProfile(good, 1m, UnitCost(kind, grade), recipe.StockCap, Margin);
    }
}

/// <summary>What a factory is making, and when it will be done.</summary>
public sealed record FactoryWork(PartKind Kind, Grade Grade, long Finishes);

/// <summary>
/// A shipyard (design §4.1, §15.5): how long it takes to assemble a ship once it has the parts, its labour on one (a sink), its
/// margin, the term package it bids on by habit, how many unsold ships it keeps on its lot before it stops building on spec,
/// the standard designs it builds on spec in turn, and how many signed builds it queues behind the one it is building
/// (design D35: one ship at a time).
/// </summary>
public sealed record YardProfile(
    long AssemblyHours,
    Credits Labour,
    decimal Margin,
    TermPackage Habit,
    int LotLimit,
    IReadOnlyList<Design> SpecDesigns,
    int QueueLimit = 2);

/// <summary>
/// A salvage yard (design §15.5, Edge Salvage): it doesn't build. It buys ships others sell, cheap (<see cref="BuyShare"/> of
/// their value at most), brings them home, and sells them second-hand from its lot at <see cref="AskShare"/> of their value,
/// down to <see cref="FloorShare"/> as they sit; it keeps at most <see cref="LotLimit"/>.
/// </summary>
public sealed record SalvageProfile(decimal BuyShare, decimal AskShare, decimal FloorShare, int LotLimit);

/// <summary>One ship a yard is building (design §6.3): for a Build, or on spec for its lot.</summary>
public sealed class YardJob
{
    public YardJob(ContractId? build, ShipParts wanted, long started)
    {
        Build = build;
        Wanted = wanted;
        Started = started;
    }

    /// <summary>The Build it is for; null for a ship on spec (or one whose Build ended some other way).</summary>
    public ContractId? Build { get; internal set; }

    /// <summary>The parts it wants: the Build's spec, or a standard design's.</summary>
    public ShipParts Wanted { get; internal set; }

    public long Started { get; }

    /// <summary>When assembly ends, once every part is in and it has started; null while it is still getting parts.</summary>
    public long? AssemblyDone { get; internal set; }

    /// <summary>The parts it is assembling with.</summary>
    public ShipParts? Fitting { get; internal set; }

    /// <summary>The wanted Sale it posted for each part not yet in, by slot.</summary>
    public Dictionary<PartSlot, ContractId> Orders { get; } = new();

    /// <summary>How many of its orders for each slot failed upstream.</summary>
    public Dictionary<PartSlot, int> Failures { get; } = new();
}

/// <summary>
/// What a yard is doing: the ship it is building (the only one it assembles, design D35), the signed builds waiting behind it,
/// whose parts it orders meanwhile, and the next design it builds on spec.
/// </summary>
public sealed class YardState
{
    public YardJob? Current { get; internal set; }

    /// <summary>Signed builds waiting behind the current job, in the order they were signed, each getting its parts in.</summary>
    public List<YardJob> Queue { get; } = new();

    public int NextSpec { get; internal set; }

    /// <summary>
    /// Whether its last ship built on spec sold within <c>DecisionEngine.SpecSellsWithinHours</c>: its sign that stock ships sell. Null
    /// before it has sold one; a yard whose last stock ship sat builds no more on spec until someone orders one.
    /// </summary>
    public bool? SpecSells { get; internal set; }

    /// <summary>Whether a Build is the yard's: the job it is building, or queued behind it.</summary>
    public bool Has(ContractId build) => Current?.Build == build || Queue.Exists(j => j.Build == build);

    /// <summary>The jobs in order: the current one first.</summary>
    public IEnumerable<YardJob> Jobs
    {
        get
        {
            if (Current is not null)
            {
                yield return Current;
            }

            foreach (YardJob job in Queue)
            {
                yield return job;
            }
        }
    }
}
