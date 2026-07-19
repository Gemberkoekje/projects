using System;
using System.Collections.Generic;

namespace Pinball.Physics;

/// <summary>
/// The physics side of the Space Cadet RT P0 table (pinball-plan §5.3 / §7.3 <c>Table/*</c>): the analytic
/// collider set co-registered with the render <c>PinballTableScene</c>. Collider geometry (walls, posts,
/// flipper pivots/tips) is the render layout's world coordinates scaled by <see cref="RenderScale"/> into SI
/// metres — chosen so the render ball (0.36 units) maps exactly to the real 27&#160;mm ball — so the two stay
/// one scale factor apart. (<see cref="BallStart"/> is a gameplay spawn, not a scaled render coordinate; the
/// render's static ball prop sits elsewhere, in the shooter lane.) The 6.5° incline is carried as tilted
/// gravity on a <b>flat</b> playfield (matching the flat
/// P0 render): identical 3-D dynamics, just expressed in the render's untilted frame (see
/// <see cref="PhysicsSettings.GravityOverride"/>). The render mover handoff — feeding the ball centre and
/// flipper angles back through P4's <c>UpdateSpheres</c>/<c>SetDynamicPose</c> — lives in the P6 game host,
/// which owns both this table and the renderer; this type is pure physics.
/// </summary>
public sealed class PinballTable
{
    /// <summary>Metres per render unit — the render ball radius (0.36 u) maps to <see cref="PhysicsConstants.BallRadius"/>.</summary>
    public const double RenderScale = PhysicsConstants.BallRadius / 0.36;

    // Render-space extents (from PinballTableScene), used to place the perimeter.
    private const double MinX = -5.5, MaxX = 5.5, MaxZ = 24.0;

    /// <summary>World-level physics settings (incline as tilted gravity on the flat playfield).</summary>
    public PhysicsSettings Settings { get; }
    /// <summary>Every collider, in the stable order that seeds deterministic contact processing.</summary>
    public IReadOnlyList<ICollider> Colliders { get; }
    /// <summary>The left flipper actuator (energise to flip).</summary>
    public Flipper LeftFlipper { get; }
    /// <summary>The right flipper actuator.</summary>
    public Flipper RightFlipper { get; }
    /// <summary>A ball comes into play centred here (metres) — up-table, ready to roll toward the flippers.</summary>
    public Vector3D BallStart { get; }
    /// <summary>A ball whose z falls below this (metres) has passed the flippers and drained.</summary>
    public double DrainZ { get; }

    /// <summary>Builds the P0 physics table.</summary>
    public PinballTable(ulong seed = 0x5D_EE_CE_5Eul)
    {
        double alpha = PhysicsConstants.DefaultInclineRadians;
        double g = PhysicsConstants.Gravity;
        // Flippers sit at the low (small-z) end, so down-slope is −z: the ball drains toward them.
        Settings = PhysicsSettings.Default with
        {
            Seed = seed,
            GravityOverride = new Vector3D(0, -g * Math.Cos(alpha), -g * Math.Sin(alpha)),
        };

        var steel = PhysicsMaterial.Steel;
        var rubber = PhysicsMaterial.Rubber;

        // Flippers, bat at ball-centre height so a side hit is a clean XZ contact. Left flips CCW (+θ),
        // right CW (−θ), each sweeping its tip up-table to knock a descending ball back up.
        double tube = 0.6 * PhysicsConstants.BallRadius;
        LeftFlipper = MakeFlipper(-1.5, 3.4, -2.7, 2.5, endAngle: +1.4, tube, rubber);
        RightFlipper = MakeFlipper(1.5, 3.4, 2.7, 2.5, endAngle: -1.4, tube, rubber);

        double postR = 0.12 * RenderScale, postH = 0.7 * RenderScale;
        var colliders = new List<ICollider>
        {
            new PlaneCollider(Vector3D.Zero, Vector3D.UnitY, PhysicsMaterial.Playfield),        // playfield y=0
            new PlaneCollider(new Vector3D(MinX * RenderScale, 0, 0), Vector3D.UnitX, steel),   // left wall
            new PlaneCollider(new Vector3D(MaxX * RenderScale, 0, 0), -Vector3D.UnitX, steel), // right wall (render MaxX; the shooter-lane divider is deferred with the lane feed)
            new PlaneCollider(new Vector3D(0, 0, MaxZ * RenderScale), -Vector3D.UnitZ, steel),  // back wall
            CylinderCollider.VerticalPost(new Vector3D(-3.9 * RenderScale, 0, 3.6 * RenderScale), postH, postR, rubber), // left slingshot post
            CylinderCollider.VerticalPost(new Vector3D(3.9 * RenderScale, 0, 3.6 * RenderScale), postH, postR, rubber),  // right slingshot post
            LeftFlipper,
            RightFlipper,
        };
        Colliders = colliders;

        BallStart = PlayfieldPoint(0.0, 6.0);       // centre, above the flippers
        DrainZ = 1.5 * RenderScale;                 // below the flipper line ⇒ drained
    }

    /// <summary>A point on the playfield at ball-rest height, from render-space X/Z (metres out).</summary>
    public static Vector3D PlayfieldPoint(double renderX, double renderZ) =>
        new(renderX * RenderScale, PhysicsConstants.BallRadius, renderZ * RenderScale);

    /// <summary>A fresh ball at rest at <see cref="BallStart"/>.</summary>
    public BallState NewBall() => BallState.AtRest(BallStart);

    /// <summary>True once the ball has drained (fallen past the flippers).</summary>
    public bool IsDrained(in BallState ball) => ball.Position.Z < DrainZ;

    private static Flipper MakeFlipper(double pivotX, double pivotZ, double tipX, double tipZ,
        double endAngle, double tube, PhysicsMaterial material)
    {
        Vector3D pivot = PlayfieldPoint(pivotX, pivotZ);
        Vector3D tip = PlayfieldPoint(tipX, tipZ);
        Vector3D baseDir = (tip - pivot).Normalized();
        double length = (tip - pivot).Length();
        return new Flipper(pivot, Vector3D.UnitY, baseDir, length, tube,
            restAngle: 0, endAngle: endAngle, material: material);
    }
}
