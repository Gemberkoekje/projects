using Vista.Astronomy;
using Vista.Weather;

namespace Vista.Scene;

/// <summary>
/// What the ground's materials read of the place rather than the world (vista-plan §10 M1.12, M1.12b): the altitudes of its grass
/// line and its tree line, from its climate (<see cref="GrassLine"/>, <see cref="TreeLine"/>), and which way its pole lies, so
/// poleward faces keep their grass and their forest lower.
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

    /// <summary>The ground cover of a place whose climate is <paramref name="climate"/> at <paramref name="latitudeDegrees"/>.</summary>
    public static GroundCover For(ClimateNormals climate, double latitudeDegrees) =>
        new(GrassLine.AltitudeMetres(climate), latitudeDegrees >= 0.0 ? 1 : -1, TreeLine.AltitudeMetres(climate));
}
