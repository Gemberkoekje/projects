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
    // Shooter lane on the right (matches PinballTableScene): a divider wall at x=4.6, the plunger at the bottom,
    // and a curved feed at the top (the LaneFeed Bézier) that sweeps a launched ball left into the playfield.
    // The divider runs z∈[DividerBottomZ, DividerTopZ]: it stops at the top *below* where the ball meets the feed
    // (LaneFeed entry z≈14.6) so the turned ball has clear passage left; and it starts *above* the slingshot
    // posts (z≈3.6) so a ball coming down the right can't wedge in the ball-narrow post↔divider gap (it drains
    // down the right outlane instead). The launched ball rides straight up (no side drift), so it needs no
    // divider low down.
    private const double DividerX = 4.6, LaneX = 5.05, DividerBottomZ = 5.0, DividerTopZ = 12.5, WallH = 1.3;

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
    /// <summary>Where a served ball rests against the plunger in the shooter lane (metres).</summary>
    public Vector3D ShooterLaneStart { get; }
    /// <summary>Auto-plunge launch speed (m/s, +z up the lane) — enough to reach the top feed and cross into play.</summary>
    public double LaunchSpeed { get; }
    /// <summary>A ball whose z falls below this (metres) — IN THE PLAYFIELD, not the shooter lane — has drained.
    /// Set to the bottom edge of the playfield (0) so the ball is only lost once it has rolled the full length of
    /// the drain chute and off the bottom, not the moment it slips past the flipper tips.</summary>
    public double DrainZ { get; }

    // A low-z ball only counts as drained while x &lt; this — keeps a ball waiting in the built-in shooter lane
    // from draining. The data-driven table passes +∞ so any low ball drains (it fell past the flippers).
    private readonly double _drainX;

    /// <summary>Builds a table from pre-assembled physics pieces — the data-driven path
    /// (<c>TableDefinition.BuildPhysicsTable</c>). Keeps this type pure physics: it just holds what it is handed.</summary>
    public PinballTable(PhysicsSettings settings, List<ICollider> colliders, Flipper leftFlipper, Flipper rightFlipper,
        Vector3D ballStart, Vector3D shooterLaneStart, double launchSpeed, double drainZ, double drainX)
    {
        Settings = settings;
        Colliders = colliders;
        LeftFlipper = leftFlipper;
        RightFlipper = rightFlipper;
        BallStart = ballStart;
        ShooterLaneStart = shooterLaneStart;
        LaunchSpeed = launchSpeed;
        DrainZ = drainZ;
        _drainX = drainX;
    }

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
            new PlaneCollider(new Vector3D(0, WallH * RenderScale, 0), -Vector3D.UnitY, PhysicsMaterial.Playfield), // plexiglass ceiling
            CylinderCollider.VerticalPost(new Vector3D(-3.9 * RenderScale, 0, 3.6 * RenderScale), postH, postR, rubber), // left slingshot post
            CylinderCollider.VerticalPost(new Vector3D(3.9 * RenderScale, 0, 3.6 * RenderScale), postH, postR, rubber),  // right slingshot post
            // Shooter-lane divider: a vertical wall at x=4.6 for z∈[DividerBottomZ, DividerTopZ]. See the const
            // note above — the ball rides straight up the lane so the divider needs neither the bottom (below
            // the slingshots, where it would wedge a returning ball) nor the very top (which would block the feed).
            new QuadCollider(new Vector3D(DividerX * RenderScale, 0, DividerBottomZ * RenderScale),
                new Vector3D(0, 0, (DividerTopZ - DividerBottomZ) * RenderScale), new Vector3D(0, WallH * RenderScale, 0), steel),
            // Feed at the top of the lane: a smooth quarter-turn Bézier guide (co-registered with the render
            // wall through LaneFeed) that catches the +z ball and sweeps it left (−x, across the divider line)
            // and down into the playfield — a curved launch rather than the old flat single-bounce deflector.
            new BezierWallCollider(LaneFeed.CurveMetres(RenderScale), 0, LaneFeed.WallHeight * RenderScale,
                LaneFeed.Segments, halfThickness: 0, steel),
            LeftFlipper,
            RightFlipper,
        };
        // Interior lane walls (right/left orbits, return guides) are authored in TableWalls and drawn by
        // PinballTableScene. They become physics colliders here — co-registered, one RenderScale apart — once
        // the layout is locked; wiring them in now conflicts with the tuned shooter-lane feed (the ball path
        // through the orbits is the "details later" pass). To enable:
        //   foreach (TableWalls.Wall w in TableWalls.All)
        //       colliders.Add(new BezierWallCollider(TableWalls.CurveMetres(w, RenderScale), 0,
        //           TableWalls.WallHeight * RenderScale, w.Segments, halfThickness: 0, steel));
        Colliders = colliders;

        BallStart = PlayfieldPoint(0.0, 6.0);       // centre, above the flippers (a mid-play spawn)
        ShooterLaneStart = PlayfieldPoint(LaneX, 1.2); // resting against the plunger, bottom of the shooter lane
        LaunchSpeed = 2.5;                          // m/s up the lane — reaches the top feed with speed to spare
        DrainZ = 0.0;                               // ball counts as lost only once it has rolled off the bottom
                                                    // edge (z<0) — i.e. all the way down the drain chute past the
                                                    // flippers — not the instant it dips below the flipper line.
        _drainX = DividerX * RenderScale;           // spare the shooter lane from the low-z drain
    }

    /// <summary>A point on the playfield at ball-rest height, from render-space X/Z (metres out).</summary>
    public static Vector3D PlayfieldPoint(double renderX, double renderZ) =>
        new(renderX * RenderScale, PhysicsConstants.BallRadius, renderZ * RenderScale);

    /// <summary>A fresh ball at rest at <see cref="BallStart"/>.</summary>
    public BallState NewBall() => BallState.AtRest(BallStart);

    /// <summary>True once the ball has drained: fallen past the flippers IN THE PLAYFIELD. The shooter lane
    /// (x > the divider) sits at low z too, so a ball waiting to launch there is not a drain — but a ball that
    /// falls off the bottom of the lane (below the plunger, z &lt; 0) is lost and counts as a drain as well.</summary>
    public bool IsDrained(in BallState ball) =>
        (ball.Position.Z < DrainZ && ball.Position.X < _drainX) || ball.Position.Z < 0;

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
