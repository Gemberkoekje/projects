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
    public void Every_contract_has_entries_that_match_how_it_ended(long seed)
    {
        // A small cap would drop early entries, so keep the run inside the journal's 5,000.
        SimulationContext context = TestWorld.RunHeadless(seed, 3_000);
        IReadOnlyList<JournalEntry> entries = context.Journal.Entries;
        Assert.Equal(context.Journal.TotalRecorded, entries.Count);
        ILookup<ContractId, JournalEntry> byContract = entries.ToLookup(e => e switch
        {
            ContractPostedEntry x => x.Contract,
            ContractSignedEntry x => x.Contract,
            GoodsHandedOverEntry x => x.Contract,
            HaulDeliveredEntry x => x.Contract,
            PaymentDefaultedEntry x => x.Contract,
            DeliveryFailedEntry x => x.Contract,
            ContractExpiredEntry x => x.Contract,
            ContractWithdrawnEntry x => x.Contract,
            StorageOpenedEntry x => x.Contract,
            StorageClosedEntry x => x.Contract,
            IncidentEntry x => x.Contract,
            _ => default,
        });

        foreach (Contract contract in context.Contracts.Values)
        {
            List<string> kinds = byContract[contract.Id].Where(e => e is not IncidentEntry).Select(e => e.GetType().Name.Replace("Entry", string.Empty)).ToList();
            if (contract is StorageContract storage)
            {
                Assert.Equal(storage.Status == ContractStatus.Active ? new[] { "StorageOpened" } : new[] { "StorageOpened", "StorageClosed" }, kinds);
                continue;
            }

            string[] expected = contract.Status switch
            {
                ContractStatus.Open => new[] { "ContractPosted" },
                ContractStatus.Active => new[] { "ContractPosted", "ContractSigned" },
                ContractStatus.Fulfilled => new[] { "ContractPosted", "ContractSigned", contract is HaulContract ? "HaulDelivered" : "GoodsHandedOver" },
                ContractStatus.Breached => new[] { "ContractPosted", "ContractSigned", "DeliveryFailed" },
                ContractStatus.Defaulted => new[] { "ContractPosted", "ContractSigned", "PaymentDefaulted" },
                ContractStatus.Expired => new[] { "ContractPosted", "ContractExpired" },
                ContractStatus.Withdrawn => new[] { "ContractPosted", "ContractWithdrawn" },
                _ => new[] { "unexpected status " + contract.Status },
            };

            Assert.Equal(expected, kinds);
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
}
