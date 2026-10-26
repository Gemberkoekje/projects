using System;
using System.Globalization;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Presentation.Text;

/// <summary>
/// How numbers read on screen (design §11.1): credits as ₵ with grouped digits, a typographic minus for
/// losses, quantities as whole units, odds as percentages. Invariant culture, so every browser shows the
/// same figures. Dates and durations come from <see cref="GameTime"/>.
/// </summary>
public static class Format
{
    private const string Currency = "₵";
    private const string Minus = "−";

    /// <summary>Whole credits: "₵1,234", "−₵42".</summary>
    public static string Money(decimal amount) => Signed(Math.Round(amount, 0, MidpointRounding.AwayFromZero), "N0");

    /// <summary>Credits to the cent: "₵12.80", "−₵42.00".</summary>
    public static string MoneyCents(decimal amount) => Signed(Math.Round(amount, 2, MidpointRounding.AwayFromZero), "N2");

    public static string Money(Credits amount) => Money(amount.Amount);

    public static string MoneyCents(Credits amount) => MoneyCents(amount.Amount);

    /// <summary>A unit count, rounded: "90".</summary>
    public static string Quantity(decimal quantity) =>
        Math.Round(quantity, 0, MidpointRounding.AwayFromZero).ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>A fraction as a percentage: one decimal under 10% ("4.8%"), whole above ("26%").</summary>
    public static string Percent(decimal fraction)
    {
        decimal percent = fraction * 100m;
        string text = percent < 10m && percent != Math.Round(percent)
            ? Math.Round(percent, 1, MidpointRounding.AwayFromZero).ToString("0.0", CultureInfo.InvariantCulture)
            : Math.Round(percent, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
        return text + "%";
    }

    /// <summary>A change in a 0..1 quality, in percentage points: 0.14 reads "14".</summary>
    public static string Points(decimal fraction) =>
        Math.Round(fraction * 100m, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);

    /// <summary>Hours as a plain count: "40".</summary>
    public static string Hours(long ticks) => ticks.ToString("N0", CultureInfo.InvariantCulture);

    public static string Date(long tick) => GameTime.FormatDate(tick);

    public static string Duration(long ticks) => GameTime.FormatDuration(ticks);

    public static string Relative(long tick, long now) => GameTime.FormatRelative(tick, now);

    private static string Signed(decimal amount, string pattern)
    {
        string digits = Math.Abs(amount).ToString(pattern, CultureInfo.InvariantCulture);
        return amount < 0m ? Minus + Currency + digits : Currency + digits;
    }
}
