using System;
using System.Collections.Generic;
using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
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

/// <summary>What happened when the player tried to post a contract: blocked, or the contract it posted.</summary>
public sealed record PostOutcome(PostBlock Block, Contract? Post)
{
    public bool Posted => Post is not null;
}

/// <summary>
/// One game in progress: the world, the player's actor, and the clock. The clock runs in real time at the
/// chosen speed (1× is one game hour per second) and advances the simulation to whole ticks; the fraction
/// in between is only for drawing ships along their courses. It stops itself when the engine raises a
/// player alert, and when a decision lands on the player (the hard pause, design §8.3): then it won't run
/// again until the player has answered. Player commands go through <see cref="PlayerActions"/>, the same
/// engine path as an AI decision.
/// </summary>
public sealed class GameSession
{
    /// <summary>The longest stretch of real time one frame may advance, so a hidden tab doesn't leap days on return.</summary>
    public const double MaxFrameSeconds = 0.25;

    private double _clock;

    public GameSession(long seed, long startTick = 0)
        : this(WorldGenerator.Generate(seed, withPlayer: true).Context)
    {
        Seed = seed;
        Context.RunTo(startTick);
        _clock = Context.CurrentTick;
    }

    /// <summary>A session over a world already made with a player in it, as it stands: a test's scenario, or later a loaded game.</summary>
    public GameSession(SimulationContext context)
    {
        Context = context;
        Player = context.Actor(context.PlayerActorId ?? throw new ArgumentException("The world has no player.", nameof(context)));
        _clock = context.CurrentTick;
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

    /// <summary>Decisions the terms handed the player, waiting for an answer. While any is, the clock can't run.</summary>
    public IReadOnlyList<PlayerDecision> Decisions => Context.PlayerDecisions;

    /// <summary>The ship the player flies. Slice 1 starts with one.</summary>
    public Ship PlayerShip => Context.Ship(Player.Ships[0]);

    public void SetSpeed(ClockSpeed speed)
    {
        if (speed != ClockSpeed.Paused && Context.HasPlayerDecisions)
        {
            Revision++;
            return; // the world waits for the player's answer
        }

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
        if (stop is RunStop.PlayerAlert or RunStop.PlayerDecision)
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

    /// <summary>
    /// Fill a wanted Sale through a carrier: buy the goods into storage at the source the check picks and post a haul to
    /// the place at the usual offer, the same market command a trader with no free ship uses.
    /// </summary>
    public DealOutcome Broker(ContractId id)
    {
        DealOutcome outcome = new(DealBlock.NotOpen, null);
        if (Context.Contract(id) is SaleContract { Side: SaleSide.Wanted } wanted)
        {
            PlayerActions.BrokerFeasibility check = PlayerActions.CheckBroker(Context, Player, wanted);
            outcome = check.Ok
                ? new DealOutcome(DealBlock.None, PlayerActions.Broker(Context, Player, wanted, check.Source!, check.Fee))
                : new DealOutcome(check.Block, null);
        }

        Revision++;
        return outcome;
    }

    /// <summary>Walk away from one of the player's signed contracts, paying the break fee.</summary>
    public bool CancelAtWill(ContractId id) => Changed(PlayerActions.CancelAtWill(Context, Player, Context.Contract(id)));

    /// <summary>Cancel a contract whose counterparty is in default.</summary>
    public bool CancelForDefault(ContractId id) => Changed(PlayerActions.CancelForDefault(Context, Player, Context.Contract(id)));

    /// <summary>Post a haul again for a Sale the player sold whose goods wait with nothing moving them.</summary>
    public bool HaulAgain(ContractId id)
    {
        if (Context.Contract(id) is not SaleContract sale || PlayerActions.CheckHaulAgain(Context, Player, sale) is not { Ok: true } check)
        {
            return false;
        }

        PlayerActions.HaulAgain(Context, Player, sale, check.From!.Value, check.Fee);
        return Changed(true);
    }

    /// <summary>Answer a decision the terms handed the player.</summary>
    public bool Decide(PlayerDecision decision, DecisionChoice choice) => Changed(PlayerActions.Decide(Context, Player, decision, choice));

    /// <summary>Put the player's goods of <paramref name="good"/> from the free ship's hold into storage where it is docked.</summary>
    public bool Store(GoodType good) => Changed(PlayerActions.Store(Context, Player, PlayerShip, good));

    /// <summary>Load the player's goods of <paramref name="good"/> from storage where the free ship is docked.</summary>
    public bool Retrieve(GoodType good) => Changed(PlayerActions.Retrieve(Context, Player, PlayerShip, good));

    /// <summary>Post a contract (design §3.6): it reaches the actors that can see it, and their answers come over game time.</summary>
    public PostOutcome Post(PostDraft draft)
    {
        PostBlock block = PlayerActions.CheckPost(Context, Player, draft);
        Contract? post = block == PostBlock.None ? PlayerActions.Post(Context, Player, draft) : null;
        Revision++;
        return new PostOutcome(block, post);
    }

    /// <summary>
    /// Make a counter-offer on a contract: on someone else's post to its poster, or on the player's own post in a responder's
    /// haggle. The reply comes in game time. The block says why it couldn't be sent, or none if it was.
    /// </summary>
    public DealBlock Counter(ContractId id, Offer offer, NegotiationId? thread = null)
    {
        Contract contract = Context.Contract(id);
        Negotiation? negotiation = thread is { } t ? Context.Negotiation(t) : null;
        DealBlock block = PlayerActions.CheckCounter(Context, Player, contract, offer, negotiation);
        if (block == DealBlock.None && PlayerActions.Counter(Context, Player, contract, offer, negotiation) is null && !contract.IsOpen)
        {
            block = DealBlock.NotOpen; // known only from the news, and taken since
        }

        Revision++;
        return block;
    }

    /// <summary>Accept the other side's latest offer in a haggle: the contract is signed on it.</summary>
    public Signing AcceptOffer(NegotiationId id)
    {
        Signing signed = PlayerActions.AcceptOffer(Context, Player, Context.Negotiation(id));
        Revision++;
        return signed;
    }

    /// <summary>Decline the other side's latest offer, ending that haggle.</summary>
    public bool Decline(NegotiationId id) => Changed(PlayerActions.Decline(Context, Player, Context.Negotiation(id)));

    /// <summary>
    /// Pay to show one of the player's open posts in further systems (design §7.1, D38): each system's fee goes to its station
    /// operator, and the post appears there a few hours or days later. The block says why not, or none; the fee is what it cost.
    /// </summary>
    public (AdBlock Block, Credits Fee) Advertise(ContractId id, IReadOnlyCollection<LocationId> at)
    {
        Contract contract = Context.Contract(id);
        Credits fee = Advertising.Quote(Context, contract, at) ?? Credits.Zero;
        AdBlock block = PlayerActions.CheckAdvertise(Context, Player, contract, at);
        if (block == AdBlock.None && !PlayerActions.Advertise(Context, Player, contract, at))
        {
            block = AdBlock.NotYours;
        }

        Revision++;
        return (block, fee);
    }

    /// <summary>Take one of the player's open posts down.</summary>
    public bool Withdraw(ContractId id) => Changed(PlayerActions.Withdraw(Context, Player, Context.Contract(id)));

    /// <summary>
    /// What the player can see now (design §7.1): its presence and the tier of every location from it. Built afresh each time,
    /// since a ship that moves changes it.
    /// </summary>
    public Sight Sight => Visibility.Of(Context, Player);

    /// <summary>
    /// The contracts the player believes open, in id order: those it sees live and are open, and those The Daily Accord reported
    /// open that it can't see live and hasn't found closed (design §7.1, D31). Its own posts are among them.
    /// </summary>
    public IReadOnlyList<Contract> KnownOpen() => Knowledge.OpenContracts(Context, Player, Sight);

    /// <summary>The player's haggles, newest first.</summary>
    public IEnumerable<Negotiation> PlayerNegotiations =>
        Context.Negotiations.Values.Where(n => n.Involves(Player.Id)).OrderByDescending(n => n.Id.Value);

    /// <summary>The player's contracts that are signed and not yet settled, in the order it joined them.</summary>
    public IEnumerable<Contract> PlayerActiveContracts =>
        Context.LiveContractsOf(Player.Id).Where(c => c.Status == ContractStatus.Active && c is not StorageContract);

    /// <summary>The player's own posts still open: Sales and hauls it posted, waiting for takers.</summary>
    public IEnumerable<Contract> PlayerOpenPosts =>
        Context.LiveContractsOf(Player.Id).Where(c => c.IsOpen && c.PostedBy == Player.Id);

    private bool Changed(bool done)
    {
        if (done)
        {
            Revision++;
        }

        return done;
    }
}
