using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Engine.Simulation;
using ByGalacticAccord.Presentation.Session;
using ByGalacticAccord.Presentation.Text;

namespace ByGalacticAccord.Presentation.Views;

/// <summary>
/// The composer's fields as the player has them (the client keeps it): what kind of post, which goods and how many, where
/// (and for a haul, to where), and its first offer: price, how many hours it runs, package and late fee.
/// </summary>
public sealed record ComposerDraft(
    PostKind Kind,
    GoodType Good,
    decimal Quantity,
    LocationId Place,
    LocationId Destination,
    decimal Price,
    long Hours,
    TermPackage Package,
    decimal LateFeeScale);

/// <summary>
/// The post composer (design §3.6, §11.3): the choices for each field, what posting it means, the reference figures the
/// player has (its board, a good's base value, the usual fee for a trip), its terms as sentences, what it commits, and
/// whether it can be posted.
/// </summary>
public sealed record ComposerViewModel(
    string Title,
    string Hint,
    IReadOnlyList<Choice<PostKind>> Kinds,
    IReadOnlyList<Choice<GoodType>> Goods,
    IReadOnlyList<Choice<LocationId>> Places,
    IReadOnlyList<Choice<long>> Durations,
    IReadOnlyList<Choice<TermPackage>> Packages,
    IReadOnlyList<Choice<decimal>> LateFees,
    string KindLabel,
    string GoodLabel,
    string QuantityLabel,
    string PlaceLabel,
    string DestinationLabel,
    string PriceLabel,
    string DurationLabel,
    string PackageLabel,
    string LateFeeLabel,
    bool HasDestination,
    string KindNote,
    string Reference,
    string? Stored,
    string TermsTitle,
    IReadOnlyList<string> Terms,
    string Commitment,
    string? Blocked,
    bool CanPost,
    string PostLabel,
    string CloseLabel);

/// <summary>Builds the composer from the player's draft, and turns a draft into what the engine posts.</summary>
public static class ComposerView
{
    /// <summary>How long a post may run, in hours: half a day to two weeks.</summary>
    public static readonly long[] Durations = { 12, 24, 48, 72, 96, 120, 168, 240, 336 };

    /// <summary>
    /// A first draft of a kind: a wanted Sale of food delivered where the player's ship is; or a lot, or a haul, of the first
    /// goods it has in storage. Priced at the reference the composer shows: the goods' base value, or the usual fee.
    /// </summary>
    public static ComposerDraft Default(GameSession session, PostKind kind)
    {
        SimulationContext context = session.Context;
        LocationId here = session.PlayerShip.Location;
        GoodType good = GoodType.Food;
        decimal quantity = 60m;
        LocationId place = here;
        if (kind != PostKind.Wanted && context.StoragesOf(session.Player.Id).FirstOrDefault(s => s.Lots.Any(l => s.Available(l.Good) > 0m)) is { } storage)
        {
            Lot lot = storage.Lots.First(l => storage.Available(l.Good) > 0m);
            good = lot.Good;
            quantity = storage.Available(good);
            place = storage.Location;
        }

        LocationId destination = context.Locations.Keys.OrderBy(l => l.Value).FirstOrDefault(l => l != place && context.FindPath(place, l) is not null);
        var draft = new ComposerDraft(kind, good, quantity, place, destination, 0m, 72, TermPackage.Standard, 1m);
        return draft with { Price = ReferencePrice(context, draft) };
    }

    /// <summary>The price the composer suggests: the goods' base value for a Sale, the usual fee for a haul's trip.</summary>
    public static decimal ReferencePrice(SimulationContext context, ComposerDraft draft) =>
        draft.Kind == PostKind.Haul
            ? DecisionEngine.UsualHaulOffer(context, draft.Place, draft.Destination).Amount
            : System.Math.Round(Engine.Domain.Goods.Info(draft.Good).BaseValue * draft.Quantity, 2);

    /// <summary>The engine's post for a draft: its deadline counted from now.</summary>
    public static PostDraft ToPost(GameSession session, ComposerDraft draft) =>
        new(draft.Kind, draft.Good, draft.Quantity, draft.Place, draft.Kind == PostKind.Haul ? draft.Destination : null,
            new Offer(Credits.Of(System.Math.Round(draft.Price, 2)), session.Now + draft.Hours, draft.Package, draft.LateFeeScale));

