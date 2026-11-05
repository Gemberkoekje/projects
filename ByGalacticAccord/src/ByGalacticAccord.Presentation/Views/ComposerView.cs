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
/// (and for a haul, to where), its first offer (price, how many hours it runs, package and late fee), and the further systems
/// to show it in, for a fee (design §7.1, D38).
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
    decimal LateFeeScale,
    IReadOnlyList<LocationId>? ShowAt = null,
    ShipParts? Spec = null,
    ShipId? Ship = null);

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
    ShowingModel Showing,
    string Commitment,
    string? Blocked,
    bool CanPost,
    string PostLabel,
    string CloseLabel,
    bool IsBuild = false,
    IReadOnlyList<Choice<Design?>>? Designs = null,
    IReadOnlyList<Choice<PartKind>>? Frames = null,
    IReadOnlyList<Choice<PartKind>>? Drives = null,
    IReadOnlyList<Choice<PartKind?>>? Modules = null,
    Design? Design = null,
    string DesignLabel = "",
    string FrameLabel = "",
    string DriveLabel = "",
    string ModuleLabel = "",
    IReadOnlyList<StatLine>? SpecStats = null);

/// <summary>Builds the composer from the player's draft, and turns a draft into what the engine posts.</summary>
public static class ComposerView
{
    /// <summary>How long a post may run, in hours: half a day to two weeks.</summary>
    public static readonly long[] Durations = { 12, 24, 48, 72, 96, 120, 168, 240, 336 };

    /// <summary>How long a Build may run: two to nine weeks (design §17: a custom build takes two to four weeks).</summary>
    public static readonly long[] BuildDurations = { 14 * 24, 21 * 24, 28 * 24, 35 * 24, 42 * 24, 63 * 24 };

    /// <summary>
    /// A first Build: a Refrigerated Transport, posted where the player is based, for its parts' and assembly's value, due in four
    /// weeks, and shown where the yards are (design §15.5) so they can bid.
    /// </summary>
    private static ComposerDraft DefaultBuild(GameSession session)
    {
        SimulationContext context = session.Context;
        ShipParts spec = Engine.Domain.Designs.Parts(Engine.Domain.Design.RefrigeratedTransport);
        LocationId home = session.Player.Home;
        var yards = context.ActorsOrdered.Where(a => a.Yard is not null && a.Home != home).Select(a => a.Home).Distinct().ToList();
        return new ComposerDraft(PostKind.Build, GoodType.Food, 1m, home, home, ShipStats.From(spec).Value.Amount, 28 * 24, TermPackage.Standard, 1m, yards, spec);
    }

