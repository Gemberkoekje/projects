using System.Globalization;
using System.Text.Json;
using Vista.Astronomy;
using Vista.Recipes;

namespace Vista.Sliders;

/// <summary>
/// What a recipe's set pieces push onto the auto sliders (vista-plan §3.4: "building blocks push defaults"). A push moves only
/// an auto slider; a pinned one keeps its value, and when the two contradict each other the result is rendered as asked, with an
/// "implausible" hint. Only the frozen lake pushes today: winter, and air below freezing.
/// </summary>
/// <param name="Place">The place the pieces suggest; null for none.</param>
/// <param name="Season">The season the pieces suggest; null for none.</param>
/// <param name="Time">The time of day the pieces suggest; null for none.</param>
/// <param name="TemperatureCeilingCelsius">The warmest auto temperature the pieces allow, °C; null for no limit.</param>
/// <param name="Pieces">The pieces that pushed something, by slider.</param>
public sealed record SliderPushes(Place? Place, Season? Season, TimeOfDay? Time, double? TemperatureCeilingCelsius,
    IReadOnlyDictionary<Slider, SetPiece> Pieces)
{
    /// <summary>The warmest auto temperature a frozen lake allows, °C: ice needs a hard frost, not a cool day.</summary>
    public const double FrozenLakeCeilingCelsius = -5.0;

    /// <summary>No pushes.</summary>
    public static SliderPushes None { get; } = new(null, null, null, null, new Dictionary<Slider, SetPiece>());

    /// <summary>What <paramref name="pieces"/> push, the first piece in catalogue order winning a slider two pieces push.</summary>
    public static SliderPushes For(IEnumerable<SetPiece> pieces)
    {
        ArgumentNullException.ThrowIfNull(pieces);
        Season? season = null;
        double? ceiling = null;
        var by = new Dictionary<Slider, SetPiece>();
        foreach (SetPiece piece in pieces.Distinct().Order())
        {
            if (piece == SetPiece.FrozenLake)
            {
                if (season is null)
                {
                    season = Recipes.Season.Winter;
                    by[Slider.Season] = piece;
                }
                if (ceiling is null || FrozenLakeCeilingCelsius < ceiling)
                {
                    ceiling = FrozenLakeCeilingCelsius;
                    by[Slider.Temperature] = piece;
                }
            }
        }
        return new SliderPushes(null, season, null, ceiling, by);
    }

    /// <summary>"pushed by frozenLake" for a slider a piece pushed.</summary>
    public string By(Slider slider) =>
        string.Create(CultureInfo.InvariantCulture, $"pushed by {JsonNamingPolicy.CamelCase.ConvertName(Pieces[slider].ToString())}");
}
