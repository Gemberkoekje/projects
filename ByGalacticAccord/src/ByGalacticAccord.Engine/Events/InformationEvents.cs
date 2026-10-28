using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Engine.Events;

/// <summary>
/// The morning price bulletin (design §7.1): at <see cref="PriceBulletin.Hour"/> every producer's ask is posted, and every
/// actor reads what it can: live where it is present, the posted asks one jump away (<see cref="Knowledge.ReadBulletin"/>).
/// </summary>
public sealed class PricesPosted : ISimEvent
{
    public PricesPosted(long tick) => Tick = tick;

    public long Tick { get; }

    public string Describe(SimulationContext context) => "The morning price bulletin.";

    public void Process(SimulationContext context)
    {
        context.Bulletin.Post(context);
        Knowledge.ReadBulletin(context);
        context.Schedule(new PricesPosted(Tick + GameTime.HoursPerDay));
    }
}

/// <summary>
/// The Daily Accord goes to press (design §7.4): right after the bulletin, it reports a handful of yesterday's contracts,
/// drawn by weighted chance (<see cref="NewsDesk.Publish"/>). With no items a day configured, it prints nothing.
/// </summary>
public sealed class NewsPublished : ISimEvent
{
    public NewsPublished(long tick) => Tick = tick;

    public long Tick { get; }

    public string Describe(SimulationContext context) => "The Daily Accord.";

    public void Process(SimulationContext context)
    {
        if (context.Config.NewsItemsPerDay > 0)
        {
            context.News.Publish(context);
        }

        context.Schedule(new NewsPublished(Tick + GameTime.HoursPerDay));
    }
}