    /// <summary>
    /// A first draft of a kind: a wanted Sale of food delivered where the player's ship is; or a lot, or a haul, of the first
    /// goods it has in storage. Priced at the reference the composer shows: the goods' base value, or the usual fee.
    /// </summary>
    public static ComposerDraft Default(GameSession session, PostKind kind)
    {
        if (kind == PostKind.Build)
        {
            return DefaultBuild(session);
        }

        if (kind == PostKind.Ship)
        {
            // One of its ships for sale where it is: the last it has, at 85% of its value, for a month.
            Ship ship = session.Context.Ship(session.Player.Ships[^1]);
            return new ComposerDraft(
                PostKind.Ship, GoodType.Food, 1m, ship.Location, ship.Location, System.Math.Round(ship.Stats.Value.Amount * 0.85m, 2), 30 * 24,
                TermPackage.Standard, 1m, null, null, ship.Id);
        }

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
    public static decimal ReferencePrice(SimulationContext context, ComposerDraft draft) => draft.Kind switch
    {
        PostKind.Haul => DecisionEngine.UsualHaulOffer(context, draft.Place, draft.Destination).Amount,
        PostKind.Build when draft.Spec is { } spec => ShipStats.From(spec).Value.Amount,
        _ => System.Math.Round(Engine.Domain.Goods.Info(draft.Good).BaseValue * draft.Quantity, 2),
    };

    /// <summary>The engine's post for a draft: its deadline counted from now; shown further only where that is still a choice (not one of its own places).</summary>
    public static PostDraft ToPost(GameSession session, ComposerDraft draft)
    {
        var post = new PostDraft(
            draft.Kind, draft.Good, draft.Quantity, draft.Place, draft.Kind == PostKind.Haul ? draft.Destination : null,
            new Offer(Credits.Of(System.Math.Round(draft.Price, 2)), session.Now + draft.Hours, draft.Package, draft.LateFeeScale), null,
            draft.Kind == PostKind.Build ? draft.Spec : null, draft.Kind == PostKind.Ship ? draft.Ship : null);
        var further = Advertising.Options(session.Context, post.Places, post.Offer.Price).Select(o => o.At).ToHashSet();
        return post with { ShowAt = (draft.ShowAt ?? System.Array.Empty<LocationId>()).Where(further.Contains).Distinct().ToList() };
    }

    public static ComposerViewModel Build(GameSession session, ComposerDraft draft)
    {
        SimulationContext context = session.Context;
        long now = session.Now;
        PostDraft post = ToPost(session, draft);
        PostBlock block = PlayerActions.CheckPost(context, session.Player, post);
        string place = context.Location(draft.Place).Name;
        string good = Templates.Good(draft.Good);
        string quantity = Format.Quantity(draft.Quantity);

        bool build = draft.Kind == PostKind.Build && draft.Spec is not null;
        var kinds = new[] { PostKind.Wanted, PostKind.Lot, PostKind.Haul, PostKind.Build }.Select(k => new Choice<PostKind>(k, Templates.Get("Composer.Kind." + k))).ToArray();
        var goods = Engine.Domain.Goods.Trade.Select(g => new Choice<GoodType>(g, Templates.Good(g))).ToArray();
        var places = context.Locations.Values.OrderBy(l => l.Id.Value).Select(l => new Choice<LocationId>(l.Id, l.Name)).ToArray();
        var durations = (build ? BuildDurations : Durations).Select(h => new Choice<long>(h, Templates.Fill("Composer.Duration", ("hours", Format.Duration(h)), ("date", Format.Date(now + h))))).ToArray();

        string? stored = null;
        if (draft.Kind is PostKind.Lot or PostKind.Haul)
        {
            decimal free = context.StorageOf(session.Player.Id, draft.Place)?.Available(draft.Good) ?? 0m;
            stored = free > 0m
                ? Templates.Fill("Composer.Stored", ("free", Format.Quantity(free)), ("good", good), ("place", place))
                : Templates.Fill("Composer.Stored.None", ("good", good), ("place", place));
        }

        IReadOnlyList<string> terms = draft.Quantity > 0m && draft.Price > 0m
            ? ContractView.TermsOf(session, Draft(session, post))
            : System.Array.Empty<string>();
        string note = build
            ? Templates.Fill("Composer.Note.Build", ("ship", Templates.ShipName(draft.Spec!)), ("place", place))
            : Templates.Fill("Composer.Note." + draft.Kind, ("place", place), ("good", good), ("quantity", quantity));
        return new ComposerViewModel(
            Templates.Get("Composer.Title"), Templates.Get("Composer.Hint"),
            kinds, goods, places, durations, NegotiationView.Packages(), NegotiationView.LateFees(),
            Templates.Get("Composer.Field.Kind"), Templates.Get("Composer.Field.Good"), Templates.Get("Composer.Field.Quantity"),
            Templates.Get(draft.Kind == PostKind.Haul ? "Composer.Field.Origin" : "Composer.Field.Place"), Templates.Get("Composer.Field.Destination"),
            Templates.Get(draft.Kind == PostKind.Haul ? "Composer.Field.Fee" : "Composer.Field.Price"), Templates.Get("Composer.Field.Duration"),
            Templates.Get("Form.Package"), Templates.Get("Form.LateFee.Label"),
            draft.Kind == PostKind.Haul,
            note,
            Reference(session, draft),
            stored,
            Templates.Fill("Contract.Terms.Package", ("package", Templates.Package(draft.Package))), terms,
            AdView.ForDraft(session, post),
            Commitment(session, post),
            block == PostBlock.None ? null : BlockText(session, post, block),
            block == PostBlock.None,
            Templates.Get("Composer.Post"), Templates.Get("Composer.Close"),
            build,
            build ? DesignChoices() : null,
            build ? ShipPart.KindsFor(PartSlot.Frame).Select(k => new Choice<PartKind>(k, Templates.Part(k))).ToArray() : null,
            build ? ShipPart.KindsFor(PartSlot.Drive).Select(k => new Choice<PartKind>(k, Templates.Part(k))).ToArray() : null,
            build ? ModuleChoices() : null,
            build ? draft.Spec!.Design : null,
            Templates.Get("Composer.Field.Design"), Templates.Get("Composer.Field.Frame"), Templates.Get("Composer.Field.Drive"), Templates.Get("Composer.Field.Module"),
            build ? SpecStats(draft.Spec!) : null);
    }

    /// <summary>The five designs, and custom: choosing a design fills in its parts (design §6.2).</summary>
    private static IReadOnlyList<Choice<Design?>> DesignChoices()
    {
        var choices = Engine.Domain.Designs.All.Select(d => new Choice<Design?>(d, Templates.Design(d))).ToList();
        choices.Add(new Choice<Design?>(null, Templates.Get("Composer.Design.Custom")));
        return choices;
    }

    private static IReadOnlyList<Choice<PartKind?>> ModuleChoices()
    {
        var choices = new List<Choice<PartKind?>> { new(null, Templates.Get("Part.None")) };
        choices.AddRange(ShipPart.KindsFor(PartSlot.Module).Select(k => new Choice<PartKind?>(k, Templates.Part(k))));
        return choices;
    }

    /// <summary>What the parts ordered would make, stat by stat with the parts behind each (design §6.1).</summary>
    public static IReadOnlyList<StatLine> SpecStats(ShipParts spec)
    {
        ShipStats stats = ShipStats.From(spec);
        var lines = new List<StatLine>
        {
            ShipView.Stat(spec, ShipStat.Cargo, Format.Quantity(stats.CargoCapacity)),
            ShipView.Stat(spec, ShipStat.Speed, stats.Speed.ToString("0.###", CultureInfo.InvariantCulture)),
            ShipView.Stat(spec, ShipStat.Toughness, stats.Toughness.ToString("0.###", CultureInfo.InvariantCulture)),
        };
        if (stats.Preservation > 0m)
        {
            lines.Add(ShipView.Stat(spec, ShipStat.Preservation, Format.Percent(stats.Preservation)));
        }

        lines.Add(ShipView.Stat(spec, ShipStat.RunningCost, Format.MoneyCents(Credits.Of(stats.RunningCostPerTick.Amount * GameTime.HoursPerDay))));
        lines.Add(ShipView.Stat(spec, ShipStat.Value, Format.Money(stats.Value)));
        return lines;
    }

    /// <summary>A draft with its spec changed: a design's parts, or one part swapped (which makes it custom unless it still matches a design).</summary>
    public static ComposerDraft WithSpec(SimulationContext context, ComposerDraft draft, ShipParts spec)
    {
        bool priced = draft.Spec is { } before && System.Math.Abs(draft.Price - ShipStats.From(before).Value.Amount) < 0.01m;
        ComposerDraft changed = draft with { Spec = spec };
        return priced ? changed with { Price = ReferencePrice(context, changed) } : changed;
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
            PostBlock.CannotAfford => CannotAfford(session, draft),
            _ => Templates.Fill("PostBlock." + block, ("place", place)),
        };
    }

