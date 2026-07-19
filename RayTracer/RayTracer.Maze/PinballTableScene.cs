using System.Numerics;
using RayTracer;

namespace RayTracer.Gpu;

/// <summary>
/// Space Cadet RT — the P0 static playfield (pinball-plan §3.3 / §5.3 / §8 P0), the <b>single source of
/// truth</b> for the table. A clean-room recreation of the *3D Pinball for Windows – Space Cadet* layout
/// (no original assets): the three vertical zones — Drain (flippers / plunger), Launch (ramp / lanes /
/// target banks) and Re-entry (attack bumpers / wormholes / gravity well) — authored as render meshes
/// (quads + analytic spheres) with spectral materials. The same part records will emit the co-registered
/// analytic colliders in P5, so render and physics cannot drift.
///
/// <para>Table space: <c>x</c> is across the table (left −, right +), <c>z</c> runs up-table (flipper end
/// at <c>z≈0</c>, re-entry at the far <c>z</c>), <c>y</c> is up out of the playfield (the P0 render keeps
/// the surface flat; the §6.1 6.5° incline is a physics concern for P5). Everything is deterministic — no
/// RNG — so a converged capture reproduces bit-exact on the same GPU.</para>
///
/// <para>Exercises the shipped Phase 6 spectral path plus the new <see cref="SurfaceKind.Emissive"/> term:
/// glowing neon inserts and wormholes, a chrome ball reflecting them, dispersive glass jewels, anodized
/// mirror rails, and oil-slick thin-film bumper caps. No gameplay and no movers — this is the static half
/// of the hybrid, the payoff of reusing the converging renderer.</para>
/// </summary>
internal sealed record PinballTableScene(
    IReadOnlyList<Tracable> Tracables,
    PackedScene Packed,
    SpectralResources Spectral,
    PackedLights PackedLights,
    Camera Camera)
{
    // Table extents (world units). Portrait, flipper end near z=0.
    private const float MinX = -5.5f, MaxX = 5.5f, MinZ = 0f, MaxZ = 24f, WallH = 1.3f;

    public static PinballTableScene Build(int width, int height)
    {
        var s = new List<Tracable>();
        var lights = new List<Light>();

        // ── Material palette (spectral-native; neon colour is the §5.3 emission term) ─────────────────
        var playfield = ColoredDiffuse("pf", new Vector3(0.11f, 0.13f, 0.20f));      // dark space-blue surface
        var apron = ColoredDiffuse("apron", new Vector3(0.05f, 0.05f, 0.08f));       // matte apron / channels
        var wall = ColoredDiffuse("wall", new Vector3(0.14f, 0.14f, 0.19f));         // plastic side walls
        var chromeRail = FlatMirror("rail", 0.82f);                                  // anodized rail caps
        var chrome = FlatMirror("chrome", 0.95f);                                    // the ball / plunger tip
        var rubber = ColoredDiffuse("rubber", new Vector3(0.04f, 0.04f, 0.05f));     // posts / bumper skirts
        var flipperRed = ColoredDiffuse("flip", new Vector3(0.9f, 0.12f, 0.08f));    // the two red flippers
        var targetWhite = ColoredDiffuse("tgt", new Vector3(0.72f, 0.74f, 0.80f));   // drop / spot targets
        var rampPurple = ColoredDiffuse("ramp", new Vector3(0.34f, 0.12f, 0.52f));   // the purple launch ramp
        var bumperBody = ColoredDiffuse("bump", new Vector3(0.55f, 0.57f, 0.62f));   // pop-bumper skirts

        var neonCyan = MaterialData.Emissive("n-cyan", new Vector3(0.05f, 0.9f, 1.0f), 1.5f);
        var neonAmber = MaterialData.Emissive("n-amber", new Vector3(1.0f, 0.55f, 0.08f), 1.5f);
        var neonBlue = MaterialData.Emissive("n-blue", new Vector3(0.2f, 0.35f, 1.0f), 1.5f);
        var neonMagenta = MaterialData.Emissive("n-mag", new Vector3(1.0f, 0.1f, 0.7f), 1.5f);
        var glowGreen = MaterialData.Emissive("wh-green", new Vector3(0.2f, 1.0f, 0.35f), 1.6f);
        var glowRed = MaterialData.Emissive("wh-red", new Vector3(1.0f, 0.12f, 0.12f), 1.6f);
        var glowYellow = MaterialData.Emissive("wh-yellow", new Vector3(1.0f, 0.85f, 0.15f), 1.6f);

        // Clear glass jewel (gravity-well centre) — dispersive dielectric, faint cool tint with depth.
        var glassJewel = new MaterialData(
            "jewel", "Jewel", null, null, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
            spectralData: null, surface: SurfaceKind.Dielectric, transmission: 0.98f, cauchyA: 1.52f, cauchyB: 0.005f,
            absorptionRgb: new Vector3(0.5f, 0.25f, 0.1f));
        // Oil-slick thin-film pop-bumper caps.
        var filmCap = new MaterialData(
            "cap", "Cap", null, null, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
            spectralData: FlatSpectrum(0.9f), surface: SurfaceKind.ThinFilm, cauchyA: 1.35f, filmThicknessNm: 360f,
            filmSpatialAmpNm: 70f, filmContrast: 1f);

        // ── Playfield surface + perimeter ─────────────────────────────────────────────────────────────
        s.Add(FloorQuad(0f, MinX, MaxX, MinZ, MaxZ, playfield));
        s.Add(WallZ(MaxZ, MinX, MaxX, 0f, WallH + 0.6f, wall));                 // back wall (taller = backbox)
        s.Add(WallX(MinX, MinZ, MaxZ, 0f, WallH, wall)); s.Add(WallX(MaxX, MinZ, MaxZ, 0f, WallH, wall));
        // Thin anodized rail caps along the two side walls (mirror strip near the top of each wall).
        s.Add(WallX(MinX + 0.03f, MinZ, MaxZ, WallH - 0.18f, WallH, chromeRail));
        s.Add(WallX(MaxX - 0.03f, MinZ, MaxZ, WallH - 0.18f, WallH, chromeRail));

        // ══ DRAIN ZONE (z 0–5): flippers, inlanes/outlanes, apron, plunger lane ═══════════════════════
        // Apron across the very bottom, angled channels funnelling to the central drain.
        s.Add(FloorQuad(0.01f, -1.1f, 1.1f, 0f, 1.4f, apron));                  // drain mouth
        AddRotBox(s, new Vector3(-2.4f, 0.35f, 2.2f), new Vector3(1.5f, 0.35f, 0.05f), 0.5f, apron);  // left inlane wall
        AddRotBox(s, new Vector3(2.4f, 0.35f, 2.2f), new Vector3(1.5f, 0.35f, 0.05f), -0.5f, apron);  // right inlane wall
        // Slingshot faces above the flippers (anodized), and outlane kickback posts.
        AddRotBox(s, new Vector3(-3.0f, 0.3f, 4.2f), new Vector3(0.9f, 0.3f, 0.06f), 0.7f, chromeRail);
        AddRotBox(s, new Vector3(3.0f, 0.3f, 4.2f), new Vector3(0.9f, 0.3f, 0.06f), -0.7f, chromeRail);
        AddPost(s, new Vector3(-3.9f, 0f, 3.6f), 0.12f, 0.7f, rubber);
        AddPost(s, new Vector3(3.9f, 0f, 3.6f), 0.12f, 0.7f, rubber);
        // Two lower red flippers (no upper flipper). Resting angle: tips down-and-out toward the outlanes.
        AddFlipper(s, new Vector3(-1.5f, 0.22f, 3.4f), new Vector3(-2.7f, 0.22f, 2.5f), 0.28f, flipperRed);
        AddFlipper(s, new Vector3(1.5f, 0.22f, 3.4f), new Vector3(2.7f, 0.22f, 2.5f), 0.28f, flipperRed);
        // Inlane rollover inserts (cyan) just above the flippers.
        s.Add(FloorQuad(0.02f, -2.5f, -1.7f, 4.6f, 5.2f, neonCyan));
        s.Add(FloorQuad(0.02f, 1.7f, 2.5f, 4.6f, 5.2f, neonCyan));

        // Plunger / shooter lane: right channel, divided from the playfield, with the chrome plunger.
        s.Add(WallX(4.6f, 0f, 15f, 0f, WallH, wall));                           // lane divider wall
        s.Add(FloorQuad(0.005f, 4.7f, MaxX, 0f, 15f, apron));                   // lane floor
        AddCyl(s, new Vector3(5.05f, 0f, 1.1f), 0.28f, 0.55f, 14, chrome);      // plunger tip (rest)
        s.Add(new Sphere(new Vector3(5.05f, 0.62f, 1.1f), 0.28f, chrome));

        // ══ LAUNCH ZONE (z 5–14): ramp, launch lanes + engine bumpers, target banks ═══════════════════
        // Purple launch ramp rising mid-left (an inclined deck + side walls + a lit purple lip).
        AddRamp(s, -2.7f, 6.5f, 10.2f, 1.25f, 1.0f, rampPurple, neonMagenta);
        // Three engine ("launch") bumpers clustered mid-left under the launch lanes.
        AddBumper(s, new Vector3(-3.4f, 0f, 9.0f), 0.42f, 0.45f, bumperBody, neonBlue);
        AddBumper(s, new Vector3(-3.8f, 0f, 10.6f), 0.42f, 0.45f, bumperBody, neonBlue);
        AddBumper(s, new Vector3(-2.9f, 0f, 11.2f), 0.42f, 0.45f, bumperBody, neonBlue);
        // Mission target bank: 3 spot targets in a centre row (select the mission).
        for (int i = 0; i < 3; i++)
            AddTarget(s, new Vector3(-0.9f + i * 0.9f, 0f, 7.4f), 0.55f, 0.5f, 0f, targetWhite);
        // Left & right Hazard banks: 3 spot targets each, angled toward centre.
        for (int i = 0; i < 3; i++) AddTarget(s, new Vector3(-4.4f, 0f, 7.6f + i * 0.9f), 0.5f, 0.5f, 1.2f, targetWhite);
        for (int i = 0; i < 3; i++) AddTarget(s, new Vector3(4.3f, 0f, 7.6f + i * 0.9f), 0.5f, 0.5f, -1.2f, targetWhite);
        // Booster drop-target bank (right) and Medal drop targets (centre-right).
        for (int i = 0; i < 3; i++) AddTarget(s, new Vector3(2.6f + i * 0.55f, 0f, 12.4f), 0.42f, 0.55f, 0.2f, targetWhite);
        for (int i = 0; i < 3; i++) AddTarget(s, new Vector3(1.4f, 0f, 5.8f + i * 0.7f), 0.42f, 0.5f, -0.9f, targetWhite);
        // Science drop-target bank: 9 targets, a 3×3 block centre-left.
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                AddTarget(s, new Vector3(-1.4f + c * 0.62f, 0f, 12.2f + r * 0.55f), 0.5f, 0.45f, 0f, targetWhite);
        // Fuel-target ring inserts (amber) around the remote bumper zone (upper-left).
        s.Add(FloorQuad(0.02f, -4.9f, -3.9f, 15.5f, 18.5f, neonAmber));

        // ══ RE-ENTRY ZONE (z 14–24): attack bumpers, wormholes, gravity well, re-entry lanes ══════════
        // Attack bumpers ("weapons"): 3 upper-centre in a triangle + 1 remote upper-left.
        AddBumper(s, new Vector3(0f, 0f, 16.4f), 0.5f, 0.5f, bumperBody, filmCap);
        AddBumper(s, new Vector3(-1.4f, 0f, 15.2f), 0.5f, 0.5f, bumperBody, filmCap);
        AddBumper(s, new Vector3(1.4f, 0f, 15.2f), 0.5f, 0.5f, bumperBody, filmCap);
        AddBumper(s, new Vector3(-3.9f, 0f, 18.6f), 0.5f, 0.5f, bumperBody, filmCap);   // remote bumper

        // Three coloured wormholes at the upper corners / mid-right (emissive glowing domes).
        AddWormhole(s, lights, new Vector3(-4.1f, 0f, 20.6f), 0.6f, glowGreen, rubber);   // green (top-left)
        AddWormhole(s, lights, new Vector3(4.1f, 0f, 21.0f), 0.6f, glowRed, rubber);       // red (top-right)
        AddWormhole(s, lights, new Vector3(4.3f, 0f, 13.6f), 0.55f, glowYellow, rubber);   // yellow (mid-right)

        // Hyperspace chute / kickout (top-right) and Space-Warp spot target left of it.
        AddCyl(s, new Vector3(3.3f, 0f, 22.6f), 0.45f, 0.2f, 14, rubber);
        s.Add(FloorQuad(0.02f, 2.9f, 3.7f, 22.4f, 23.4f, neonAmber));                     // kickout insert
        AddTarget(s, new Vector3(1.9f, 0f, 22.2f), 0.5f, 0.5f, 0.3f, targetWhite);        // space-warp target

        // Gravity Well: the centre light ring — two concentric rings of emissive inserts (the middle_circle
        // rank lights + the outer_circle progress ring) around a glass jewel in a chrome cup.
        var wellCentre = new Vector3(0f, 0f, 19.2f);
        AddCyl(s, wellCentre, 0.55f, 0.18f, 16, chromeRail);                              // the well cup
        s.Add(new Sphere(wellCentre + new Vector3(0f, 0.35f, 0f), 0.4f, glassJewel));     // the jewel
        AddRing(s, wellCentre, 0.95f, 9, 0.07f, neonAmber);                               // middle_circle (rank)
        AddRing(s, wellCentre, 1.45f, 18, 0.06f, neonBlue);                               // outer_circle (progress)
        lights.Add(new Light { Position = wellCentre + new Vector3(0f, 1.6f, 0f), Color = new Vector3(0.35f, 0.3f, 0.5f) });

        // Re-entry lanes (Out / Return / Bonus) across the very top, split by anodized lane guides,
        // each with a rollover insert.
        for (int i = 0; i < 4; i++) s.Add(WallX(-2.4f + i * 1.6f, 22.6f, MaxZ, 0f, 0.5f, chromeRail));
        for (int i = 0; i < 3; i++) s.Add(FloorQuad(0.02f, -2.2f + i * 1.6f, -1.0f + i * 1.6f, 23.2f, 23.9f, neonCyan));

        // A pair of flag/spinner gates at the lane entries (thin upright anodized blades).
        AddRotBox(s, new Vector3(-1.6f, 0.35f, 13.6f), new Vector3(0.4f, 0.32f, 0.03f), 1.5f, chromeRail);
        AddRotBox(s, new Vector3(1.6f, 0.35f, 13.6f), new Vector3(0.4f, 0.32f, 0.03f), -1.5f, chromeRail);

        // The chrome ball, resting in the shooter lane's feed (near the right inlane). Tagged Dynamic
        // (pinball-plan §4.1): even standing still in P0/P1 it takes the cheap mover tier — a 1-bounce
        // mirror of the converged static table + a hard contact shadow — and soft-caps at MotionSampleCap.
        s.Add(new Sphere(new Vector3(-2.1f, 0.36f, 5.6f), 0.36f, chrome) { Dynamic = true });

        // ── Lighting: dim overhead keys + warm backbox + insert cast-lights (the emissive parts supply
        // the glow and the ball's reflection; these give clean coloured illumination on the playfield). ─
        lights.Add(new Light { Position = new Vector3(0f, 11f, 6f), Color = new Vector3(0.66f, 0.66f, 0.74f) });
        lights.Add(new Light { Position = new Vector3(0f, 10f, 16f), Color = new Vector3(0.6f, 0.6f, 0.7f) });
        lights.Add(new Light { Position = new Vector3(0f, 5f, 3f), Color = new Vector3(0.45f, 0.4f, 0.45f) }); // flipper fill
        lights.Add(new Light { Position = new Vector3(0f, 6f, 23.5f), Color = new Vector3(0.5f, 0.4f, 0.7f) }); // backbox
        lights.Add(new Light { Position = new Vector3(-2.6f, 1.4f, 8.5f), Color = new Vector3(0.3f, 0.13f, 0.42f) }); // ramp
        lights.Add(new Light { Position = new Vector3(-3.4f, 1.2f, 10f), Color = new Vector3(0.15f, 0.25f, 0.7f) }); // engines
        lights.Add(new Light { Position = new Vector3(0f, 1.5f, 16f), Color = new Vector3(0.5f, 0.5f, 0.55f) }); // attack bumpers

        // ── Camera: the classic Space Cadet view — from behind the flippers, looking up the table ──────
        Vector3 camPos = new(0f, 11.5f, -8.5f);
        var camera = new Camera
        {
            Position = camPos,
            Rotation = LookRotation(camPos, new Vector3(0f, 0f, 12.5f), new Vector3(0f, 1f, 0f)),
            Fov = MathF.PI / 3.4f,
            Aspect = (float)width / height,
            ImgPlaneZ = 1f,
        };

        PackedScene packed = GpuScenePacker.Pack(s);
        SpectralResources spectral = SpectralResourceBaker.Bake(new WavelengthLookup(), packed.Materials);
        PackedLights packedLights = LightPacker.Pack(lights);
        return new PinballTableScene(s, packed, spectral, packedLights, camera);
    }

    // ══ Part builders (the single-source-of-truth authoring vocabulary) ══════════════════════════════

    private static TracableRectangle Quad(Vector3 a, Vector3 b, Vector3 c, MaterialData m) => new((a, b, c), m);

    // A horizontal quad at height y spanning [x0,x1] × [z0,z1].
    private static TracableRectangle FloorQuad(float y, float x0, float x1, float z0, float z1, MaterialData m)
        => new((new Vector3(x0, y, z0), new Vector3(x1, y, z0), new Vector3(x0, y, z1)), m);

    // A vertical quad in the z = zPlane plane spanning [x0,x1] × [y0,y1].
    private static TracableRectangle WallZ(float zPlane, float x0, float x1, float y0, float y1, MaterialData m)
        => new((new Vector3(x0, y0, zPlane), new Vector3(x1, y0, zPlane), new Vector3(x0, y1, zPlane)), m);

    // A vertical quad in the x = xPlane plane spanning [z0,z1] × [y0,y1].
    private static TracableRectangle WallX(float xPlane, float z0, float z1, float y0, float y1, MaterialData m)
        => new((new Vector3(xPlane, y0, z0), new Vector3(xPlane, y0, z1), new Vector3(xPlane, y1, z0)), m);

    // Rotate a point about the world Y axis through the origin.
    private static Vector3 RotY(Vector3 d, float rad)
    {
        float c = MathF.Cos(rad), sn = MathF.Sin(rad);
        return new Vector3(d.X * c + d.Z * sn, d.Y, -d.X * sn + d.Z * c);
    }

    // An axis-aligned box (about Y) rotated by <paramref name="rad"/>, as 6 quads. Face normals are
    // auto-oriented against the ray, so winding does not matter.
    private static void AddRotBox(List<Tracable> s, Vector3 c, Vector3 h, float rad, MaterialData m)
    {
        Vector3 Corner(float sx, float sy, float sz) => c + RotY(new Vector3(sx * h.X, sy * h.Y, sz * h.Z), rad);
        // -Z / +Z
        s.Add(Quad(Corner(-1, -1, -1), Corner(1, -1, -1), Corner(-1, 1, -1), m));
        s.Add(Quad(Corner(-1, -1, 1), Corner(1, -1, 1), Corner(-1, 1, 1), m));
        // -X / +X
        s.Add(Quad(Corner(-1, -1, -1), Corner(-1, -1, 1), Corner(-1, 1, -1), m));
        s.Add(Quad(Corner(1, -1, -1), Corner(1, -1, 1), Corner(1, 1, -1), m));
        // -Y / +Y
        s.Add(Quad(Corner(-1, -1, -1), Corner(1, -1, -1), Corner(-1, -1, 1), m));
        s.Add(Quad(Corner(-1, 1, -1), Corner(1, 1, -1), Corner(-1, 1, 1), m));
    }

    // A cylinder wall (ring of quads) about the Y axis, base at <paramref name="baseC"/>, rising <paramref name="h"/>.
    private static void AddCyl(List<Tracable> s, Vector3 baseC, float r, float h, int sides, MaterialData m)
    {
        for (int i = 0; i < sides; i++)
        {
            float a0 = MathF.Tau * i / sides, a1 = MathF.Tau * (i + 1) / sides;
            Vector3 p0 = baseC + new Vector3(r * MathF.Cos(a0), 0f, r * MathF.Sin(a0));
            Vector3 p1 = baseC + new Vector3(r * MathF.Cos(a1), 0f, r * MathF.Sin(a1));
            s.Add(Quad(p0, p1, p0 + new Vector3(0f, h, 0f), m));
        }
    }

    // A pop bumper: a skirt cylinder, a bright emissive rim band, and a domed cap on top.
    private static void AddBumper(List<Tracable> s, Vector3 baseC, float r, float h, MaterialData body, MaterialData cap)
    {
        AddCyl(s, baseC, r, h * 0.6f, 14, body);
        AddCyl(s, baseC + new Vector3(0f, h * 0.6f, 0f), r * 1.02f, h * 0.18f, 14, cap); // lit rim band
        s.Add(new Sphere(baseC + new Vector3(0f, h * 0.75f, 0f), r * 0.95f, cap));       // mushroom cap
    }

    // A rubber post: a thin skirt cylinder with a rounded cap.
    private static void AddPost(List<Tracable> s, Vector3 baseC, float r, float h, MaterialData m)
    {
        AddCyl(s, baseC, r, h, 10, m);
        s.Add(new Sphere(baseC + new Vector3(0f, h, 0f), r, m));
    }

    // An upright target standing on the playfield, its face rotated by <paramref name="rad"/> about Y.
    private static void AddTarget(List<Tracable> s, Vector3 baseC, float w, float h, float rad, MaterialData face)
        => AddRotBox(s, baseC + new Vector3(0f, h * 0.5f, 0f), new Vector3(w * 0.5f, h * 0.5f, 0.05f), rad, face);

    // A flat flipper bat: a top quad between the pivot and tip, rounded by an end sphere at each.
    private static void AddFlipper(List<Tracable> s, Vector3 pivot, Vector3 tip, float w, MaterialData m)
    {
        Vector3 dir = Vector3.Normalize(tip - pivot);
        Vector3 perp = new Vector3(-dir.Z, 0f, dir.X) * w;
        s.Add(Quad(pivot - perp, pivot + perp, tip - perp, m));
        s.Add(new Sphere(pivot, w, m));
        s.Add(new Sphere(tip, w * 0.65f, m));
    }

    // An inclined launch ramp at x=<paramref name="x"/> rising from z0 (on the playfield) to z1 (height y1),
    // with two side walls and a lit lip along the top edge.
    private static void AddRamp(List<Tracable> s, float x, float z0, float z1, float y1, float wHalf, MaterialData deck, MaterialData lip)
    {
        Vector3 a = new(x - wHalf, 0.03f, z0), b = new(x + wHalf, 0.03f, z0), cLo = new(x - wHalf, y1, z1);
        s.Add(Quad(a, b, cLo, deck));                                                    // the inclined deck
        s.Add(Quad(new Vector3(x - wHalf, 0.03f, z0), new Vector3(x - wHalf, y1, z1), new Vector3(x - wHalf, 0.33f, z0), deck)); // left wall
        s.Add(Quad(new Vector3(x + wHalf, 0.03f, z0), new Vector3(x + wHalf, y1, z1), new Vector3(x + wHalf, 0.33f, z0), deck)); // right wall
        s.Add(Quad(new Vector3(x - wHalf, y1, z1), new Vector3(x + wHalf, y1, z1), new Vector3(x - wHalf, y1 + 0.12f, z1), lip)); // lit lip
    }

    // A coloured wormhole: a dark recessed rim with a glowing emissive dome + a co-located cast light.
    private static void AddWormhole(List<Tracable> s, List<Light> lights, Vector3 c, float r, MaterialData glow, MaterialData rim)
    {
        AddCyl(s, c, r * 1.15f, 0.18f, 14, rim);
        s.Add(new Sphere(c + new Vector3(0f, 0.05f, 0f), r, glow));
        lights.Add(new Light { Position = c + new Vector3(0f, 1.1f, 0f), Color = glowColor(glow) });
    }

    // A ring of <paramref name="count"/> small emissive insert spheres (a rank / progress light ring).
    private static void AddRing(List<Tracable> s, Vector3 c, float radius, int count, float r, MaterialData m)
    {
        for (int i = 0; i < count; i++)
        {
            float a = MathF.Tau * i / count;
            s.Add(new Sphere(c + new Vector3(radius * MathF.Cos(a), 0.05f, radius * MathF.Sin(a)), r, m));
        }
    }

    // A soft cast-light colour derived from an emissive insert's hue (dimmed so it fills, not blows out).
    private static Vector3 glowColor(MaterialData glow) => glow.Id switch
    {
        "wh-green" => new Vector3(0.15f, 0.7f, 0.25f),
        "wh-red" => new Vector3(0.7f, 0.12f, 0.12f),
        "wh-yellow" => new Vector3(0.7f, 0.6f, 0.12f),
        _ => new Vector3(0.4f, 0.4f, 0.4f),
    };

    // ── Material helpers (kept local so the table is a single source of truth) ───────────────────────

    private static MaterialData FlatMirror(string id, float reflectance)
        => new(id, id, null, null, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, FlatSpectrum(reflectance), SurfaceKind.Mirror);

    private static MaterialData ColoredDiffuse(string id, Vector3 rgb)
    {
        const int min = 360, max = 830, step = 5;
        int count = (max - min) / step + 1;
        var wavelengths = new int[count];
        var values = new float[count];
        for (int i = 0; i < count; i++)
        {
            int w = min + i * step;
            wavelengths[i] = w;
            float r = MathF.Exp(-MathF.Pow((w - 620f) / 70f, 2f));
            float g = MathF.Exp(-MathF.Pow((w - 540f) / 60f, 2f));
            float b = MathF.Exp(-MathF.Pow((w - 460f) / 55f, 2f));
            values[i] = Math.Clamp(rgb.X * r + rgb.Y * g + rgb.Z * b, 0f, 1f);
        }
        return new MaterialData(id, id, null, null, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
            new SpectralData(wavelengths, values, (float[])values.Clone()), SurfaceKind.Diffuse);
    }

    private static SpectralData FlatSpectrum(float value)
    {
        const int min = 360, max = 830, step = 5;
        int count = (max - min) / step + 1;
        var wavelengths = new int[count];
        var values = new float[count];
        for (int i = 0; i < count; i++)
        {
            wavelengths[i] = min + i * step;
            values[i] = value;
        }
        return new SpectralData(wavelengths, values, (float[])values.Clone());
    }

    private static Quaternion LookRotation(Vector3 pos, Vector3 target, Vector3 up)
    {
        Vector3 f = Vector3.Normalize(target - pos);
        Vector3 r = Vector3.Normalize(Vector3.Cross(up, f));
        Vector3 u = Vector3.Cross(f, r);
        var m = new Matrix4x4(r.X, r.Y, r.Z, 0f, u.X, u.Y, u.Z, 0f, f.X, f.Y, f.Z, 0f, 0f, 0f, 0f, 1f);
        return Quaternion.CreateFromRotationMatrix(m);
    }
}
