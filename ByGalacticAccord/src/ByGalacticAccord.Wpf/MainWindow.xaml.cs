using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Engine.Simulation;
using Visibility = System.Windows.Visibility;
using EngineVisibility = ByGalacticAccord.Engine.Economy.Visibility;

namespace ByGalacticAccord.Wpf;

/// <summary>
/// The live 2D view of the simulation. It owns a <see cref="SimulationContext"/>, advances it on a
/// frame timer at the chosen speed (§3.4 time control), and renders the location graph with ships
/// moving along their routes, prices and idle-producer tells on the map, the contract board, the
/// player's in-flight hauls, and a live event log.
/// </summary>
public partial class MainWindow : Window
{
    private readonly SimulationContext _ctx;
    private readonly ActorState _player;
    private readonly System.Windows.Threading.DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();

    private double _displayTick;
    private double _ticksPerSecond;       // 0 = paused
    private long _lastFrameMs;
    private int _frame;

    private readonly Dictionary<LocationId, Point> _nodePos = new();
    private readonly List<(ShipId Ship, Point At)> _shipPos = new();
    private LocationId? _selectedLocation;
    private ShipId? _selectedShip;
    private ContractId? _selectedContract;

    public MainWindow(long seed, bool autoRun)
    {
        InitializeComponent();

        WorldHandles handles = WorldGenerator.Generate(seed, withPlayer: true);
        _ctx = handles.Context;
        _player = _ctx.Actor(handles.Player!.Value);
        _selectedLocation = _player.Home;

        _timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33),
        };
        _timer.Tick += OnFrame;

        Loaded += (_, _) =>
        {
            DrawMap();
            RefreshPanels();
            if (autoRun)
            {
                _ticksPerSecond = 6;
                SpeedText.Text = "1× (6 ticks/s)";
                _clock.Start();
                _lastFrameMs = _clock.ElapsedMilliseconds;
                _timer.Start();
            }
        };
    }

    // ---------------------------------------------------------------- frame loop (§3.4)

    private void OnFrame(object? sender, EventArgs e)
    {
        long now = _clock.ElapsedMilliseconds;
        double dt = Math.Min(0.25, (now - _lastFrameMs) / 1000.0);
        _lastFrameMs = now;

        if (_ticksPerSecond > 0)
        {
            _displayTick += _ticksPerSecond * dt;
            RunStop stop = _ctx.RunTo((long)_displayTick);
            if (stop == RunStop.PlayerAlert || _ctx.HasPlayerAlerts)
            {
                ShowAlerts();
            }
        }

        DrawMap();
        if (_frame++ % 6 == 0)
        {
            RefreshPanels();
        }
    }

    private void ShowAlerts()
    {
        AlertText.Text = string.Join("\n", _ctx.PlayerAlerts);
        AlertBanner.Visibility = Visibility.Visible;
        _ctx.ClearPlayerAlerts();
        SetSpeed(0, "paused (alert)");
    }

    // ---------------------------------------------------------------- speed control

    private void OnPause(object sender, RoutedEventArgs e) => SetSpeed(0, "paused");

    private void OnPlay(object sender, RoutedEventArgs e)
    {
        AlertBanner.Visibility = Visibility.Collapsed;
        SetSpeed(6, "1× (6 ticks/s)");
    }

    private void OnFast(object sender, RoutedEventArgs e)
    {
        AlertBanner.Visibility = Visibility.Collapsed;
        SetSpeed(40, "fast (40 ticks/s)");
    }

    private void SetSpeed(double ticksPerSecond, string label)
    {
        _ticksPerSecond = ticksPerSecond;
        SpeedText.Text = label;
        if (!_clock.IsRunning)
        {
            _clock.Start();
        }

        _lastFrameMs = _clock.ElapsedMilliseconds;
        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }
    }

    // ---------------------------------------------------------------- map rendering

    private void DrawMap()
    {
        double w = MapCanvas.ActualWidth > 0 ? MapCanvas.ActualWidth : MapCanvas.Width;
        double h = MapCanvas.ActualHeight > 0 ? MapCanvas.ActualHeight : MapCanvas.Height;
        if (double.IsNaN(w) || w < 50 || double.IsNaN(h) || h < 50)
        {
            return;
        }

        MapCanvas.Children.Clear();
        _nodePos.Clear();
        _shipPos.Clear();

        const double margin = 70;
        foreach (Location loc in _ctx.Locations.Values)
        {
            _nodePos[loc.Id] = MapLayout.Of(loc.Name, w, h, margin);
        }

        DrawRoutes();
        DrawNodes();
        DrawShips();
    }

    private void DrawRoutes()
    {
        foreach (Route r in _ctx.Routes)
        {
            Point a = _nodePos[r.A];
            Point b = _nodePos[r.B];
            var line = new Line
            {
                X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y,
                Stroke = MapLayout.DangerBrush(r.Band),
                StrokeThickness = r.Band == DangerBand.Red ? 2.4 : 1.6,
                Opacity = 0.75,
            };
            if (r.Band == DangerBand.Red)
            {
                line.StrokeDashArray = new DoubleCollection { 4, 3 };
            }

            MapCanvas.Children.Add(line);
        }
    }

    private void DrawNodes()
    {
        foreach (Location loc in _ctx.Locations.Values)
        {
            Point p = _nodePos[loc.Id];
            bool selected = _selectedLocation == loc.Id;
            bool isHome = loc.Id == _player.Home;
            double radius = loc.Type == LocationType.CoreWorld ? 13 : 11;

            if (selected)
            {
                AddCircle(p, radius + 6, null, new SolidColorBrush(MapLayout.Accent), 2);
            }

            AddCircle(p, radius, MapLayout.LocationBrush(loc.Type),
                new SolidColorBrush(isHome ? MapLayout.Accent : MapLayout.Panel), isHome ? 2.5 : 1.5);

            AddText(loc.Name, p.X, p.Y - radius - 16, MapLayout.Ink, 12.5, bold: true, center: true);
            AddText(MapLayout.ShortType(loc.Type), p.X, p.Y - radius - 2, MapLayout.Muted, 10, bold: false, center: true);

            DrawNodePrices(loc, p, radius);
        }
    }

    private void DrawNodePrices(Location loc, Point p, double radius)
    {
        double y = p.Y + radius + 4;
        foreach (ActorId id in loc.Residents)
        {
            ActorState a = _ctx.Actor(id);
            if (a.Producer is not { } prof)
            {
                continue;
            }

            decimal stock = a.Stock(prof.Good);
            Credits ask = PriceModel.ProducerAsk(prof, stock);
            bool idle = prof.IsIdle(stock);
            string text = $"{Abbrev(prof.Good)} ₡{ask.Amount:0}{(idle ? "  ▲idle" : string.Empty)}";
            AddText(text, p.X, y, idle ? MapLayout.Gold : MapLayout.Muted, 10.5, bold: false, center: true);
            y += 13;
        }
    }

    private void DrawShips()
    {
        // Spread docked ships a little around their node so they don't stack.
        var dockedCount = new Dictionary<LocationId, int>();

        foreach (Ship ship in _ctx.Ships.Values)
        {
            bool isPlayer = ship.Owner == _player.Id;
            Point at;
            Cargo? cargo = ship.Manifest;

            if (ship.Status == ShipStatus.InTransit && cargo is { } c && ship.ActiveContract is { } cid)
            {
                Contract contract = _ctx.Contract(cid);
                RoutePlan? plan = _ctx.FindPath(contract.OriginLocationId, contract.DestinationLocationId);
                if (plan is null || plan.DistanceTicks <= 0)
                {
                    continue;
                }

                long travel = Math.Max(1, PriceModel.TravelTicks(plan.DistanceTicks, ship.Class.SpeedMultiplier));
                double f = Math.Clamp((_displayTick - ship.DepartedTick) / travel, 0, 1);
                at = PointAlongPath(plan.Path, f);
                DrawShipMarker(ship, at, isPlayer, $"{c.Quantity:0} {Abbrev(c.Good)}");
            }
            else
            {
                int n = dockedCount.TryGetValue(ship.Location, out int v) ? v : 0;
                dockedCount[ship.Location] = n + 1;
                Point node = _nodePos[ship.Location];
                // Park docked ships to the right of the node, clear of the price labels below it.
                at = new Point(node.X + 20 + ((n % 4) * 12), node.Y - 2 + ((n / 4) * 12));
                DrawShipMarker(ship, at, isPlayer, label: null);
            }

            _shipPos.Add((ship.Id, at));
        }
    }

    private void DrawShipMarker(Ship ship, Point at, bool isPlayer, string? label)
    {
        bool selected = _selectedShip == ship.Id;
        double r = isPlayer ? 7 : 5;
        Brush fill = isPlayer ? new SolidColorBrush(MapLayout.Accent) : new SolidColorBrush(MapLayout.Ink);

        if (selected)
        {
            AddCircle(at, r + 4, null, new SolidColorBrush(MapLayout.Gold), 2);
        }

        AddCircle(at, r, fill, new SolidColorBrush(MapLayout.Background), 1.5);
        if (isPlayer)
        {
            AddCircle(at, r + 3, null, new SolidColorBrush(MapLayout.Accent), 1.5);
        }

        if (label is not null)
        {
            AddText(label, at.X, at.Y - r - 13, isPlayer ? MapLayout.Accent : MapLayout.Muted, 10, bold: isPlayer, center: true);
        }
    }

    private Point PointAlongPath(IReadOnlyList<LocationId> path, double f)
    {
        if (path.Count == 1)
        {
            return _nodePos[path[0]];
        }

        // Walk the polyline using each leg's tick-distance so motion matches travel time.
        var legLen = new double[path.Count - 1];
        double total = 0;
        for (int i = 0; i < path.Count - 1; i++)
        {
            Route? route = _ctx.RouteBetween(path[i], path[i + 1]);
            legLen[i] = route?.DistanceTicks ?? 1;
            total += legLen[i];
        }

        double target = f * total;
        double acc = 0;
        for (int i = 0; i < legLen.Length; i++)
        {
            if (acc + legLen[i] >= target || i == legLen.Length - 1)
            {
                double segFrac = legLen[i] <= 0 ? 0 : (target - acc) / legLen[i];
                Point a = _nodePos[path[i]];
                Point b = _nodePos[path[i + 1]];
                return new Point(a.X + ((b.X - a.X) * segFrac), a.Y + ((b.Y - a.Y) * segFrac));
            }

            acc += legLen[i];
        }

        return _nodePos[path[^1]];
    }

    // ---------------------------------------------------------------- canvas helpers

    private void AddCircle(Point center, double radius, Brush? fill, Brush stroke, double strokeThickness)
    {
        var e = new Ellipse
        {
            Width = radius * 2, Height = radius * 2,
            Fill = fill, Stroke = stroke, StrokeThickness = strokeThickness,
        };
        Canvas.SetLeft(e, center.X - radius);
        Canvas.SetTop(e, center.Y - radius);
        MapCanvas.Children.Add(e);
    }

    private void AddText(string text, double x, double y, Color color, double size, bool bold, bool center)
    {
        var tb = new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(color),
            FontSize = size,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        };
        if (center)
        {
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            x -= tb.DesiredSize.Width / 2;
        }

        Canvas.SetLeft(tb, x);
        Canvas.SetTop(tb, y);
        MapCanvas.Children.Add(tb);
    }

    // ---------------------------------------------------------------- panels

    private void RefreshPanels()
    {
        TickText.Text = $"tick {_ctx.CurrentTick:N0}";
        CreditsText.Text = _player.Credits.ToString();

        RefreshBoard();
        RefreshHauls();
        RefreshLog();
        RefreshDetail();
    }

    private void RefreshBoard()
    {
        var rows = new List<BoardRow>();
        foreach (Contract c in EngineVisibility.OpenContractsVisibleTo(_ctx, _player)
                     .Where(c => c.Type == ContractType.Haul && c.Status == ContractStatus.Open)
                     .OrderBy(c => _ctx.FindPath(c.OriginLocationId, c.DestinationLocationId)?.MaxDanger ?? 0m)
                     .ThenByDescending(c => c.OfferedPayment.Amount))
        {
            RoutePlan? plan = _ctx.FindPath(c.OriginLocationId, c.DestinationLocationId);
            rows.Add(new BoardRow
            {
                Id = c.Id,
                Danger = plan is null ? new SolidColorBrush(MapLayout.Muted) : MapLayout.DangerBrush(plan.Band),
                Good = $"{c.Quantity:0} {c.Good}",
                Route = $"{_ctx.Location(c.OriginLocationId).Name} → {_ctx.Location(c.DestinationLocationId).Name}",
                Law = Reach(c.Jurisdiction),
                Fee = $"≤{c.BuyerReservationFee}",
                Deadline = $"due t{c.DeadlineTick}",
            });
        }

        BoardList.ItemsSource = rows;
        if (_selectedContract is { } sel)
        {
            BoardList.SelectedItem = rows.FirstOrDefault(r => r.Id == sel);
        }
    }

    private void RefreshHauls()
    {
        var rows = new List<HaulRow>();
        foreach (Contract c in _ctx.Contracts.Values
                     .Where(c => c.CounterpartyId == _player.Id && c.Status == ContractStatus.Active)
                     .OrderBy(c => c.DeadlineTick))
        {
            double progress = 0;
            string detail;
            if (c.AssignedShip is { } sid)
            {
                Ship ship = _ctx.Ship(sid);
                RoutePlan? plan = _ctx.FindPath(c.OriginLocationId, c.DestinationLocationId);
                if (ship.Manifest is not null && plan is not null && plan.DistanceTicks > 0)
                {
                    long travel = Math.Max(1, PriceModel.TravelTicks(plan.DistanceTicks, ship.Class.SpeedMultiplier));
                    progress = Math.Clamp((_displayTick - ship.DepartedTick) / travel, 0, 1);
                    detail = $"{ship.Class.Name} · {progress * 100:0}% · due t{c.DeadlineTick}";
                }
                else
                {
                    detail = $"{ship.Class.Name} · repositioning · due t{c.DeadlineTick}";
                }
            }
            else
            {
                detail = "awaiting ship";
            }

            rows.Add(new HaulRow
            {
                Cargo = $"{c.Quantity:0} {c.Good}",
                Route = $"→ {_ctx.Location(c.DestinationLocationId).Name}",
                Detail = detail,
                Progress = progress,
            });
        }

        HaulsList.ItemsSource = rows;
    }

    private void RefreshLog()
    {
        var rows = new List<LogRow>();
        foreach (JournalEntry entry in _ctx.Journal.Tail(60))
        {
            rows.Add(new LogRow { Color = new SolidColorBrush(CategoryColor(entry.Category)), Text = $"t{entry.Tick,-6} {entry.Text}" });
        }

        LogList.ItemsSource = rows;
        if (rows.Count > 0)
        {
            LogList.ScrollIntoView(rows[^1]);
        }
    }

    private void RefreshDetail()
    {
        if (_selectedShip is { } sid && _ctx.Ships.TryGetValue(sid, out Ship? ship))
        {
            DetailText.Text = DescribeShip(ship);
        }
        else if (_selectedLocation is { } lid)
        {
            DetailText.Text = DescribeLocation(_ctx.Location(lid));
        }
    }

    private string DescribeLocation(Location loc)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{loc.Name}  ({MapLayout.ShortType(loc.Type)})");
        sb.AppendLine($"{Reach(loc.Reach)} · region {loc.Region}");
        sb.AppendLine();
        foreach (ActorId id in loc.Residents)
        {
            ActorState a = _ctx.Actor(id);
            if (a.Producer is { } prof)
            {
                decimal stock = a.Stock(prof.Good);
                string idle = prof.IsIdle(stock) ? "  [idle → price soft]" : string.Empty;
                sb.AppendLine($"sells {prof.Good} @ ₡{PriceModel.ProducerAsk(prof, stock).Amount:0}/u  ({stock:0}/{prof.StockCap:0}){idle}");
            }

            if (a.Demands.Count > 0)
            {
                foreach (ConsumerDemand d in a.Demands)
                {
                    sb.AppendLine($"wants {d.Good}  ({a.Stock(d.Good):0}/{d.TargetStock:0})");
                }
            }
        }

        return sb.ToString().TrimEnd();
    }

    private string DescribeShip(Ship ship)
    {
        var sb = new System.Text.StringBuilder();
        ActorState owner = _ctx.Actor(ship.Owner);
        sb.AppendLine($"{ship.Class.Name}  [{ship.Id}]");
        sb.AppendLine($"owner: {owner.Name}{(ship.Owner == _player.Id ? "  (you)" : string.Empty)}");
        sb.AppendLine($"status: {ship.Status}");
        if (ship.Manifest is { } m)
        {
            sb.AppendLine($"hauling: {m.Quantity:0} {m.Good} (quality {m.Quality * 100:0}%)");
        }

        if (ship.ActiveContract is { } cid && _ctx.Contracts.TryGetValue(cid, out Contract? c))
        {
            sb.AppendLine($"to {_ctx.Location(c.DestinationLocationId).Name} · fee {c.Fee} · due t{c.DeadlineTick}");
        }

        return sb.ToString().TrimEnd();
    }

    // ---------------------------------------------------------------- interaction

    private void OnMapClick(object sender, MouseButtonEventArgs e)
    {
        Point click = e.GetPosition(MapCanvas);

        foreach ((ShipId ship, Point at) in _shipPos)
        {
            if (Dist(click, at) <= 12)
            {
                _selectedShip = ship;
                _selectedLocation = null;
                RefreshDetail();
                DrawMap();
                return;
            }
        }

        LocationId? nearest = null;
        double best = 26;
        foreach ((LocationId id, Point at) in _nodePos)
        {
            double d = Dist(click, at);
            if (d < best)
            {
                best = d;
                nearest = id;
            }
        }

        if (nearest is { } n)
        {
            _selectedLocation = n;
            _selectedShip = null;
            RefreshDetail();
            DrawMap();
        }
    }

    private void OnNegotiate(object sender, RoutedEventArgs e)
    {
        if (BoardList.SelectedItem is not BoardRow row)
        {
            return;
        }

        _selectedContract = row.Id;
        if (!_ctx.Contracts.TryGetValue(row.Id, out Contract? contract) || contract.Status != ContractStatus.Open)
        {
            return;
        }

        PlayerActions.HaulFeasibility feas = PlayerActions.CheckHaul(_ctx, _player, contract);
        bool wasRunning = _ticksPerSecond > 0;
        SetSpeed(0, "paused (negotiating)");

        var dialog = new NegotiationDialog(_ctx, _player, contract, feas) { Owner = this };
        dialog.ShowDialog();

        RefreshPanels();
        DrawMap();
        if (wasRunning)
        {
            SetSpeed(6, "1× (6 ticks/s)");
        }
    }

    // ---------------------------------------------------------------- headless screenshot

    /// <summary>Render a single still to PNG without showing the window (for screenshots / verification).</summary>
    public void RenderToPng(string path, int width, int height, long warmupTicks)
    {
        _displayTick = warmupTicks;
        _ctx.RunTo(warmupTicks);

        // Detach the content and lay it out standalone: a never-shown Window has no presentation
        // source, so rendering it directly captures nothing. Rendering the arranged root does work.
        FrameworkElement root = RootGrid;
        Content = null;
        root.Width = width;
        root.Height = height;
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();

        DrawMap();
        RefreshPanels();
        root.UpdateLayout();

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using FileStream fs = File.Create(path);
        encoder.Save(fs);
    }

    // ---------------------------------------------------------------- small helpers

    private static double Dist(Point a, Point b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

    private static string Abbrev(GoodType good) => good switch
    {
        GoodType.Machinery => "Mach",
        GoodType.Medicine => "Med",
        GoodType.Luxury => "Lux",
        _ => good.ToString(),
    };

    private static string Reach(AccordReach reach) => reach switch
    {
        AccordReach.DeepAccord => "core (deep Accord)",
        AccordReach.AccordEdge => "mid (Accord edge)",
        AccordReach.OutsideAccord => "frontier (no escrow)",
        _ => "black market",
    };

    private static Color CategoryColor(string category) => category switch
    {
        "Fulfilled" => Color.FromRgb(0x6F, 0xC0, 0x7A),
        "Breach" => Color.FromRgb(0xD9, 0x4F, 0x4F),
        "Incident" => Color.FromRgb(0xF2, 0xC8, 0x5E),
        "Accepted" => Color.FromRgb(0x5E, 0xC8, 0xF2),
        "Posted" => Color.FromRgb(0x8A, 0x97, 0xAD),
        "Inflation" => Color.FromRgb(0xB0, 0x8A, 0xE0),
        _ => Color.FromRgb(0xC4, 0xCE, 0xDC),
    };
}
