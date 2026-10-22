using Vista.Astronomy;
using Vista.Weather;

namespace Vista.Scene;

/// <summary>
/// What the ground's materials read of the place rather than the world (vista-plan §10 M1.12): the altitude of its grass line,
/// from its climate (<see cref="GrassLine"/>), and which way its pole lies, so poleward faces keep their grass lower.
/// </summary>
/// <param name="GrassLineMetres">The grass line above sea level, m.</param>
/// <param name="Poleward">1 when the pole lies toward +z (north, the northern hemisphere), −1 when toward −z.</param>
public sealed record GroundCover(double GrassLineMetres, int Poleward)
{
    /// <summary>The default place's (<see cref="Places.Default"/>): what a scene that names no place gets.</summary>
    public static GroundCover Default { get; } = For(Climate.For(Places.Get(Places.Default)), Places.Get(Places.Default).LatitudeDegrees);

    /// <summary>The ground cover of a place whose climate is <paramref name="climate"/> at <paramref name="latitudeDegrees"/>.</summary>
    public static GroundCover For(ClimateNormals climate, double latitudeDegrees) =>
        new(GrassLine.AltitudeMetres(climate), latitudeDegrees >= 0.0 ? 1 : -1);
}
