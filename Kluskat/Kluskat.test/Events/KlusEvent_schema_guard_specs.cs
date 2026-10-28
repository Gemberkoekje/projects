using System.Reflection;

namespace Kluskat.test.Events;

/// <summary>
/// The klus streams are kept forever and phase 2 recomputes XP and achievements from them, so an
/// event may only be added — never renamed, removed or changed. Changing one fails this spec; add a
/// new version of the event instead and append it to the list.
/// </summary>
public sealed class KlusEvent_schema_guard_specs
{
    private static readonly string[] Bevroren =
    [
        "KlusAangepast(Grootte:Grootte, IntervalDagen:Int32, Naam:String, Ruimte:String, When:DateTime)",
        "KlusGedaan(GedaanOp:DateTime, ViaBeheer:Boolean, When:DateTime)",
        "KlusGedaanTeruggedraaid(LaatstGedaan:DateTime, When:DateTime)",
        "KlusGeschrapt(When:DateTime)",
        "KlusOvergeslagen(Reden:OverslaanReden, When:DateTime)",
        "KlusToegevoegd(Grootte:Grootte, IntervalDagen:Int32, LaatstGedaan:DateTime, Naam:String, Ruimte:String, When:DateTime)",
    ];

    [Test]
    public void no_event_was_changed_or_removed()
    {
        var vingerafdrukken = typeof(KlusEvent).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(KlusEvent)) && !t.IsAbstract)
            .Select(Vingerafdruk)
            .Order(StringComparer.Ordinal);

        vingerafdrukken.Should().Equal(Bevroren);
    }

    private static string Vingerafdruk(Type type)
    {
        var velden = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => $"{p.Name}:{p.PropertyType.Name}");

        return $"{type.Name}({string.Join(", ", velden)})";
    }
}
