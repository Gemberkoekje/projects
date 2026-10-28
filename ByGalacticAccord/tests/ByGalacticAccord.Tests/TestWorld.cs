using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Tests;

/// <summary>Shared helpers for building and running a headless world in tests.</summary>
internal static class TestWorld
{
    /// <summary>
    /// Trait for soak-length tests (design §20.3). They are excluded by default (see the test project's
    /// VSTestTestCaseFilter); run them with <c>dotnet test --filter Category=Soak</c>.
    /// </summary>
    public const string Category = "Category";

    public const string Soak = "Soak";

    /// <summary>The horizon of a fast world run: 20,000 game hours, about 833 game days.</summary>
    public const long FastTicks = 20_000;

    public static SimulationContext RunHeadless(long seed, long ticks, SimulationConfig? config = null)
    {
        SimulationContext context = WorldGenerator.Generate(seed, withPlayer: false, config).Context;
        context.RunTo(ticks);
        return context;
    }

    /// <summary>
    /// The sector with nobody acting on their own: every heartbeat is pushed far beyond any test's horizon, so a test
    /// drives the market itself and only the events it causes run. The world, its stocks and its money are as generated;
    /// transit incidents are off unless asked for, so a trip takes exactly its hours; and the player's posts draw no
    /// answers unless asked for (<paramref name="answers"/>).
    /// </summary>
    public static SimulationContext Quiet(bool withPlayer = false, long seed = 42L, bool incidents = false, bool answers = false) =>
        WorldGenerator.Generate(
            seed, withPlayer,
            new SimulationConfig { HeartbeatInterval = 1_000_000_000, IncidentProbabilityScale = incidents ? 0.6m : 0m, PostsDrawAnswers = answers }).Context;

    /// <summary>A compact fingerprint of world state, used to prove deterministic replay (design §20.1).</summary>
    public static string Signature(SimulationContext context) => StateFingerprint.Of(context);

    public static ActorState Actor(this SimulationContext context, string name) => context.Actors.Values.Single(a => a.Name == name);

    public static Location Place(this SimulationContext context, string name) => context.Locations.Values.Single(l => l.Name == name);

    public static ActorState Player(this SimulationContext context) => context.Actor(context.PlayerActorId!.Value);

    public static Ship ShipOf(this SimulationContext context, ActorState owner) => context.Ship(owner.Ships[0]);

    /// <summary>
    /// Give an actor a presence at a place (design §7.1), so it sees the contracts there live: a unit of ore in storage there,
    /// taken from the ore producer's warehouse. For tests of what the player does with contracts it would otherwise know of only
    /// from the news.
    /// </summary>
    public static void PresentAt(this SimulationContext context, ActorState actor, string place) =>
        context.PutInStorage(actor, context.Place(place).Id, context.TakeFromStock(context.Actor("Rust Belt Extraction"), GoodType.Ore, 1m, reserved: false));

    /// <summary>
    /// Its poster pays to show a post one jump further out, in every system there (as an AI does for a post that sits unsold,
    /// design §7.1, D38), and the clock runs until it is shown there.
    /// </summary>
    public static void ShowAJumpFurther(this SimulationContext context, Contract post)
    {
        int next = Advertising.Options(context, post)[0].Jumps;
        var ring = Advertising.Options(context, post).Where(o => o.Jumps == next).Select(o => o.At).ToList();
        Assert.True(Advertising.Show(context, context.Actor(post.PostedBy), post, ring), $"{post.Id} couldn't be shown further.");
        context.RunTo(context.CurrentTick + Advertising.DelayHours(context, next));
    }

    /// <summary>Run until a contract is settled one way or another (or the horizon passes).</summary>
    public static void RunUntilSettled(this SimulationContext context, Contract contract, long horizon = 2_000)
    {
        long end = context.CurrentTick + horizon;
        while (contract.IsLive && context.CurrentTick < end)
        {
            context.RunTo(context.CurrentTick + 1);
        }
    }

    /// <summary>Post a delivered wanted Sale as a consumer would: at its home, for its stock, on standard terms unless asked.</summary>
    public static SaleContract PostWanted(
        this SimulationContext context, ActorState buyer, GoodType good, decimal quantity, decimal price, long hours = 400, TermPackage package = TermPackage.Standard) =>
        Market.PostSale(
            context, buyer, SaleSide.Wanted, good, quantity, buyer.Home, Credits.Of(price), Credits.Of(price * 1.5m),
            context.CurrentTick + hours, Holding.StockAt(buyer.Home), package);

    /// <summary>The goods conservation and every other invariant hold.</summary>
    public static void AssertSound(this SimulationContext context)
    {
        var violations = Invariants.Check(context);
        Assert.True(violations.Count == 0, string.Join("; ", violations.Select(v => $"[{v.Name}] {v.Detail}")));
    }
}
