using System.Collections.Generic;

namespace ByGalacticAccord.Engine.Domain;

/// <summary>Where a negotiation stands.</summary>
public enum NegotiationStatus
{
    /// <summary>Offers are going back and forth; the contract stays open to everyone meanwhile (design §3.6).</summary>
    Open,

    /// <summary>One side accepted the other's offer and the contract was signed on it.</summary>
    Agreed,

    /// <summary>It ended without a deal (<see cref="Negotiation.End"/> says how).</summary>
    Ended,
}

/// <summary>How a negotiation ended without a deal (design §16.5).</summary>
public enum NegotiationEnd
{
    /// <summary>A side walked away: the gap to the most it would give was too wide.</summary>
    WalkedAway,

    /// <summary>A side walked away when the rounds ran out.</summary>
    RoundsRanOut,

    /// <summary>A side declined the other's offer.</summary>
    Declined,

    /// <summary>The post closed: someone else took it, or it was withdrawn or expired.</summary>
    PostClosed,

    /// <summary>The side that would perform could no longer do it when the offer was accepted.</summary>
    CannotPerform,
}

/// <summary>
/// How a reply reads (design §3.6): its wording is a tell, as its timing is. The hidden numbers stay hidden; how close a
/// counterparty is to its limit shows only in how it answers.
/// </summary>
public enum Tell
{
    /// <summary>No tell: a post, or the player's own offer.</summary>
    None,

    /// <summary>Far from its limit.</summary>
    Readily,

    /// <summary>Getting near its limit.</summary>
    Reluctant,

    /// <summary>At, or all but at, its limit.</summary>
    AtLimit,
}

/// <summary>
/// One offer in a negotiation: who made it, when, on what terms, and, for a reply, how long it took and how it read. Round 0
/// is the post itself; every counter-offer after it is a round.
/// </summary>
public sealed record Bid(int Round, long Tick, ActorId By, Offer Offer, long? ReplyHours = null, Tell Tell = Tell.None);

/// <summary>
/// A haggle over one open contract between its poster and one responder (design §3.6, §16.5): offers going back and forth
/// over game time, each reply taking hours, until one side accepts the other's offer, walks away, or the post closes. The
/// contract stays open to everyone while it runs. Haggles between two AI actors never get here: they settle in closed form
/// (<see cref="Actors.Haggling.Resolve"/>). Never removed from the world, so the interface can show how one went.
/// </summary>
public sealed class Negotiation
{
    private readonly List<Bid> _bids = new();

    public Negotiation(NegotiationId id, ContractId contract, ActorId poster, ActorId responder, long opened)
    {
        Id = id;
        Contract = contract;
        Poster = poster;
        Responder = responder;
        Opened = opened;
    }

    public NegotiationId Id { get; }

    public ContractId Contract { get; }

    public ActorId Poster { get; }

    /// <summary>The other side: the actor that answered the post, or countered it.</summary>
    public ActorId Responder { get; }

    public long Opened { get; }

    public NegotiationStatus Status { get; private set; } = NegotiationStatus.Open;

    /// <summary>How it ended without a deal, once it has.</summary>
    public NegotiationEnd? End { get; private set; }

    /// <summary>Who ended it, or accepted: the side that made the last move.</summary>
    public ActorId? ClosedBy { get; private set; }

    public long? ClosedTick { get; private set; }

    /// <summary>The offers so far, the post first.</summary>
    public IReadOnlyList<Bid> Bids => _bids;

    public Bid Latest => _bids[^1];

    /// <summary>Counter-offers made so far (the post is not one).</summary>
    public int Rounds => _bids.Count == 0 ? 0 : _bids[^1].Round;

    /// <summary>Whose reply it waits for: the side that didn't make the latest offer.</summary>
    public ActorId AwaitingFrom => Other(Latest.By);

    /// <summary>When the reply it waits for is due, if the side it waits on answers in game time.</summary>
    public long? ReplyDue { get; private set; }

    public bool IsOpen => Status == NegotiationStatus.Open;

    public bool Involves(ActorId actor) => Poster == actor || Responder == actor;

    public ActorId Other(ActorId actor) => actor == Poster ? Responder : Poster;

    /// <summary>The latest offer <paramref name="actor"/> made, if any.</summary>
    public Bid? LatestBy(ActorId actor)
    {
        for (int i = _bids.Count - 1; i >= 0; i--)
        {
            if (_bids[i].By == actor)
            {
                return _bids[i];
            }
        }

        return null;
    }

    internal void Add(Bid bid)
    {
        _bids.Add(bid);
        ReplyDue = null;
    }

    internal void AwaitReply(long due) => ReplyDue = due;

    internal void Agree(ActorId by, long tick)
    {
        Status = NegotiationStatus.Agreed;
        ClosedBy = by;
        ClosedTick = tick;
        ReplyDue = null;
    }

    internal void Close(NegotiationEnd end, ActorId? by, long tick)
    {
        Status = NegotiationStatus.Ended;
        End = end;
        ClosedBy = by;
        ClosedTick = tick;
        ReplyDue = null;
    }
}
