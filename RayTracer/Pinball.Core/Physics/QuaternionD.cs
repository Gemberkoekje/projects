using System;

namespace Pinball.Physics;

/// <summary>
/// A double-precision unit quaternion for ball orientation (pinball-plan §6.1.1). Integrated as
/// <c>q̇ = ½ ω⊗q</c> and renormalised each step to kill drift. A uniform sphere's inertia is isotropic
/// so orientation is not dynamically load-bearing, but it is tracked so the renderer can spin the ball's
/// surface/reflection and so non-uniform features can be attached later. Layout: <c>(W, X, Y, Z)</c>.
/// </summary>
public readonly struct QuaternionD : IEquatable<QuaternionD>
{
    /// <summary>Scalar (real) component.</summary>
    public readonly double W;
    /// <summary>Vector X component.</summary>
    public readonly double X;
    /// <summary>Vector Y component.</summary>
    public readonly double Y;
    /// <summary>Vector Z component.</summary>
    public readonly double Z;

    /// <summary>Constructs a quaternion from its components (<paramref name="w"/> scalar first).</summary>
    public QuaternionD(double w, double x, double y, double z) { W = w; X = x; Y = y; Z = z; }

    /// <summary>The identity rotation.</summary>
    public static QuaternionD Identity => new(1, 0, 0, 0);

    /// <summary>Hamilton product <c>a·b</c> (apply <paramref name="b"/> then <paramref name="a"/>).</summary>
    public static QuaternionD operator *(QuaternionD a, QuaternionD b) => new(
        a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z,
        a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
        a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
        a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W);

    /// <summary>Component-wise sum (used by the derivative integrator).</summary>
    public static QuaternionD operator +(QuaternionD a, QuaternionD b) => new(a.W + b.W, a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    /// <summary>Scalar multiply.</summary>
    public static QuaternionD operator *(QuaternionD a, double s) => new(a.W * s, a.X * s, a.Y * s, a.Z * s);

    /// <summary>Length of the 4-vector.</summary>
    public double Length() => Math.Sqrt(W * W + X * X + Y * Y + Z * Z);

    /// <summary>Renormalises to a unit quaternion (identity if degenerate).</summary>
    public QuaternionD Normalized()
    {
        double len = Length();
        return len > 1e-300 ? this * (1.0 / len) : Identity;
    }

    /// <summary>
    /// Advances the orientation by angular velocity <paramref name="omega"/> (world axes, rad/s) over
    /// <paramref name="h"/> seconds via one semi-implicit step <c>q + ½(ω⊗q)h</c>, then renormalises
    /// (§6.1.5). <paramref name="omega"/> is treated as the pure quaternion <c>(0, ωx, ωy, ωz)</c>.
    /// </summary>
    public QuaternionD IntegrateAngularVelocity(Vector3D omega, double h)
    {
        var omegaQuat = new QuaternionD(0, omega.X, omega.Y, omega.Z);
        QuaternionD derivative = omegaQuat * this; // ω⊗q
        QuaternionD next = this + derivative * (0.5 * h);
        return next.Normalized();
    }

    /// <summary>Rotates a vector by this quaternion (<c>q·(0,v)·q⁻¹</c>).</summary>
    public Vector3D Rotate(Vector3D v)
    {
        // Optimised form: t = 2·(q_vec × v); v' = v + w·t + q_vec × t.
        var qv = new Vector3D(X, Y, Z);
        Vector3D t = Vector3D.Cross(qv, v) * 2.0;
        return v + t * W + Vector3D.Cross(qv, t);
    }

    /// <inheritdoc/>
    public bool Equals(QuaternionD other) => W.Equals(other.W) && X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is QuaternionD q && Equals(q);
    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(W, X, Y, Z);
    /// <summary>Value equality.</summary>
    public static bool operator ==(QuaternionD a, QuaternionD b) => a.Equals(b);
    /// <summary>Value inequality.</summary>
    public static bool operator !=(QuaternionD a, QuaternionD b) => !a.Equals(b);
    /// <inheritdoc/>
    public override string ToString() => $"({W:0.#####}, {X:0.#####}, {Y:0.#####}, {Z:0.#####})";
}
