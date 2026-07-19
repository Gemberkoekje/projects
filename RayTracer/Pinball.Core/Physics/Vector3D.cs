using System;
using System.Numerics;

namespace Pinball.Physics;

/// <summary>
/// A double-precision 3-vector for the physics simulation (pinball-plan §6.1.1). The render side uses
/// <see cref="System.Numerics.Vector3"/> (single precision); contact resolution and reproducible
/// integration need the extra mantissa, so all narrowphase/integration math is <see langword="double"/>.
/// Immutable value type.
/// </summary>
public readonly struct Vector3D : IEquatable<Vector3D>
{
    /// <summary>X component (metres, world/playfield frame).</summary>
    public readonly double X;
    /// <summary>Y component (metres; +Y is true vertical, §6.1.1).</summary>
    public readonly double Y;
    /// <summary>Z component (metres).</summary>
    public readonly double Z;

    /// <summary>Constructs a vector from its components.</summary>
    public Vector3D(double x, double y, double z) { X = x; Y = y; Z = z; }

    /// <summary>The zero vector.</summary>
    public static Vector3D Zero => new(0, 0, 0);
    /// <summary>The world X axis.</summary>
    public static Vector3D UnitX => new(1, 0, 0);
    /// <summary>The world Y axis (true vertical).</summary>
    public static Vector3D UnitY => new(0, 1, 0);
    /// <summary>The world Z axis.</summary>
    public static Vector3D UnitZ => new(0, 0, 1);

    /// <summary>Component-wise sum.</summary>
    public static Vector3D operator +(Vector3D a, Vector3D b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    /// <summary>Component-wise difference.</summary>
    public static Vector3D operator -(Vector3D a, Vector3D b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    /// <summary>Negation.</summary>
    public static Vector3D operator -(Vector3D a) => new(-a.X, -a.Y, -a.Z);
    /// <summary>Scalar multiply.</summary>
    public static Vector3D operator *(Vector3D a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    /// <summary>Scalar multiply.</summary>
    public static Vector3D operator *(double s, Vector3D a) => new(a.X * s, a.Y * s, a.Z * s);
    /// <summary>Scalar divide.</summary>
    public static Vector3D operator /(Vector3D a, double s) => new(a.X / s, a.Y / s, a.Z / s);

    /// <summary>Dot product.</summary>
    public static double Dot(Vector3D a, Vector3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    /// <summary>Cross product (right-handed).</summary>
    public static Vector3D Cross(Vector3D a, Vector3D b) => new(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X);

    /// <summary>Euclidean length.</summary>
    public double Length() => Math.Sqrt(X * X + Y * Y + Z * Z);
    /// <summary>Squared length (no square root).</summary>
    public double LengthSquared() => X * X + Y * Y + Z * Z;

    /// <summary>Unit vector in the same direction; returns <see cref="Zero"/> for a (near-)zero vector.</summary>
    public Vector3D Normalized()
    {
        double len = Length();
        return len > 1e-300 ? this / len : Zero;
    }

    /// <summary>Distance between two points.</summary>
    public static double Distance(Vector3D a, Vector3D b) => (a - b).Length();

    /// <summary>Linear interpolation, <paramref name="t"/> in [0,1].</summary>
    public static Vector3D Lerp(Vector3D a, Vector3D b, double t) => a + (b - a) * t;

    /// <summary>Component-wise minimum.</summary>
    public static Vector3D Min(Vector3D a, Vector3D b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
    /// <summary>Component-wise maximum.</summary>
    public static Vector3D Max(Vector3D a, Vector3D b) => new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));

    /// <summary>Converts to the render side's single-precision vector (the physics→render handoff, §6.2).</summary>
    public Vector3 ToVector3() => new((float)X, (float)Y, (float)Z);
    /// <summary>Lifts a single-precision vector (e.g. authored scene geometry) into physics space.</summary>
    public static Vector3D FromVector3(Vector3 v) => new(v.X, v.Y, v.Z);

    /// <inheritdoc/>
    public bool Equals(Vector3D other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Vector3D v && Equals(v);
    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
    /// <summary>Value equality.</summary>
    public static bool operator ==(Vector3D a, Vector3D b) => a.Equals(b);
    /// <summary>Value inequality.</summary>
    public static bool operator !=(Vector3D a, Vector3D b) => !a.Equals(b);
    /// <inheritdoc/>
    public override string ToString() => $"({X:0.#####}, {Y:0.#####}, {Z:0.#####})";
}