    public static ComposerViewModel Build(GameSession session, ComposerDraft draft)
    {
        SimulationContext context = session.Context;
        long now = session.Now;
        PostDraft post = ToPost(session, draft);
        PostBlock block = PlayerActions.CheckPost(context, session.Player, post);
        string place = context.Location(draft.Place).Name;
        string good = Templates.Good(draft.Good);
        string quantity = Format.Quantity(draft.Quantity);

        var kinds = new[] { PostKind.Wanted, PostKind.Lot, PostKind.Haul }.Select(k => new Choice<PostKind>(k, Templates.Get("Composer.Kind." + k))).ToArray();
        var goods = Engine.Domain.Goods.All.Select(g => new Choice<GoodType>(g, Templates.Good(g))).ToArray();
        var places = context.Locations.Values.OrderBy(l => l.Id.Value).Select(l => new Choice<LocationId>(l.Id, l.Name)).ToArray();
        var durations = Durations.Select(h => new Choice<long>(h, Templates.Fill("Composer.Duration", ("hours", Format.Duration(h)), ("date", Format.Date(now + h))))).ToArray();

        string? stored = null;
        if (draft.Kind != PostKind.Wanted)
        {
            decimal free = context.StorageOf(session.Player.Id, draft.Place)?.Available(draft.Good) ?? 0m;
            stored = free > 0m
                ? Templates.Fill("Composer.Stored", ("free", Format.Quantity(free)), ("good", good), ("place", place))
                : Templates.Fill("Composer.Stored.None", ("good", good), ("place", place));
        }

        IReadOnlyList<string> terms = draft.Quantity > 0m && draft.Price > 0m
            ? ContractView.TermsOf(session, Draft(session, post))
            : System.Array.Empty<string>();
        return new ComposerViewModel(
            Templates.Get("Composer.Title"), Templates.Get("Composer.Hint"),
            kinds, goods, places, durations, NegotiationView.Packages(), NegotiationView.LateFees(),
            Templates.Get("Composer.Field.Kind"), Templates.Get("Composer.Field.Good"), Templates.Get("Composer.Field.Quantity"),
            Templates.Get(draft.Kind == PostKind.Haul ? "Composer.Field.Origin" : "Composer.Field.Place"), Templates.Get("Composer.Field.Destination"),
            Templates.Get(draft.Kind == PostKind.Haul ? "Composer.Field.Fee" : "Composer.Field.Price"), Templates.Get("Composer.Field.Duration"),
            Templates.Get("Form.Package"), Templates.Get("Form.LateFee.Label"),
            draft.Kind == PostKind.Haul,
            Templates.Fill("Composer.Note." + draft.Kind, ("place", place), ("good", good), ("quantity", quantity)),
            Reference(session, draft),
            stored,
            Templates.Fill("Contract.Terms.Package", ("package", Templates.Package(draft.Package))), terms,
            Commitment(session, post),
            block == PostBlock.None ? null : BlockText(session, post, block),
            block == PostBlock.None,
            Templates.Get("Composer.Post"), Templates.Get("Composer.Close"));
    }

    /// <summary>Why the player can't post a draft, in words.</summary>
    public static string BlockText(GameSession session, PostDraft draft, PostBlock block)
    {
        SimulationContext context = session.Context;
        string place = context.Location(draft.Place).Name;
        return block switch
        {
            PostBlock.NoGoods => Templates.Fill(
                "PostBlock.NoGoods", ("quantity", Format.Quantity(draft.Quantity)), ("good", Templates.Good(draft.Good)), ("place", place)),
            PostBlock.NoRoute => Templates.Fill(
                "PostBlock.NoRoute", ("origin", place), ("destination", draft.Destination is { } d ? context.Location(d).Name : place)),
            PostBlock.CannotAfford => Templates.Fill(
                "PostBlock.CannotAfford", ("cost", Format.Money(Credits.Of(draft.Offer.Price.Amount * (1m + TaxAt(context, draft))))), ("credits", Format.Money(session.Player.Credits))),
            _ => Templates.Fill("PostBlock." + block, ("place", place)),
        };
    }

    /// <summary>What came of the player's post: posted, or why not.</summary>
    public static string OutcomeText(GameSession session, PostOutcome outcome, PostDraft draft) =>
        outcome.Post is { } post
            ? Templates.Fill("Outcome.Posted", ("head", ContractView.Head(session.Context, post)), ("deadline", Format.Date(post.DeadlineTick)))
            : BlockText(session, draft, outcome.Block);

