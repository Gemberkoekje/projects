using System.Collections.Generic;
using System.Linq;
using ByGalacticAccord.Engine.Actors;
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
    public void Every_contract_has_entries_that_match_how_it_ended(long seed)
    {
        // A small cap would drop early entries, so keep the run inside the journal's 5,000.
        SimulationContext context = TestWorld.RunHeadless(seed, 3_000);
        IReadOnlyList<JournalEntry> entries = context.Journal.Entries;
        Assert.Equal(context.Journal.TotalRecorded, entries.Count);
        ILookup<ContractId, JournalEntry> byContract = entries.OfType<IContractEntry>().ToLookup(e => e.Contract, e => (JournalEntry)e);

        foreach (Contract contract in context.Contracts.Values)
        {
            // Going into default is a step on the way, not an ending: a contract may pass through it and still be delivered.
            List<string> kinds = byContract[contract.Id]
                .Where(e => e is not IncidentEntry and not ContractInDefaultEntry)
                .Select(e => e.GetType().Name.Replace("Entry", string.Empty))
                .ToList();
            if (contract is StorageContract storage)
            {
                Assert.Equal(storage.Status == ContractStatus.Active ? new[] { "StorageOpened" } : new[] { "StorageOpened", "StorageClosed" }, kinds);
                continue;
            }

            string delivered = contract is HaulContract ? "HaulDelivered" : "GoodsHandedOver";
            string[] expected = contract.Status switch
            {
                ContractStatus.Open => new[] { "ContractPosted" },
                ContractStatus.Active => new[] { "ContractPosted", "ContractSigned" },
                ContractStatus.Fulfilled or ContractStatus.PartlyFulfilled => new[] { "ContractPosted", "ContractSigned", delivered },
                ContractStatus.Breached => new[] { "ContractPosted", "ContractSigned", "DeliveryFailed" },
                ContractStatus.Cancelled => new[] { "ContractPosted", "ContractSigned", "ContractCancelled" },
                ContractStatus.Defaulted => new[] { "ContractPosted", "ContractSigned", "PaymentDefaulted" },
                ContractStatus.Expired => new[] { "ContractPosted", "ContractExpired" },
                ContractStatus.Withdrawn => new[] { "ContractPosted", "ContractWithdrawn" },
                _ => new[] { "unexpected status " + contract.Status },
            };

            Assert.Equal(expected, kinds);
            int defaults = byContract[contract.Id].Count(e => e is ContractInDefaultEntry);
            Assert.True(defaults <= 1 && (defaults == 0 || contract.SignedTick is not null), $"{contract.Id} went into default {defaults} times.");
            if (contract.Status == ContractStatus.Fulfilled && contract is SaleContract sale)
            {
                GoodsHandedOverEntry handed = byContract[contract.Id].OfType<GoodsHandedOverEntry>().Single();
                Assert.Equal((sale.Seller!.Value, sale.Buyer!.Value, sale.Delivered), (handed.Seller, handed.Buyer, handed.Quantity));
            }
        }
    }

    [Fact]
    public void Signed_entries_name_the_taker_and_the_price_it_signed_at()
    {
        SimulationContext context = TestWorld.RunHeadless(7L, 2_000);
        List<ContractSignedEntry> signed = context.Journal.Entries.OfType<ContractSignedEntry>().ToList();
        Assert.NotEmpty(signed);
        foreach (ContractSignedEntry e in signed)
        {
            Contract contract = context.Contract(e.Contract);
            Assert.Equal(contract.CounterpartyId, e.Taker);
            Assert.Equal(contract.Price, e.Price);
        }
    }

    [Fact]
    public void Incident_entries_carry_the_resolved_incident_and_the_ship_owner()
    {
        SimulationContext context = TestWorld.RunHeadless(42L, 4_000);
        List<IncidentEntry> incidents = context.Journal.Entries.OfType<IncidentEntry>().ToList();
        Assert.NotEmpty(incidents);
        Assert.Equal(context.IncidentsOccurred, incidents.Count);
        Assert.All(incidents, e => Assert.True(context.Actor(e.Hauler).MovesCargo));
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
        Assert.Equal(4, journal.Entries[^1].Tick);
        Assert.Throws<System.ArgumentOutOfRangeException>(() => journal.Entries[3]);
    }

    [Theory]
    [InlineData(42L)]
    [InlineData(20260619L)]
    public void Every_lifecycle_entry_records_an_earlier_cause(long seed)
    {
        SimulationContext context = TestWorld.RunHeadless(seed, 3_000);
        IReadOnlyList<JournalEntry> entries = context.Journal.Entries;
        Assert.All(entries.Select((e, i) => (e, i)), x => Assert.Equal(x.i, x.e.Seq)); // the ring kept everything, in order

        foreach (JournalEntry entry in entries.Where(e => e is IContractEntry))
        {
            // A post is where a contract's record starts, unless something else led to it; anything later names what did.
            bool root = entry is ContractPostedEntry or StorageOpenedEntry;
            Assert.True(root || entry.Cause is not null, $"{entry} has no cause.");
            Assert.True(entry.Cause is null || entry.Cause < entry.Seq, $"{entry} names a later cause.");
        }

        // A haul posted to deliver a Sale follows from that Sale.
        foreach (ContractPostedEntry posted in entries.OfType<ContractPostedEntry>())
        {
            if (context.Contract(posted.Contract) is HaulContract { Serves: { } served } && posted.Cause is { } cause)
            {
                Assert.True(entries[(int)cause] is IContractEntry about && (about.Contract == served || context.Contract(about.Contract) is HaulContract h && h.Serves == served));
            }
        }
    }

    [Fact]
    public void A_lost_cargo_reads_back_from_the_failed_sale_through_its_haul_to_the_incident()
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState habs = context.Actor("Meridian Habs");
        ActorState farm = context.Actor("Verdant Agricombine");
        ActorState juno = context.Actor("Juno Okafor");
        Ship ship = context.ShipOf(juno);
        SaleContract wanted = context.PostWanted(habs, GoodType.Food, 60m, 900m, hours: 200);
        Market.FillViaHaul(context, farm, wanted, wanted.OfferedPrice, FillSource.FromStock(farm), Credits.Of(80m), Credits.Of(100m), out HaulContract? haul);
        Market.TakeHaul(context, juno, haul!, ship, haul!.OfferedPrice);
        context.RunTo(context.CurrentTick + 5);

        // A total loss on the way, recorded as TransitIncidentOccurred records it.
        var loss = new TransitIncident(TransitIncidentKind.TotalLoss, 0, 0m, 1m, Credits.Zero, "total cargo loss");
        JournalEntry incident = context.Record(new IncidentEntry(context.CurrentTick, haul.Id, juno.Id, loss));
        FailurePathTests.LoseAboard(context, ship, farm, GoodType.Food, 60m);
        context.RunUntilSettled(haul);

        DeliveryFailedEntry haulFailed = context.Journal.Entries.OfType<DeliveryFailedEntry>().Single(e => e.Contract == haul.Id);
        DeliveryFailedEntry saleFailed = context.Journal.Entries.OfType<DeliveryFailedEntry>().Single(e => e.Contract == wanted.Id);
        Assert.Equal(incident.Seq, haulFailed.Cause);
        Assert.Equal(haulFailed.Seq, saleFailed.Cause);
        Assert.Equal(saleFailed.Seq, wanted.LastEntry);
    }

    [Fact]
    public void The_journal_finds_an_entry_by_its_sequence_while_it_keeps_it()
    {
        var journal = new EventJournal(cap: 3);
        for (long t = 0; t < 5; t++)
        {
            Assert.Equal(t, journal.Record(new ContractExpiredEntry(t, new ContractId(t))).Seq);
        }

        Assert.Null(journal.Find(1)); // dropped from the ring
        Assert.Equal(2, journal.Find(2)!.Tick);
        Assert.Equal(4, journal.Find(4)!.Tick);
        Assert.Null(journal.Find(5)); // not yet recorded
    }
}
