using System.Collections.Generic;
using System.Linq;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Tests;

/// <summary>
/// The journal records structured entries (design §11.5), not prose, and each entry agrees with the state
/// it describes: the interface's text is only as true as these facts.
/// </summary>
public sealed class JournalTests
{
    [Theory]
    [InlineData(42L)]
    [InlineData(20260619L)]
    public void Every_settled_haul_has_one_entry_that_matches_its_contract(long seed)
    {
        // A small cap would drop early entries, so keep the run inside the journal's 5,000.
        SimulationContext context = TestWorld.RunHeadless(seed, 3_000);
        IReadOnlyList<JournalEntry> entries = context.Journal.Entries;
        Assert.Equal(context.Journal.TotalRecorded, entries.Count);

        foreach (Contract contract in context.Contracts.Values)
        {
            Assert.Single(entries.OfType<ContractPostedEntry>(), e => e.Contract == contract.Id);
            int taken = entries.OfType<HaulTakenEntry>().Count(e => e.Contract == contract.Id);
            int delivered = entries.OfType<HaulDeliveredEntry>().Count(e => e.Contract == contract.Id);
            int failed = entries.OfType<DeliveryFailedEntry>().Count(e => e.Contract == contract.Id)
                       + entries.OfType<PaymentDefaultedEntry>().Count(e => e.Contract == contract.Id);
            int expired = entries.OfType<ContractExpiredEntry>().Count(e => e.Contract == contract.Id);

            switch (contract.Status)
            {
                case ContractStatus.Fulfilled:
                    Assert.Equal((1, 1, 0, 0), (taken, delivered, failed, expired));
                    HaulDeliveredEntry entry = entries.OfType<HaulDeliveredEntry>().Single(e => e.Contract == contract.Id);
                    Assert.Equal(contract.CounterpartyId, entry.Hauler);
                    Assert.Equal(contract.Fee, entry.Fee);
                    break;
                case ContractStatus.Breached:
                    Assert.Equal((1, 0, 1, 0), (taken, delivered, failed, expired));
                    break;
                case ContractStatus.Expired:
                    Assert.Equal((0, 0, 0, 1), (taken, delivered, failed, expired));
                    break;
                case ContractStatus.Active:
                    Assert.Equal((1, 0, 0, 0), (taken, delivered, failed, expired));
                    break;
                default:
                    Assert.Equal((0, 0, 0, 0), (taken, delivered, failed, expired));
                    break;
            }
        }
    }

    [Fact]
    public void Posted_entries_name_the_poster_and_the_producer_at_the_origin()
    {
        SimulationContext context = TestWorld.RunHeadless(7L, 2_000);
        List<ContractPostedEntry> posted = context.Journal.Entries.OfType<ContractPostedEntry>().ToList();
        Assert.NotEmpty(posted);
        foreach (ContractPostedEntry e in posted)
        {
            Contract contract = context.Contract(e.Contract);
            Assert.Equal(contract.PostedBy, e.Poster);
            Assert.Equal(contract.OriginLocationId, context.Actor(e.Supplier).Home);
            Assert.Equal(contract.Good, context.Actor(e.Supplier).Producer!.Good);
        }
    }

    [Fact]
    public void Incident_entries_carry_the_resolved_incident()
    {
        SimulationContext context = TestWorld.RunHeadless(42L, 4_000);
        List<IncidentEntry> incidents = context.Journal.Entries.OfType<IncidentEntry>().ToList();
        Assert.NotEmpty(incidents);
        Assert.Equal(context.IncidentsOccurred, incidents.Count);
        Assert.All(incidents, e => Assert.Equal(context.Contract(e.Contract).CounterpartyId, e.Hauler));
    }

    [Fact]
    public void The_journal_keeps_its_cap_and_counts_everything()
    {
        var journal = new EventJournal(cap: 3);
        for (long t = 0; t < 5; t++)
        {
            journal.Record(new ContractExpiredEntry(t, new ContractId(t)));
        }

        Assert.Equal(5, journal.TotalRecorded);
        Assert.Equal(new long[] { 2, 3, 4 }, journal.Entries.Select(e => e.Tick));
        Assert.Equal(new long[] { 3, 4 }, journal.Tail(2).Select(e => e.Tick));
    }
}