    // The figures the player has to price it by: the base value and the board for a Sale; the usual fee for a haul's trip.
    private static string Reference(GameSession session, ComposerDraft draft)
    {
        SimulationContext context = session.Context;
        if (draft.Kind == PostKind.Haul)
        {
            return context.FindPath(draft.Place, draft.Destination) is null || draft.Place == draft.Destination
                ? Templates.Get("Composer.Reference.NoTrip")
                : Templates.Fill(
                    "Composer.Reference.Haul", ("fee", Format.Money(DecisionEngine.UsualHaulOffer(context, draft.Place, draft.Destination))),
                    ("origin", context.Location(draft.Place).Name), ("destination", context.Location(draft.Destination).Name));
        }

        decimal unit = Engine.Domain.Goods.Info(draft.Good).BaseValue;
        string reference = Templates.Fill("Composer.Reference.Base", ("good", Templates.Good(draft.Good)), ("unit", Format.MoneyCents(unit)));
        var forSale = new List<decimal>();
        var wanted = new List<decimal>();
        foreach (Contract contract in Visibility.OpenContractsVisibleTo(context, session.Player))
        {
            if (contract is SaleContract sale && sale.Good == draft.Good && sale.PostedBy != session.Player.Id && sale.Quantity > 0m)
            {
                (sale.Side == SaleSide.ForSale ? forSale : wanted).Add(sale.OfferedPrice.Amount / sale.Quantity);
            }
        }

        reference += " " + Range("Composer.Reference.ForSale", forSale) + " " + Range("Composer.Reference.Wanted", wanted);
        return reference;
    }

    private static string Range(string key, List<decimal> units) =>
        units.Count == 0
            ? Templates.Get(key + ".None")
            : Templates.Fill(key, ("low", Format.MoneyCents(units.Min())), ("high", Format.MoneyCents(units.Max())), ("count", units.Count.ToString(CultureInfo.InvariantCulture)));

    // What posting it commits the player to, and when.
    private static string Commitment(GameSession session, PostDraft draft)
    {
        SimulationContext context = session.Context;
        bool accord = TaxAt(context, draft) > 0m;
        var facts = new[]
        {
            ("price", Format.Money(draft.Offer.Price)),
            ("tax", Format.Percent(TaxAt(context, draft))),
            ("place", context.Location(draft.Place).Name),
            ("quantity", Format.Quantity(draft.Quantity)),
            ("good", Templates.Good(draft.Good)),
        };
        string key = "Composer.Commits." + draft.Kind + (accord || draft.Kind == PostKind.Lot ? string.Empty : ".Frontier");
        return Templates.Fill(key, facts) + " " + Templates.Get("Composer.Commits.Answers");
    }

    // The Accord tax on the price where it would be performed: a Sale's place, a haul's destination.
    private static decimal TaxAt(SimulationContext context, PostDraft draft) =>
        Terms.TaxRate(context, context.Location(draft.Kind == PostKind.Haul && draft.Destination is { } d ? d : draft.Place).Reach);

    // The contract the draft would post, unregistered, so its terms read exactly as the posted one's will.
    private static Contract Draft(GameSession session, PostDraft draft)
    {
        SimulationContext context = session.Context;
        Offer offer = draft.Offer;
        long now = session.Now;
        if (draft.Kind == PostKind.Haul && draft.Destination is { } destination)
        {
            Location to = context.Location(destination);
            return new HaulContract(
                new ContractId(0), session.Player.Id, now, draft.Good, draft.Quantity, draft.Place, destination, Holding.StorageAt(draft.Place), null,
                offer.Price, offer.Price, Terms.TaxRate(context, to.Reach), offer.Deadline, to.Reach, to.Region,
                Terms.ForHaul(offer.Package, draft.Good, offer.Price, draft.Quantity, Credits.Of(Engine.Domain.Goods.Info(draft.Good).BaseValue), offer.LateFeeScale));
        }

        Location at = context.Location(draft.Place);
        decimal? seen = draft.Kind == PostKind.Lot ? context.StorageOf(session.Player.Id, draft.Place)?.Peek(draft.Good, now)?.Quality : null;
        return new SaleContract(
            new ContractId(0), session.Player.Id, now, draft.Kind == PostKind.Wanted ? SaleSide.Wanted : SaleSide.ForSale, draft.Good, draft.Quantity, draft.Place,
            offer.Price, offer.Price, Terms.TaxRate(context, at.Reach), offer.Deadline, at.Reach, at.Region,
            Terms.ForSale(offer.Package, draft.Good, offer.Price, seen, offer.Deadline - now, offer.LateFeeScale));
    }
}
