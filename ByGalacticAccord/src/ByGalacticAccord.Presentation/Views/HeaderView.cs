using System.Collections.Generic;
using System.Linq;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;
using ByGalacticAccord.Presentation.Session;
using ByGalacticAccord.Presentation.Text;

namespace ByGalacticAccord.Presentation.Views;

/// <summary>A speed button of the time bar.</summary>
public sealed record SpeedButton(ClockSpeed Speed, string Label, bool On);

/// <summary>
/// The time bar and the player's position (design §11.3): speed controls, the date, credits, what is
/// locked in escrow, what the fleet costs to run, where the ship is, and any alert that stopped the clock.
/// </summary>
public sealed record HeaderViewModel(
    string Title,
    string Who,
    IReadOnlyList<SpeedButton> Speeds,
    string Date,
    string SpeedNote,
    string CreditsLabel,
    string Credits,
    string CreditsTip,
    string EscrowLabel,
    string Escrow,
    string EscrowTip,
    string RunningLabel,
    string Running,
    string RunningTip,
    string Fleet,
    string AlertTitle,
    IReadOnlyList<string> Alerts,
    string ResumeLabel);

public static class HeaderView
{
    public static HeaderViewModel Build(GameSession session)
    {
        SimulationContext context = session.Context;
        Ship ship = session.PlayerShip;
        string location = context.Location(ship.Location).Name;

        var speeds = new List<SpeedButton>
        {
            new(ClockSpeed.Paused, Templates.Get("Speed.Paused"), session.Speed == ClockSpeed.Paused),
            new(ClockSpeed.X1, Templates.Get("Speed.X1"), session.Speed == ClockSpeed.X1),
            new(ClockSpeed.X4, Templates.Get("Speed.X4"), session.Speed == ClockSpeed.X4),
            new(ClockSpeed.X16, Templates.Get("Speed.X16"), session.Speed == ClockSpeed.X16),
        };
        string speedNote = session.Speed switch
        {
            ClockSpeed.Paused => Templates.Get("SpeedNote.Paused"),
            ClockSpeed.X1 => Templates.Get("SpeedNote.One"),
            _ => Templates.Fill("SpeedNote.Many", ("hours", ((int)session.Speed).ToString(System.Globalization.CultureInfo.InvariantCulture))),
        };

        // What of the player's own money escrow holds: its payments where it pays, its bond where it performs.
        decimal escrow = session.PlayerActiveContracts.Sum(c => c.Payer == session.Player.Id ? c.EscrowHeld.Amount - c.LockedBond.Amount : c.LockedBond.Amount);
        decimal runningPerDay = session.Player.Ships.Sum(id => context.Ship(id).Class.RunningCostPerTick.Amount) * GameTime.HoursPerDay;
        string shipClass = Templates.ShipClass(ship.Type);

        var alerts = new List<string>();
        foreach (JournalEntry alert in session.Alerts)
        {
            alerts.Add(JournalText.Describe(context, alert));
        }

        return new HeaderViewModel(
            Templates.Get("Header.Title"),
            Templates.Fill("Header.Who", ("role", Templates.Role(session.Player.Role)), ("location", location)),
            speeds,
            Format.Date(session.Now),
            speedNote,
            Templates.Get("Header.Credits"),
            Format.Money(session.Player.Credits),
            Templates.Get("Header.Credits.Tip"),
            Templates.Get("Header.Escrow"),
            Format.Money(escrow),
            Templates.Get("Header.Escrow.Tip"),
            Templates.Get("Header.Running"),
            Templates.Fill("PerDay", ("amount", Format.Money(-runningPerDay))),
            Templates.Fill("Header.Running.Tip", ("shipClass", shipClass), ("rate", Format.MoneyCents(ship.Class.RunningCostPerTick))),
            Fleet(session, ship, shipClass),
            Templates.Get("Alert.Title"),
            alerts,
            Templates.Get("Alert.Resume"));
    }

    private static string Fleet(GameSession session, Ship ship, string shipClass)
    {
        SimulationContext context = session.Context;
        var facts = new List<(string Name, string Value)>
        {
            ("shipClass", shipClass),
            ("load", Format.Quantity(ship.HoldQuantity)),
            ("capacity", Format.Quantity(ship.CargoCapacity)),
            ("location", context.Location(ship.Location).Name),
        };

        if (ship.Job is not { } job)
        {
            return Templates.Fill("Fleet.Docked", facts.ToArray());
        }

        facts.Add(("good", Templates.Good(job.Good)));
        facts.Add(("origin", context.Location(job.Pickup).Name));
        facts.Add(("destination", context.Location(job.Destination ?? job.Pickup).Name));
        facts.Add(("when", Format.Date(ship.Course?.ArrivalTick ?? session.Now)));
        if (ship.Location == job.Pickup && ship.Course is not null)
        {
            return Templates.Fill("Fleet.Laden", facts.ToArray());
        }

        if (job.Destination is null)
        {
            return Templates.Fill(ship.Course is null ? "Fleet.Loading.Collect" : "Fleet.Collecting", facts.ToArray());
        }

        return Templates.Fill(ship.Course is null ? "Fleet.Loading" : "Fleet.Repositioning", facts.ToArray());
    }
}
