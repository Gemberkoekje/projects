using System.Collections.Generic;
using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;
using ByGalacticAccord.Presentation.Session;
using ByGalacticAccord.Presentation.Text;

namespace ByGalacticAccord.Presentation.Views;

/// <summary>A command on a ledger line: put goods aboard into storage here, or load stored goods aboard.</summary>
public sealed record LedgerAction(string Label, GoodType Good, bool Store);

/// <summary>One line of the ledger: what it is, where or why, and its figure.</summary>
public sealed record LedgerLine(string Label, string Detail, string Value, Tone Tone, LedgerAction? Action = null, ContractId? Contract = null);

/// <summary>
/// The ledger (design §11.2: "how am I doing?", §11.3): the player's money, what it owns and where (design §16.1: in a
/// ship's hold or in storage), and its contracts in progress.
/// </summary>
public sealed record LedgerViewModel(
    string Title,
    IReadOnlyList<LedgerLine> Money,
    string PossessionsTitle,
    IReadOnlyList<LedgerLine> Possessions,
    string PossessionsEmpty,
    string ContractsTitle,
    IReadOnlyList<LedgerLine> Contracts,
    string ContractsEmpty);

public static class LedgerView
{
    public static LedgerViewModel Build(GameSession session)
    {
        SimulationContext context = session.Context;
        HeaderViewModel header = HeaderView.Build(session);
        var money = new List<LedgerLine>
        {
            new(header.CreditsLabel, header.CreditsTip, header.Credits, Tone.Plain),
            new(header.EscrowLabel, header.EscrowTip, header.Escrow, Tone.Plain),
            new(header.RunningLabel, header.RunningTip, header.Running, Tone.Loss),
        };

        return new LedgerViewModel(
            Templates.Get("Ledger.Title"),
            money,
            Templates.Get("Ledger.Possessions"),
            Possessions(session),
            Templates.Get("Ledger.Possessions.Empty"),
            Templates.Get("Ledger.Contracts"),
            Contracts(session),
            Templates.Get("Ledger.Contracts.Empty"));
    }

    private static List<LedgerLine> Possessions(GameSession session)
    {
        SimulationContext context = session.Context;
        ActorState player = session.Player;
        var lines = new List<LedgerLine>();
        foreach (ShipId id in player.Ships)
        {
            Ship ship = context.Ship(id);
            string where = context.Location(ship.Location).Name;
            bool canStore = ship.IsFree && context.StationOperatorAt(ship.Location) is not null;
            foreach (Lot lot in ship.Hold.Where(l => l.Owner == player.Id))
            {
                Cargo goods = lot.At(context.CurrentTick, ship.Preservation).Cargo;
                string detail = Templates.Fill(
                    ship.IsFree ? "Ledger.Hold.Docked" : "Ledger.Hold.Moving", ("shipClass", Templates.ShipClass(ship.Type)), ("where", where));
                lines.Add(new LedgerLine(
                    Goods(goods), Fresh(detail, goods), Worth(goods), Tone.Muted,
                    canStore ? new LedgerAction(Templates.Fill("Ledger.Store", ("place", where)), goods.Good, Store: true) : null));
            }
        }

        Ship flagship = session.PlayerShip;
        foreach (StorageContract storage in context.StoragesOf(player.Id))
        {
            string place = context.Location(storage.Location).Name;
            bool canLoad = flagship.IsFree && flagship.Location == storage.Location && flagship.FreeSpace > 0m;
            foreach (Lot lot in storage.Lots)
            {
                Cargo goods = lot.At(context.CurrentTick, 0m).Cargo;
                string detail = Templates.Fill(
                    "Ledger.Storage", ("place", place),
                    ("rent", Format.MoneyCents(storage.RatePerUnitDay.Amount * goods.Quantity)));
                bool free = storage.Available(goods.Good) > 0m;
                lines.Add(new LedgerLine(
                    Goods(goods), Fresh(detail, goods), Worth(goods), Tone.Muted,
                    canLoad && free ? new LedgerAction(Templates.Get("Ledger.Load"), goods.Good, Store: false) : null));
            }
        }

        return lines;
    }

    private static List<LedgerLine> Contracts(GameSession session)
    {
        SimulationContext context = session.Context;
        var lines = new List<LedgerLine>();
        foreach (Contract contract in session.PlayerActiveContracts)
        {
            bool paying = contract.Payer == session.Player.Id;
            string role = Templates.Get(contract switch
            {
                HaulContract when paying => "Ledger.Role.Shipping",
                HaulContract => "Ledger.Role.Carrying",
                SaleContract when paying => "Ledger.Role.Buying",
                _ => "Ledger.Role.Selling",
            });
            string party = context.Actor(contract.Other(session.Player.Id) ?? contract.PostedBy).Name;
            bool inDefault = contract.InDefault(session.Now);
            string detail = inDefault
                ? Templates.Fill("Ledger.Contract.InDefault", ("role", role), ("party", party), ("when", Format.Date(contract.GraceEndsTick)))
                : Templates.Fill("Ledger.Contract", ("role", role), ("party", party), ("when", Format.Relative(contract.DeadlineTick, session.Now)));
            lines.Add(new LedgerLine(
                ContractView.Head(context, contract), detail, Format.Money(paying ? -contract.Price.Amount : contract.Price.Amount),
                inDefault ? Tone.Warn : paying ? Tone.Loss : Tone.Gain, Contract: contract.Id));
        }

        // The player's own posts still waiting for a taker: a haul it posted for a carrier.
        foreach (Contract post in session.PlayerOpenPosts)
        {
            lines.Add(new LedgerLine(
                ContractView.Head(context, post), Templates.Fill("Ledger.Post", ("when", Format.Relative(post.DeadlineTick, session.Now))),
                Format.Money(-post.OfferedPrice.Amount), Tone.Muted, Contract: post.Id));
        }

        return lines;
    }

    private static string Goods(Cargo goods) =>
        Templates.Fill("Ledger.Goods", ("quantity", Format.Quantity(goods.Quantity)), ("good", Templates.Good(goods.Good)));

    // A perishable says how fresh it is; anything else just where it is.
    private static string Fresh(string detail, Cargo goods) =>
        Engine.Domain.Goods.Info(goods.Good).Perishable
            ? Templates.Fill("Ledger.Fresh", ("detail", detail), ("quality", Format.Percent(goods.Quality)))
            : detail;

    // A fixed reference, labelled as one, not a market price (design §11.5): the goods at the sector's base value.
    private static string Worth(Cargo goods) =>
        Templates.Fill("Ledger.Base", ("value", Format.Money(Engine.Domain.Goods.Info(goods.Good).BaseValue * goods.Quantity)));
}
