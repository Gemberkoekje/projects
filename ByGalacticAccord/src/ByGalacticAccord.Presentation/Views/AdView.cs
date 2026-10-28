using System.Collections.Generic;
using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Engine.Simulation;
using ByGalacticAccord.Presentation.Session;
using ByGalacticAccord.Presentation.Text;

namespace ByGalacticAccord.Presentation.Views;

/// <summary>A system a post could also be shown in: its name, how far, what it costs and how long until it appears, and whether it is ticked.</summary>
public sealed record ShowOption(LocationId At, string Label, string Detail, decimal Fee, bool Chosen);

/// <summary>
/// Where a post is shown, and where else it could be (design §7.1, D38): a post is shown in its own system at once; the
/// player pays to show it further. The composer and the player's own open posts carry one.
/// </summary>
public sealed record ShowingModel(string Title, string Note, IReadOnlyList<ShowOption> Options, string Unchosen, string ActionLabel)
{
    /// <summary>What the ticked systems cost in all.</summary>
    public decimal Fee => Options.Where(o => o.Chosen).Sum(o => o.Fee);

    /// <summary>"Showing it in 3 more systems costs ₵0.90" (ad fees are small: to the cent), or where it is shown when none is ticked.</summary>
    public string Total => Options.Count(o => o.Chosen) switch
    {
        0 => Unchosen,
        1 => Templates.Fill("Ads.Total.One", ("fee", Format.MoneyCents(Fee))),
        int count => Templates.Fill("Ads.Total", ("count", count.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("fee", Format.MoneyCents(Fee))),
    };

    /// <summary>The same choices with these systems ticked: the contract panel keeps its ticks until the player pays.</summary>
    public ShowingModel Choose(IReadOnlyCollection<LocationId> chosen) =>
        this with { Options = Options.Select(o => o with { Chosen = chosen.Contains(o.At) }).ToList() };
}

/// <summary>Words and choices for advertising a post further out (<see cref="Advertising"/>).</summary>
public static class AdView
{
    /// <summary>The composer's choices: every system beyond the draft's own places, with its fee, the ticked ones chosen.</summary>
    public static ShowingModel ForDraft(GameSession session, PostDraft draft)
    {
        SimulationContext context = session.Context;
        var chosen = new HashSet<LocationId>(draft.ShowAt ?? System.Array.Empty<LocationId>());
        IReadOnlyList<(LocationId At, int Jumps, Credits Fee)> options = Advertising.Options(context, draft.Places, draft.Offer.Price);
        return Model(session, draft.Places, options, chosen);
    }

    /// <summary>For the player's own open post: where else it could be shown. Null for any other contract.</summary>
    public static ShowingModel? ForPost(GameSession session, Contract contract)
    {
        if (contract.PostedBy != session.Player.Id || !contract.IsOpen || contract is StorageContract)
        {
            return null;
        }

        IReadOnlyList<(LocationId At, int Jumps, Credits Fee)> options = Advertising.Options(session.Context, contract);
        return options.Count == 0 ? null : Model(session, contract.Places, options, new HashSet<LocationId>());
    }

    /// <summary>Where a contract is shown, soonest first (its own places, then where it was paid for): "Shown at Verdant; Meridian Station from Day 2 · 10:00."</summary>
    public static string ShownLine(GameSession session, Contract contract)
    {
        SimulationContext context = session.Context;
        var parts = new List<string>();
        foreach ((LocationId at, long from) in contract.Shown.OrderBy(s => s.From).ThenBy(s => s.At.Value))
        {
            string place = context.Location(at).Name;
            parts.Add(from <= session.Now ? place : Templates.Fill("Ads.Shown.From", ("place", place), ("date", Format.Date(from))));
        }

        return Templates.Fill("Ads.Shown", ("places", string.Join(Templates.Get("Ads.Separator"), parts)));
    }

    /// <summary>What came of showing the player's post further: where, for how much, or why not.</summary>
    public static string OutcomeText(GameSession session, Contract contract, IReadOnlyCollection<LocationId> at, AdBlock block, Credits fee)
    {
        SimulationContext context = session.Context;
        return block switch
        {
            AdBlock.None => Templates.Fill(
                "Outcome.Advertised", ("places", string.Join(Templates.Get("Ads.Separator"), at.OrderBy(l => l.Value).Select(l => context.Location(l).Name))),
                ("fee", Format.MoneyCents(fee))),
            AdBlock.CannotAfford => Templates.Fill("AdBlock.CannotAfford", ("fee", Format.MoneyCents(fee)), ("credits", Format.MoneyCents(session.Player.Credits))),
            _ => Templates.Get("AdBlock." + block),
        };
    }

    private static ShowingModel Model(
        GameSession session, IReadOnlyList<LocationId> places, IReadOnlyList<(LocationId At, int Jumps, Credits Fee)> options, HashSet<LocationId> chosen)
    {
        SimulationContext context = session.Context;
        var list = options.Select(o => new ShowOption(
            o.At,
            context.Location(o.At).Name,
            Templates.Fill(
                "Ads.Option", ("jumps", Templates.Fill(o.Jumps == 1 ? "Ads.Jumps.One" : "Ads.Jumps", ("n", o.Jumps.ToString(System.Globalization.CultureInfo.InvariantCulture)))),
                ("fee", Format.MoneyCents(o.Fee.Amount)), ("delay", Format.Duration(Advertising.DelayHours(context, o.Jumps)))),
            o.Fee.Amount,
            chosen.Contains(o.At))).ToList();
        string own = string.Join(Templates.Get("Ads.Separator"), places.Distinct().Select(p => context.Location(p).Name));
        string note = Templates.Fill("Ads.Note", ("growth", context.Config.AdFeeGrowth.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));
        return new ShowingModel(Templates.Get("Ads.Title"), note, list, Templates.Fill("Ads.Total.None", ("places", own)), Templates.Get("Ads.Show"));
    }
}
