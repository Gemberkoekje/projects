using Curator.Core.Content;

namespace Curator.Core.Game;

/// <summary>An outcome settled at a visit's decision, and whether it has surfaced yet.</summary>
public sealed class Resolution
{
    internal Resolution(string resolutionId) => ResolutionId = resolutionId;

    public string ResolutionId { get; }

    public string PatronId { get; internal set; } = "";

    public string VisitId { get; internal set; } = "";

    /// <summary>The book lent, or empty for declines and walk-outs.</summary>
    public string BookId { get; internal set; } = "";

    public string LoanId { get; internal set; } = "";

    public OutcomeCategory Category { get; internal set; }

    public Cause Cause { get; internal set; }

    public string OutcomeKey { get; internal set; } = "";

    /// <summary>The channel it will surface (or surfaced) on, after any redirect.</summary>
    public OutcomeChannel Channel { get; internal set; }

    /// <summary>The channel the outcome was written for.</summary>
    public OutcomeChannel OriginalChannel { get; internal set; }

    public int ResolvedDay { get; internal set; }

    public int SurfaceDay { get; internal set; }

    public bool Redirected { get; internal set; }

    public bool Surfaced { get; internal set; }

    public int SurfacedDay { get; internal set; }

    public string Headline { get; internal set; } = "";

    public string From { get; internal set; } = "";

    public string Text { get; internal set; } = "";

    public int TrustDelta { get; internal set; }

    public bool HasReputation { get; internal set; }

    public int Reputation { get; internal set; }

    public string RemovePageOnReturn { get; internal set; } = "";
}
