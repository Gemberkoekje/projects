using System;
using System.Collections.Generic;
using System.Linq;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;
using ByGalacticAccord.Presentation.Session;
using ByGalacticAccord.Presentation.Text;

namespace ByGalacticAccord.Presentation.Views;

/// <summary>Which way a label's text runs from its anchor point.</summary>
public enum LabelAnchor
{
    Start,
    Middle,
    End,
}

/// <summary>
/// A location on the map, with the facts its label shows. The label sits above the location when more of
/// its routes leave downwards, and hugs the frame's edge near one.
/// </summary>
public sealed record MapNode(
    LocationId Id, string Name, int X, int Y, int Radius, string Meta, string Makes, bool Here, string Tip, bool LabelAbove, LabelAnchor Anchor, int LabelX);

/// <summary>A route, styled by its danger band; <see cref="Dimmed"/> when the selected contract's overlay covers it.</summary>
public sealed record MapRoute(int X1, int Y1, int X2, int Y2, DangerBand Band, string Hours, int LabelX, int LabelY, bool Dimmed, string Tip);

/// <summary>One leg of the selected contract's path: the empty leg to the pickup, or a laden leg by danger.</summary>
public sealed record MapLeg(int X1, int Y1, int X2, int Y2, DangerBand Band, bool Empty);

/// <summary>A region's label (with a second line saying it lies outside the Accord, if so), and its frame then.</summary>
public sealed record MapRegion(string Label, string? Note, int X, int Y, int Width, int Height, bool Framed);

/// <summary>A ship at its interpolated position.</summary>
public sealed record MapShip(ShipId Id, double X, double Y, bool Yours, string Tip);

public sealed record MapLegendItem(DangerBand Band, bool Empty, string Label);

public sealed record MapViewModel(
    int Width,
    int Height,
    string Title,
    string Hint,
    IReadOnlyList<MapRegion> Regions,
    IReadOnlyList<MapRoute> Routes,
    IReadOnlyList<MapLeg> Overlay,
    IReadOnlyList<MapNode> Nodes,
    IReadOnlyList<MapLegendItem> Legend,
    string? OverlayTitle,
    string? OverlayEta,
    Tone OverlayTone,
    string Description);

/// <summary>
/// The schematic sector map (design §11.1, §11.3): locations at their map points, routes coloured and
/// dashed by danger, ships moving along their courses, and the selected contract's path drawn over it.
/// </summary>
public static class MapView
{
    public const int Width = 860;
    public const int Height = 540;

    private const int RegionPadding = 12;
    private const int RegionTitle = 32;

    // Within this many map units of the frame's side, a location's label runs inwards instead of centring.
    private const int LabelEdge = 110;

    public static MapViewModel Build(GameSession session, ContractId? selected)
    {
        SimulationContext context = session.Context;
        Ship yours = session.PlayerShip;
        LocationId? here = yours.Status == ShipStatus.Docked ? yours.Location : null;

        var overlay = new List<MapLeg>();
        string? overlayTitle = null;
        string? overlayEta = null;
        Tone overlayTone = Tone.Muted;
        var covered = new HashSet<(LocationId, LocationId)>();
        if (selected is { } id && context.Contracts.TryGetValue(id, out Contract? contract))
        {
            (overlayTitle, overlayEta, overlayTone) = Overlay(session, contract, overlay, covered);
        }

        var routes = new List<MapRoute>();
        foreach (Route route in context.Routes)
        {
            MapPoint a = context.Location(route.A).Map;
            MapPoint b = context.Location(route.B).Map;
            (int lx, int ly) = LabelPoint(a, b);
            string hours = Templates.Fill("Map.Hours", ("hours", Format.Hours(route.DistanceTicks)));
            string tip = Templates.Fill(
                "Map.Route.Tip",
                ("a", context.Location(route.A).Name), ("b", context.Location(route.B).Name),
                ("hours", Format.Hours(route.DistanceTicks)), ("band", Templates.Danger(route.Band)), ("danger", Format.Percent(route.DangerRating)));
            routes.Add(new MapRoute(a.X, a.Y, b.X, b.Y, route.Band, hours, lx, ly, covered.Contains((route.A, route.B)), tip));
        }

        var nodes = new List<MapNode>();
        foreach (Location location in context.Locations.Values.OrderBy(l => l.Id.Value))
        {
            nodes.Add(Node(context, location, here == location.Id));
        }

        List<MapRegion> regions = Regions(context, nodes);

        var legend = new List<MapLegendItem>
        {
            new(DangerBand.Green, false, Templates.Danger(DangerBand.Green)),
            new(DangerBand.Amber, false, Templates.Danger(DangerBand.Amber)),
            new(DangerBand.Red, false, Templates.Danger(DangerBand.Red)),
            new(DangerBand.Green, true, Templates.Get("Map.Legend.Empty")),
        };

        return new MapViewModel(
            Width, Height, Templates.Get("Map.Title"), Templates.Get("Map.Hint"),
            regions, routes, overlay, nodes, legend, overlayTitle, overlayEta, overlayTone,
            Templates.Get("Map.Description"));
    }