    /// <summary>What came of the player's post: posted, or why not.</summary>
    public static string OutcomeText(GameSession session, PostOutcome outcome, PostDraft draft) =>
        outcome.Post is { } post
            ? Templates.Fill("Outcome.Posted", ("head", ContractView.Head(session.Context, post)), ("deadline", Format.Date(post.DeadlineTick)))
            : BlockText(session, draft, outcome.Block);

    // The figures the player has to price it by: the base value and the board for a Sale; the usual fee for a haul's trip; a ship's
    // parts and assembly for a Build.
    private static string Reference(GameSession session, ComposerDraft draft)
    {
        SimulationContext context = session.Context;
        if (draft.Kind == PostKind.Build && draft.Spec is { } spec)
        {
            return Templates.Fill("Composer.Reference.Build", ("value", Format.Money(ShipStats.From(spec).Value)), ("ship", Templates.ShipName(spec)));
        }

        if (draft.Kind == PostKind.Ship && draft.Ship is { } shipId)
        {
            return Templates.Fill("Composer.Reference.Ship", ("value", Format.Money(context.Ship(shipId).Stats.Value)));
        }

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
        foreach (Contract contract in session.KnownOpen())
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
        if (draft.Kind == PostKind.Build)
        {
            Credits deposit = BuildContract.DepositFor(draft.Offer.Price);
            return Templates.Fill(
                "Composer.Commits.Build", ("deposit", Format.Money(deposit)), ("balance", Format.Money(draft.Offer.Price - deposit)),
                ("tax", Format.Percent(context.Config.TaxRate)))
                + (draft.ShowFee(context).Amount > 0m ? " " + Templates.Fill("Composer.Commits.Showing", ("fee", Format.MoneyCents(draft.ShowFee(context)))) : string.Empty)
                + " " + Templates.Get("Composer.Commits.Bids");
        }

        if (draft.Kind == PostKind.Ship)
        {
            return Templates.Get("Composer.Commits.Ship");
        }

        string key = "Composer.Commits." + draft.Kind + (accord || draft.Kind == PostKind.Lot ? string.Empty : ".Frontier");
        Credits showing = draft.ShowFee(context);
        string shown = showing.Amount > 0m ? " " + Templates.Fill("Composer.Commits.Showing", ("fee", Format.MoneyCents(showing))) : string.Empty;
        return Templates.Fill(key, facts) + shown + " " + Templates.Get("Composer.Commits.Answers");
    }

