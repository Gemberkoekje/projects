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
        Assert.All(Enum.GetValues<ShipType>(), s => Assert.False(string.IsNullOrEmpty(Templates.ShipClass(s))));
    }

    [Fact]
    public void Every_journal_entry_reads_as_a_sentence_for_an_ai_and_for_the_player()
    {
        SimulationContext context = WorldGenerator.Generate(42L, withPlayer: true).Context;
        ActorId player = context.PlayerActorId!.Value;
        ActorState consumer = context.ActorsOrdered.First(a => a.Demands.Count > 0);
        ActorState producer = context.ActorsOrdered.First(a => a.Producer is not null);
        ActorId ai = context.ActorsOrdered.First(a => a.MovesCargo && !a.IsPlayer).Id;
        var contract = new Contract(
            context.NextContractId(), ContractType.Haul, consumer.Id, producer.Producer!.Good, 40m, producer.Home, consumer.Home,
            Credits.Of(120m), Credits.Of(200m), Credits.Of(300m), 0.04m, 500, AccordReach.AccordEdge, "Helios",
            Array.Empty<ContractClause>());
        context.Add(contract);
        ContractId id = contract.Id;

        var entries = new List<JournalEntry>
        {
            new ContractPostedEntry(1, id, consumer.Id, producer.Id),
            new ContractExpiredEntry(2, id),
            new FaucetAdjustedEntry(3, 0.09m, 0.56m),
        };
        foreach (ActorId hauler in new[] { ai, player })
        {
            entries.Add(new HaulTakenEntry(4, id, hauler, Credits.Of(120m)));
            entries.Add(new HaulDeliveredEntry(5, id, hauler, 40m, 1m, 0, Credits.Of(120m), Credits.Zero, Credits.Zero));
            entries.Add(new HaulDeliveredEntry(6, id, hauler, 38m, 0.5m, 7, Credits.Of(120m), Credits.Of(14m), Credits.Of(9m)));
            entries.Add(new PaymentDefaultedEntry(7, id, consumer.Id, hauler));
            entries.Add(new DeliveryFailedEntry(8, id, hauler));
            foreach (TransitIncidentKind kind in Enum.GetValues<TransitIncidentKind>())
            {
                entries.Add(new IncidentEntry(9, id, hauler, new TransitIncident(kind, 6, 0.14m, 0.2m, Credits.Of(75m), "unused")));
            }
        }

        foreach (JournalEntry entry in entries)
        {
            string text = JournalText.Describe(context, entry);
            Assert.DoesNotContain("{", text, StringComparison.Ordinal);
            Assert.DoesNotContain("unused", text, StringComparison.Ordinal);
            Assert.EndsWith(".", text, StringComparison.Ordinal);
            if (JournalText.Subject(entry) == player)
            {
                Assert.DoesNotContain(" You ", text, StringComparison.Ordinal); // the player's name is only ever sentence-initial
            }
        }

        Assert.StartsWith("You took", JournalText.Describe(context, new HaulTakenEntry(4, id, player, Credits.Of(120m))), StringComparison.Ordinal);
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