    /// <summary>Every ship at <paramref name="clock"/>: docked ships beside their location, moving ones along their course.</summary>
    public static IReadOnlyList<MapShip> Ships(GameSession session, double clock)
    {
        SimulationContext context = session.Context;
        var ships = new List<MapShip>();
        var dockedAt = new Dictionary<LocationId, int>();
        foreach (Ship ship in context.Ships.Values.OrderBy(s => s.Id.Value))
        {
            bool yours = ship.Owner == session.Player.Id;
            string owner = context.Actor(ship.Owner).Name;
            string shipClass = Templates.ShipClass(ship.Type);
            if (ship.Course is { } course)
            {
                (double x, double y) = Along(context, course, clock);
                string tip = Templates.Fill(
                    "Map.Ship.Moving",
                    ("owner", owner), ("shipClass", shipClass),
                    ("from", context.Location(course.From).Name), ("to", context.Location(course.To).Name), ("when", Format.Date(course.ArrivalTick)));
                ships.Add(new MapShip(ship.Id, x, y, yours, tip));
            }
            else
            {
                // Docked, or loading at its pickup this hour: a small ring of ships around the location, on the
                // side away from its label.
                int index = dockedAt.GetValueOrDefault(ship.Location);
                dockedAt[ship.Location] = index + 1;
                Location at = context.Location(ship.Location);
                MapPoint p = at.Map;
                double start = LabelAbove(context, at) ? 90 : -90;
                double angle = (start + (index * 40)) * Math.PI / 180.0;
                const double ring = 22;
                string tip = Templates.Fill(
                    "Map.Ship.Docked",
                    ("owner", owner), ("shipClass", shipClass), ("location", context.Location(ship.Location).Name));
                ships.Add(new MapShip(ship.Id, p.X + (Math.Cos(angle) * ring), p.Y + (Math.Sin(angle) * ring), yours, tip));
            }
        }

        return ships;
    }

    /// <summary>Where a ship on <paramref name="course"/> is at <paramref name="clock"/>, by share of the path's hours.</summary>
    public static (double X, double Y) Along(SimulationContext context, ShipCourse course, double clock)
    {
        IReadOnlyList<LocationId> path = course.Path;
        MapPoint start = context.Location(path[0]).Map;
        if (path.Count < 2)
        {
            return (start.X, start.Y);
        }

        double span = Math.Max(1, course.ArrivalTick - course.DepartedTick);
        double share = Math.Clamp((clock - course.DepartedTick) / span, 0, 1);

        double total = 0;
        var legs = new double[path.Count - 1];
        for (int i = 0; i < legs.Length; i++)
        {
            legs[i] = context.RouteBetween(path[i], path[i + 1])?.DistanceTicks ?? 1;
            total += legs[i];
        }

        double distance = share * total;
        for (int i = 0; i < legs.Length; i++)
        {
            if (distance <= legs[i] || i == legs.Length - 1)
            {
                double t = legs[i] <= 0 ? 1 : Math.Clamp(distance / legs[i], 0, 1);
                MapPoint a = context.Location(path[i]).Map;
                MapPoint b = context.Location(path[i + 1]).Map;
                return (a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t));
            }

            distance -= legs[i];
        }

