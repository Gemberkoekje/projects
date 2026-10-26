using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Text.RegularExpressions;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;
using ByGalacticAccord.Presentation.Text;

namespace ByGalacticAccord.Tests;

/// <summary>
/// The template layer (design §11.5): every interface sentence is a template over structured data, and a
/// template either fills completely or fails loudly.
/// </summary>
public sealed class TemplateTests
{
    [Fact]
    public void Fill_replaces_every_named_field()
    {
        string text = Templates.Fill("Route", ("origin", "Verdant"), ("destination", "Meridian Station"));
        Assert.Equal("Verdant → Meridian Station", text);
    }

    [Fact]
    public void A_missing_field_or_template_throws()
    {
        Assert.Throws<FormatException>(() => Templates.Fill("Route", ("origin", "Verdant")));
        Assert.Throws<KeyNotFoundException>(() => Templates.Get("No.Such.Template"));
    }

    [Fact]
    public void Every_template_is_well_formed()
    {
        var fields = new Regex(@"\{([A-Za-z]+)\}");
        foreach ((string key, string template) in AllTemplates())
        {
            string stripped = fields.Replace(template, string.Empty);
            Assert.False(stripped.Contains('{') || stripped.Contains('}'), $"Template '{key}' has a malformed field: {template}");
            Assert.False(string.IsNullOrWhiteSpace(template), $"Template '{key}' is empty.");
        }
    }

    [Fact]
    public void Every_word_the_engine_can_ask_for_has_a_template()
    {
        Assert.All(Enum.GetValues<GoodType>(), g => Assert.False(string.IsNullOrEmpty(Templates.Good(g))));
        Assert.All(Enum.GetValues<AccordReach>(), r =>
        {
            Assert.False(string.IsNullOrEmpty(Templates.Jurisdiction(r)));
            Assert.False(string.IsNullOrEmpty(Templates.JurisdictionLong(r)));
            Assert.False(string.IsNullOrEmpty(Templates.JurisdictionShort(r)));
        });
        Assert.All(Enum.GetValues<DangerBand>(), b => Assert.False(string.IsNullOrEmpty(Templates.Danger(b))));
        Assert.All(Enum.GetValues<LocationType>(), t => Assert.False(string.IsNullOrEmpty(Templates.LocationKind(t))));
        Assert.All(Enum.GetValues<ActorRole>(), r => Assert.False(string.IsNullOrEmpty(Templates.Role(r))));
        Assert.All(Enum.GetValues<ContractType>(), k => Assert.False(string.IsNullOrEmpty(Templates.ContractKind(k))));
        Assert.All(Enum.GetValues<SaleSide>(), s => Assert.False(string.IsNullOrEmpty(Templates.Side(s))));
        Assert.All(Enum.GetValues<ShipType>(), s => Assert.False(string.IsNullOrEmpty(Templates.ShipClass(s))));
    }

