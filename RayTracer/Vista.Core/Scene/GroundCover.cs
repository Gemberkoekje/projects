using Vista.Astronomy;
using Vista.Weather;
using Vista.World;

namespace Vista.Scene;

/// <summary>
/// What the ground's materials read of the place rather than the world (vista-plan §10 M1.12, M1.12b): the altitudes of its grass
/// line and its tree line, from its climate (<see cref="GrassLine"/>, <see cref="TreeLine"/>), and which way its pole lies, so
/// poleward faces keep their grass and their forest lower; since M1.13 the season's snow from the sliders and the line of the
/// world's glaciers at the scene's altitude.
/// </summary>
/// <param name="GrassLineMetres">The grass line above sea level, m.</param>
/// <param name="Poleward">1 when the pole lies toward +z (north, the northern hemisphere), −1 when toward −z.</param>
/// <param name="TreeLineMetres">The tree line above sea level, m.</param>
public sealed record GroundCover(double GrassLineMetres, int Poleward, double TreeLineMetres)
{
    /// <summary>The default place's (<see cref="Places.Default"/>): what a scene that names no place gets.</summary>
    public static GroundCover Default { get; } = For(Climate.For(Places.Get(Places.Default)), Places.Get(Places.Default).LatitudeDegrees);

    /// <summary>
    /// Whether forest grows below the tree line (vista-plan §10 M1.12b); false renders M1.12's ground, its meadows and soil where the
    /// forest would stand (<c>--forest off</c>).
    /// </summary>
    public bool Forest { get; init; } = true;

    /// <summary>
    /// Whether the season's snow lies above <see cref="SnowlineMetres"/> (vista-plan §10 M1.13); false (the default, and
    /// <c>--snow off</c>) renders the ground bare. The world's glaciers lie either way.
    /// </summary>
    public bool Snow { get; init; }

    /// <summary>The seasonal snowline above sea level, m: the snow slider's (<see cref="Sliders.SliderState.SnowlineMetres"/>).</summary>
    public double SnowlineMetres { get; init; }

    /// <summary>The freezing level above sea level, m (<see cref="Sliders.SliderState.FreezingLevelMetres"/>): snow below it is wet.</summary>
    public double FreezingLevelMetres { get; init; }

    /// <summary>
    /// The world's glaciers' equilibrium line above sea level, m: theirs in the world's frame (<see cref="ErosionSettings.GlacierElaMetres"/>)
    /// plus the altitude the scene gives the world's height 0; firn above it, bare ice below.
    /// </summary>
    public double GlacierLineMetres { get; init; }

    /// <summary>
    /// The prevailing wind's direction of travel as (x, z), east and north (vista-plan §10 M1.13): the mid-latitudes' westerlies, which
    /// blow toward the east in both hemispheres, for every place in the dropdown.
    /// </summary>
    public static (float X, float Z) Wind => (1f, 0f);

    /// <summary>The snow and the glaciers' line as the material rules read them (<see cref="MaterialConstants.WithSnow"/>).</summary>
    public SnowPlacement SnowPlacement =>
        new(Snow, (float)SnowlineMetres, (float)FreezingLevelMetres, Wind.X, Wind.Z, (float)GlacierLineMetres);

    /// <summary>The ground cover of a place whose climate is <paramref name="climate"/> at <paramref name="latitudeDegrees"/>.</summary>
    public static GroundCover For(ClimateNormals climate, double latitudeDegrees) =>
        new(GrassLine.AltitudeMetres(climate), latitudeDegrees >= 0.0 ? 1 : -1, TreeLine.AltitudeMetres(climate));
}