    // What posting it would cost against what the player has; with a fee to show it further, to the cent (design §7.1, D38).
    private static string CannotAfford(GameSession session, PostDraft draft)
    {
        SimulationContext context = session.Context;
        decimal locked = draft.Kind == PostKind.Lot ? 0m : draft.Offer.Price.Amount * (1m + (draft.Kind == PostKind.Build ? context.Config.TaxRate : TaxAt(context, draft)));
        decimal showing = draft.ShowFee(context).Amount;
        return showing > 0m
            ? Templates.Fill("PostBlock.CannotAfford.Showing", ("cost", Format.MoneyCents(locked + showing)), ("credits", Format.MoneyCents(session.Player.Credits)))
            : Templates.Fill("PostBlock.CannotAfford", ("cost", Format.Money(Credits.Of(locked))), ("credits", Format.Money(session.Player.Credits)));
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
        if (draft.Kind == PostKind.Build && draft.Spec is { } spec)
        {
            Location here = context.Location(draft.Place);
            return new BuildContract(
                new ContractId(0), session.Player.Id, now, spec, draft.Place, offer.Price, offer.Price, Terms.TaxRate(context, here.Reach), offer.Deadline,
                here.Reach, here.Region, Terms.ForBuild(offer.Package, offer.Price, offer.LateFeeScale));
        }

        if (draft.Kind == PostKind.Ship && draft.Ship is { } sold)
        {
            Location docked = context.Location(draft.Place);
            return new ShipSaleContract(
                new ContractId(0), session.Player.Id, now, sold, draft.Place, offer.Price, offer.Price, Terms.TaxRate(context, docked.Reach), offer.Deadline,
                docked.Reach, docked.Region, Terms.ForShipSale(offer.Package));
        }

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