    [Fact]
    public void Every_journal_entry_reads_as_a_sentence_for_an_ai_and_for_the_player()
    {
        SimulationContext context = WorldGenerator.Generate(42L, withPlayer: true).Context;
        ActorId player = context.PlayerActorId!.Value;
        ActorState consumer = context.ActorsOrdered.First(a => a.Demands.Count > 0);
        ActorState producer = context.ActorsOrdered.First(a => a.Producer is not null);
        ActorId trader = context.ActorsOrdered.First(a => a.MovesCargo && !a.IsPlayer).Id;
        ActorId station = context.StationOperatorAt(consumer.Home)!.Id;

        // Each kind of contract, posted by an AI actor and by the player, and signed by the other side.
        var contracts = new List<Contract>();
        foreach ((ActorId poster, ActorId taker) in new[] { (consumer.Id, player), (player, trader), (consumer.Id, trader) })
        {
            contracts.Add(Signed(context, Sale(context, poster, SaleSide.Wanted, producer, consumer), taker));
            contracts.Add(Signed(context, Sale(context, poster == player ? player : producer.Id, SaleSide.ForSale, producer, consumer), taker == player ? player : consumer.Id));
            contracts.Add(Signed(context, Haul(context, poster == player ? player : producer.Id, producer, consumer), taker));
        }

        var entries = new List<JournalEntry> { new FaucetAdjustedEntry(1, 0.09m, 0.56m) };
        foreach (Contract contract in contracts)
        {
            ContractId id = contract.Id;
            ActorId payer = contract.Payer!.Value;
            ActorId performer = contract.Performer!.Value;
            entries.Add(new ContractPostedEntry(1, id, contract.PostedBy));
            entries.Add(new ContractSignedEntry(2, id, contract.CounterpartyId!.Value, Credits.Of(120m)));
            entries.Add(new ContractExpiredEntry(3, id));
            entries.Add(new ContractWithdrawnEntry(3, id));
            entries.Add(new PaymentDefaultedEntry(7, id, payer, performer));
            foreach (FailureReason reason in Enum.GetValues<FailureReason>())
            {
                entries.Add(new DeliveryFailedEntry(8, id, performer, reason));
            }

            if (contract is SaleContract)
            {
                entries.Add(new GoodsHandedOverEntry(5, id, performer, payer, 40m, 1m, 0, Credits.Of(120m), Credits.Zero, Credits.Zero));
                entries.Add(new GoodsHandedOverEntry(6, id, performer, payer, 38m, 0.5m, 7, Credits.Of(120m), Credits.Of(14m), Credits.Of(9m)));
            }
            else
            {
                entries.Add(new HaulDeliveredEntry(5, id, performer, 40m, 1m, 0, Credits.Of(120m), Credits.Zero, Credits.Zero));
                entries.Add(new HaulDeliveredEntry(6, id, performer, 38m, 0.5m, 7, Credits.Of(120m), Credits.Of(14m), Credits.Of(9m)));
            }

            foreach (TransitIncidentKind kind in Enum.GetValues<TransitIncidentKind>())
            {
                entries.Add(new IncidentEntry(9, id, performer, new TransitIncident(kind, 6, 0.14m, 0.2m, Credits.Of(75m), "unused")));
            }
        }

        foreach (ActorId renter in new[] { trader, player })
        {
            var storage = new StorageContract(context.NextContractId(), station, renter, 0, consumer.Home, Credits.Of(0.1m), AccordReach.AccordEdge, "Helios");
            context.Add(storage);
            entries.Add(new StorageOpenedEntry(1, storage.Id, renter));
            entries.Add(new StorageClosedEntry(2, storage.Id, renter, Credits.Of(4m)));
        }

        foreach (JournalEntry entry in entries)
        {
            string text = JournalText.Describe(context, entry);
            Assert.DoesNotContain("{", text, StringComparison.Ordinal);
            Assert.DoesNotContain("unused", text, StringComparison.Ordinal);
            Assert.DoesNotContain("  ", text, StringComparison.Ordinal); // no field filled with nothing
            Assert.EndsWith(".", text, StringComparison.Ordinal);

            // The player is "You" only at the start of a sentence, and "you" or "your" inside one, never "You's".
            Assert.False(Regex.IsMatch(text, @"(?<!^|\. )You\b"), $"The player's name inside a sentence: {text}");
            Assert.DoesNotContain("You's", text, StringComparison.Ordinal);
        }

        Contract yourHaul = contracts.OfType<HaulContract>().Single(h => h.PostedBy == player);
        Assert.StartsWith("You took", JournalText.Describe(context, new ContractSignedEntry(4, contracts[2].Id, player, Credits.Of(120m))), StringComparison.Ordinal);
        Assert.Contains("took your haul", JournalText.Describe(context, new ContractSignedEntry(4, yourHaul.Id, trader, Credits.Of(120m))), StringComparison.Ordinal);
    }

    [Fact]
    public void Delivered_text_reports_lateness_and_the_warranty_refund()
    {
        SimulationContext context = TestWorld.RunHeadless(42L, 500);
        Contract contract = context.Contracts.Values.First();
        ActorId hauler = context.ActorsOrdered.First(a => a.MovesCargo).Id;

        string late = JournalText.Describe(context, new HaulDeliveredEntry(5, contract.Id, hauler, 40m, 0.5m, 30, Credits.Of(120m), Credits.Of(14m), Credits.Of(9m)));

        Assert.Contains("1d 6h late", late, StringComparison.Ordinal);
        Assert.Contains("late fee ₵14", late, StringComparison.Ordinal);
        Assert.Contains("Quality on arrival 50%: ₵9", late, StringComparison.Ordinal);
    }

    // A Sale of the producer's good at the consumer's home, posted by poster.
    private static SaleContract Sale(SimulationContext context, ActorId poster, SaleSide side, ActorState producer, ActorState consumer)
    {
        var sale = new SaleContract(
            context.NextContractId(), poster, 0, side, producer.Producer!.Good, 40m, consumer.Home, Credits.Of(120m), Credits.Of(150m), 0.04m, 500,
            AccordReach.AccordEdge, "Helios", Array.Empty<ContractClause>());
        context.Add(sale);
        return sale;
    }

    // A haul of the producer's good from its home to the consumer's.
    private static HaulContract Haul(SimulationContext context, ActorId shipper, ActorState producer, ActorState consumer)
    {
        var haul = new HaulContract(
            context.NextContractId(), shipper, 0, producer.Producer!.Good, 40m, producer.Home, consumer.Home, Holding.StockAt(producer.Home), null,
            Credits.Of(120m), Credits.Of(150m), 0.04m, 500, AccordReach.AccordEdge, "Helios", Array.Empty<ContractClause>());
        context.Add(haul);
        return haul;
    }

    private static Contract Signed(SimulationContext context, Contract contract, ActorId taker)
    {
        contract.Sign(taker, contract.OfferedPrice, 1);
        return contract;
    }

    private static IEnumerable<(string Key, string Template)> AllTemplates()
    {
        var resources = new ResourceManager("ByGalacticAccord.Presentation.Text.Templates", typeof(Templates).Assembly);
        ResourceSet set = resources.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: true)!;
        foreach (DictionaryEntry entry in set)
        {
            yield return ((string)entry.Key, (string)entry.Value!);
        }
    }
}
