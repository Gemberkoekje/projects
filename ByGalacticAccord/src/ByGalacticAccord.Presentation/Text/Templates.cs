using System;
using System.Collections.Generic;
using System.Globalization;
using System.Resources;
using System.Text;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;

namespace ByGalacticAccord.Presentation.Text;

/// <summary>
/// The template layer (design §11.5). Every sentence and label the interface shows comes from
/// <c>Templates.resx</c>, keyed by event type or fact type, with named fields such as <c>{good}</c> filled
/// from structured data. Nothing is written free-hand: a missing key or an unfilled field throws, so a
/// test that renders a template proves it is complete.
/// </summary>
public static class Templates
{
    private static readonly ResourceManager Resources =
        new("ByGalacticAccord.Presentation.Text.Templates", typeof(Templates).Assembly);

    /// <summary>The template for <paramref name="key"/>, unfilled.</summary>
    public static string Get(string key) =>
        Resources.GetString(key, CultureInfo.CurrentUICulture)
        ?? throw new KeyNotFoundException($"No interface template '{key}'.");

    /// <summary>The template for <paramref name="key"/> with each <c>{name}</c> replaced by its fact.</summary>
    public static string Fill(string key, params (string Name, string Value)[] facts)
    {
        string template = Get(key);
        var result = new StringBuilder(template.Length + 32);
        int i = 0;
        while (i < template.Length)
        {
            char c = template[i];
            if (c != '{')
            {
                result.Append(c);
                i++;
                continue;
            }

            int close = template.IndexOf('}', i + 1);
            if (close < 0)
            {
                throw new FormatException($"Template '{key}' has an unclosed field.");
            }

            string name = template.Substring(i + 1, close - i - 1);
            result.Append(Fact(key, name, facts));
            i = close + 1;
        }

        return result.ToString();
    }

    private static string Fact(string key, string name, (string Name, string Value)[] facts)
    {
        foreach ((string factName, string value) in facts)
        {
            if (factName == name)
            {
                return value;
            }
        }

        throw new FormatException($"Template '{key}' needs the field '{name}'.");
    }

    // ---- words for the engine's enums (fact types) ----
    public static string Good(GoodType good) => Get("Good." + good);

    public static string Jurisdiction(AccordReach reach) => Get("Jurisdiction." + reach);

    public static string JurisdictionLong(AccordReach reach) => Get("Jurisdiction." + reach + ".Long");

    public static string JurisdictionShort(AccordReach reach) => Get("Jurisdiction." + reach + ".Short");

    public static string Danger(DangerBand band) => Get("Danger." + band);

    public static string LocationKind(LocationType type) => Get("LocationType." + type);

    public static string Role(ActorRole role) => Get("Role." + role);

    public static string ContractKind(ContractType type) => Get("Kind." + type);

    public static string ShipClass(ShipType type) => Get("ShipClass." + type);
}