        MapPoint end = context.Location(path[^1]).Map;
        return (end.X, end.Y);
    }

    private static (string? Title, string? Eta, Tone Tone) Overlay(
        GameSession session, Contract contract, List<MapLeg> overlay, HashSet<(LocationId, LocationId)> covered)
    {
        SimulationContext context = session.Context;
        string title = OverlayTitle(context, contract);
        Ship ship = session.PlayerShip;
        long arrival;
        bool collect = contract is SaleContract { Side: SaleSide.ForSale };

        if (contract.Status == ContractStatus.Active && ship.Job is { } job && job.Serves(contract.Id))
        {
            // Your own job: what is left of it, from the ship's current leg.
            bool laden = ship.Location == job.Pickup;
            if (ship.Course is { } course)
            {
                AddLegs(context, course.Path, empty: !laden, overlay, covered);
            }

            if ((!laden || ship.Course is null) && job.Destination is { } destination && context.FindPath(job.Pickup, destination) is { } rest)
            {
                AddLegs(context, rest.Path, empty: false, overlay, covered);
            }

            arrival = Market.NextFree(context, ship).Tick;
            collect = job.Destination is null;
        }
        else if (contract.IsOpen && DealEstimate.For(context, session.Player, ship, contract) is { } estimate)
        {
            if (estimate.Repositioning is { } repo)
            {
                AddLegs(context, repo.Path, empty: true, overlay, covered);
            }

            if (estimate.Laden is { } laden)
            {
                AddLegs(context, laden.Path, empty: false, overlay, covered);
            }

            arrival = estimate.Kind == DealKind.Buy ? estimate.PickupTick : estimate.ArrivalTick;
        }
        else
        {
            if (contract is HaulContract haul && context.FindPath(haul.Origin, haul.Destination) is { } path)
            {
                AddLegs(context, path.Path, empty: false, overlay, covered);
            }

            return (title, null, Tone.Muted);
        }

        if (collect)
        {
            string at = Templates.Fill("Map.Overlay.Collect", ("arrive", Format.Date(arrival)), ("deadline", Format.Date(contract.DeadlineTick)));
            return (title, at, arrival > contract.DeadlineTick ? Tone.Loss : Tone.Muted);
        }

        bool late = arrival > contract.DeadlineTick;
        string eta = late
            ? Templates.Fill("Map.Overlay.Late", ("arrive", Format.Date(arrival)), ("deadline", Format.Date(contract.DeadlineTick)), ("late", Format.Duration(arrival - contract.DeadlineTick)))
            : Templates.Fill("Map.Overlay.Spare", ("arrive", Format.Date(arrival)), ("deadline", Format.Date(contract.DeadlineTick)), ("spare", Format.Duration(contract.DeadlineTick - arrival)));
        return (title, eta, late ? Tone.Loss : Tone.Muted);
    }

    private static string OverlayTitle(SimulationContext context, Contract contract) => contract switch
    {
        HaulContract haul => Templates.Fill(
            "Map.Overlay.Title.Haul",
            ("quantity", Format.Quantity(haul.Quantity)), ("good", Templates.Good(haul.Good)),
            ("origin", context.Location(haul.Origin).Name), ("destination", context.Location(haul.Destination).Name)),
        SaleContract sale => Templates.Fill(
            "Map.Overlay.Title.Sale." + sale.Side,
            ("quantity", Format.Quantity(sale.Quantity)), ("good", Templates.Good(sale.Good)), ("place", context.Location(sale.Place).Name)),
        _ => string.Empty,
    };

    private static void AddLegs(SimulationContext context, IReadOnlyList<LocationId> path, bool empty, List<MapLeg> overlay, HashSet<(LocationId, LocationId)> covered)
    {
        for (int i = 0; i + 1 < path.Count; i++)
        {
            Route? route = context.RouteBetween(path[i], path[i + 1]);
            if (route is null)
            {
                continue;
            }

            MapPoint a = context.Location(path[i]).Map;
            MapPoint b = context.Location(path[i + 1]).Map;
            overlay.Add(new MapLeg(a.X, a.Y, b.X, b.Y, route.Band, empty));
            covered.Add((route.A, route.B));
        }
    }

    private static MapNode Node(SimulationContext context, Location location, bool here)
    {
        var made = new List<string>();
        foreach (ActorId resident in location.Residents)
        {
            if (context.Actor(resident).Producer is { } producer)
            {
                string good = Templates.Good(producer.Good);
                if (!made.Contains(good))
                {
                    made.Add(good);
                }
            }
        }

        string meta = Templates.Fill(
            "Map.Node.Meta", ("type", Templates.LocationKind(location.Type)), ("jurisdiction", Templates.JurisdictionShort(location.Reach)));
        string makes = made.Count == 0 ? string.Empty : Templates.Fill("Map.Node.Makes", ("goods", string.Join(", ", made)));
        string tip = Templates.Fill(here ? "Map.Node.Tip.Here" : "Map.Node.Tip", ("name", location.Name), ("meta", meta));
        int radius = location.Type == LocationType.CoreWorld ? 12 : 9;

        (LabelAnchor anchor, int labelX) = location.Map.X > Width - LabelEdge
            ? (LabelAnchor.End, location.Map.X + radius + 4)
            : location.Map.X < LabelEdge ? (LabelAnchor.Start, location.Map.X - radius - 4) : (LabelAnchor.Middle, location.Map.X);
        return new MapNode(location.Id, location.Name, location.Map.X, location.Map.Y, radius, meta, makes, here, tip, LabelAbove(context, location), anchor, labelX);
    }

    // A label goes on the side of its location that fewer routes leave from: above when most go down.
    private static bool LabelAbove(SimulationContext context, Location location)
    {
        int up = 0;
        int down = 0;
        foreach (Route route in context.Routes)
        {
            if (route.A == location.Id || route.B == location.Id)
            {
                if (context.Location(route.Other(location.Id)).Map.Y < location.Map.Y)
                {
                    up++;
                }
                else
                {
                    down++;
                }
            }
        }

        return down > up;
    }

    private static List<MapRegion> Regions(SimulationContext context, List<MapNode> nodes)
    {
        var regions = new List<MapRegion>();
        foreach (IGrouping<string, Location> group in context.Locations.Values.OrderBy(l => l.Id.Value).GroupBy(l => l.Region))
        {
            bool outside = group.All(l => l.Reach is AccordReach.OutsideAccord or AccordReach.BlackMarket);
            if (!outside)
            {
                regions.Add(new MapRegion(Templates.Fill("Map.Region.Accord", ("region", group.Key)), null, 14, 12, 0, 0, false));
                continue;
            }

            // Frame the region's locations and their labels, with room on top for the region's own two lines.
            double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
            foreach (Location location in group)
            {
                MapNode node = nodes.First(n => n.Id == location.Id);
                (double l, double t, double r, double b) = Extent(node);
                left = Math.Min(left, l);
                top = Math.Min(top, t);
                right = Math.Max(right, r);
                bottom = Math.Max(bottom, b);
            }

            int x = (int)Math.Max(8, left - RegionPadding);
            int y = (int)Math.Max(8, top - RegionPadding - RegionTitle);
            int w = (int)Math.Min(Width - 8, right + RegionPadding) - x;
            int h = (int)Math.Min(Height - 8, bottom + RegionPadding) - y;
            regions.Add(new MapRegion(
                Templates.Fill("Map.Region.Outside", ("region", group.Key)), Templates.Get("Map.Region.Outside.Note"), x, y, w, h, true));
        }

        return regions;
    }

    // Roughly where a location's dot and label lines reach, from character counts at the label sizes.
    private static (double Left, double Top, double Right, double Bottom) Extent(MapNode node)
    {
        double width = Math.Max(node.Name.Length * 7.4, Math.Max(node.Meta.Length * 6.0, node.Makes.Length * 6.6));
        (double left, double right) = node.Anchor switch
        {
            LabelAnchor.Start => (node.LabelX, node.LabelX + width),
            LabelAnchor.End => (node.LabelX - width, node.LabelX),
            _ => (node.LabelX - (width / 2), node.LabelX + (width / 2)),
        };
        double labelTop = node.LabelAbove ? node.Y - node.Radius - 10 - 28 - 11 : node.Y + node.Radius + 6;
        double labelBottom = node.LabelAbove ? node.Y - node.Radius - 6 : node.Y + node.Radius + 17 + 28 + 4;
        return (
            Math.Min(left, node.X - node.Radius),
            Math.Min(labelTop, node.Y - node.Radius),
            Math.Max(right, node.X + node.Radius),
            Math.Max(labelBottom, node.Y + node.Radius));
    }

    // A route's hours label sits beside its midpoint, offset perpendicular to the line.
    private static (int X, int Y) LabelPoint(MapPoint a, MapPoint b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double length = Math.Max(1, Math.Sqrt((dx * dx) + (dy * dy)));
        double x = ((a.X + b.X) / 2.0) - (dy / length * 12);
        double y = ((a.Y + b.Y) / 2.0) + (dx / length * 12);
        return ((int)Math.Round(x), (int)Math.Round(y));
    }
}
