using System;
using System.Collections.Generic;
using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Presentation.Session;

/// <summary>The time bar's speeds (design §8.3): game hours per real second.</summary>
public enum ClockSpeed
{
    Paused = 0,
    X1 = 1,
    X4 = 4,
    X16 = 16,
}

/// <summary>What happened when the player tried to take a contract: blocked before signing, or the signing's result.</summary>
public sealed record DealOutcome(DealBlock Block, Signing? Signing)
{
    public bool Signed => Signing == Engine.Simulation.Signing.Signed;
}

/// <summary>
/// One game in progress: the world, the player's actor, and the clock. The clock runs in real time at the
/// chosen speed (1× is one game hour per second) and advances the simulation to whole ticks; the fraction
/// in between is only for drawing ships along their courses. It stops itself when the engine raises a
/// player alert (the hard pause, design §8.3). Player commands go through <see cref="PlayerActions"/>, the
/// same engine path as an AI decision.
/// </summary>
public sealed class GameSession
{
    /// <summary>The longest stretch of real time one frame may advance, so a hidden tab doesn't leap days on return.</summary>
    public const double MaxFrameSeconds = 0.25;

    private double _clock;

    public GameSession(long seed, long startTick = 0)
    {
        WorldHandles handles = WorldGenerator.Generate(seed, withPlayer: true);
        Seed = seed;
        Context = handles.Context;
        Player = Context.Actor(handles.Player!.Value);
        Context.RunTo(startTick);
        _clock = Context.CurrentTick;
    }

    public long Seed { get; }

    public SimulationContext Context { get; }

    public ActorState Player { get; }

    public ClockSpeed Speed { get; private set; } = ClockSpeed.Paused;

    /// <summary>Game time with its fraction: at least the current tick, less than the next.</summary>
    public double Clock => _clock;

    public long Now => Context.CurrentTick;

    /// <summary>Bumped whenever what the panels show may have changed: a tick passed, or the player acted.</summary>
    public long Revision { get; private set; }

    /// <summary>The engine's alerts for the player, oldest first. While any is pending the clock stays stopped.</summary>
    public IReadOnlyList<JournalEntry> Alerts => Context.PlayerAlerts;

    /// <summary>The ship the player flies. Slice 1 starts with one.</summary>
    public Ship PlayerShip => Context.Ship(Player.Ships[0]);

    public void SetSpeed(ClockSpeed speed)
    {
        if (speed != ClockSpeed.Paused && Context.HasPlayerAlerts)
        {
            // Resuming is how the player acknowledges what stopped the clock.
            Context.ClearPlayerAlerts();
        }

        Speed = speed;
        Revision++;
    }

    /// <summary>Advance by a frame's worth of real time. Returns true if the simulation moved to a later tick.</summary>
    public bool Advance(double realSeconds)
    {
        if (Speed == ClockSpeed.Paused || realSeconds <= 0)
        {
            return false;
        }

        _clock += Math.Min(realSeconds, MaxFrameSeconds) * (int)Speed;
        long target = (long)Math.Floor(_clock);
        if (target <= Context.CurrentTick)
        {
            return false;
        }

        RunStop stop = Context.RunTo(target);
        if (stop == RunStop.PlayerAlert)
        {
            Speed = ClockSpeed.Paused;
            _clock = Context.CurrentTick;
        }

        Revision++;
        return true;
    }

    /// <summary>
    /// Take an open contract as posted, through the same checks and market commands an AI actor uses: carry a haul,
    /// fill a delivered Sale (buying the goods at the source the check picks), or buy a lot and collect it.
    /// </summary>
    public DealOutcome Accept(ContractId id)
    {
        DealOutcome outcome;
        switch (Context.Contract(id))
        {
            case HaulContract haul:
            {
                PlayerActions.HaulFeasibility check = PlayerActions.CheckHaul(Context, Player, haul);
                outcome = check.Ok ? new DealOutcome(DealBlock.None, PlayerActions.TakeHaul(Context, Player, haul, check.Ship!)) : new DealOutcome(check.Block, null);
                break;
            }

            case SaleContract { Side: SaleSide.Wanted } wanted:
            {
                PlayerActions.FillFeasibility check = PlayerActions.CheckFill(Context, Player, wanted);
                outcome = check.Ok ? new DealOutcome(DealBlock.None, PlayerActions.Fill(Context, Player, wanted, check.Ship!, check.Source!)) : new DealOutcome(check.Block, null);
                break;
            }

            case SaleContract lot:
            {
                PlayerActions.BuyFeasibility check = PlayerActions.CheckBuy(Context, Player, lot);
                outcome = check.Ok ? new DealOutcome(DealBlock.None, PlayerActions.Buy(Context, Player, lot, check.Ship!)) : new DealOutcome(check.Block, null);
                break;
            }

            default:
                outcome = new DealOutcome(DealBlock.NotOpen, null);
                break;
        }

        Revision++;
        return outcome;
    }

    /// <summary>Put the player's goods of <paramref name="good"/> from the free ship's hold into storage where it is docked.</summary>
    public bool Store(GoodType good) => Changed(PlayerActions.Store(Context, Player, PlayerShip, good));

    /// <summary>Load the player's goods of <paramref name="good"/> from storage where the free ship is docked.</summary>
    public bool Retrieve(GoodType good) => Changed(PlayerActions.Retrieve(Context, Player, PlayerShip, good));

    /// <summary>The player's contracts that are signed and not yet settled, in the order it joined them.</summary>
    public IEnumerable<Contract> PlayerActiveContracts =>
        Context.LiveContractsOf(Player.Id).Where(c => c.Status == ContractStatus.Active && c is not StorageContract);

    private bool Changed(bool done)
    {
        if (done)
        {
            Revision++;
        }

        return done;
    }
}
