using System;
using System.Collections.Generic;
using System.Linq;

namespace Pinball.Physics;

/// <summary>
/// The single-ball physics world (pinball-plan §6.1.5): a fixed-timestep symplectic-Euler integrator with
/// swept-sphere continuous collision (CCD) and speculative resting contacts, over a set of analytic
/// colliders. Deterministic by construction — fixed <c>h</c>, a fixed contact-processing order, all
/// double-precision, a seeded PRNG — so the §6.1.8 closed-form tests are meaningful and repeatable.
///
/// Per substep: (1) apply external forces to velocity (semi-implicit); (2) gather speculative/resting
/// contacts; (3) resolve them (restitution-thresholded normal + Coulomb friction), which is where a resting
/// ball is held still and a rolling ball is driven; (4) integrate position with CCD, catching fast approach
/// to separated colliders so nothing tunnels; (5) Baumgarte position correction of residual penetration;
/// (6) rolling resistance; (7) integrate orientation. Broadphase here is a linear scan (the table is tiny);
/// reusing the float <c>BVH</c> via <c>OverlapSweep</c> is the documented later optimisation (§6.1.2).
/// </summary>
public sealed class PhysicsWorld
{
    // Tuning (metres / iterations). Speculative margin &gt; max penetration slop so near contacts are caught.
    private const double SpeculativeMargin = 1e-3;   // contacts within 1 mm are resting/speculative
    private const double CcdSkin = 1e-6;             // conservative-advancement contact threshold
    private const double PenetrationSlop = 1e-4;     // allowed penetration before position correction
    private const double BaumgarteBeta = 0.2;        // fraction of penetration corrected per step (bounded)
    private const double RollingSlipThreshold = 0.05; // |v_contact| below this ⇒ treated as rolling
    private const int MaxCcdIterations = 8;          // outer impacts resolved per substep (§6.1.5)
    private const int MaxAdvancementIterations = 64; // inner conservative-advancement steps per collider
    private const int SolverIterations = 4;          // resting-contact velocity relaxation passes

    private readonly ICollider[] _colliders;
    private readonly IActuator[] _actuators;
    private readonly PhysicsSettings _settings;
    private readonly List<Contact> _contacts = new();
    private BallState _ball;
    private double _accumulator;

    /// <summary>Creates a world with the given settings and static/kinematic colliders.</summary>
    public PhysicsWorld(PhysicsSettings settings, IEnumerable<ICollider> colliders)
    {
        ArgumentNullException.ThrowIfNull(colliders);
        _settings = settings;
        _colliders = colliders.ToArray();
        _actuators = _colliders.OfType<IActuator>().ToArray(); // flippers/plunger advance on the physics clock
        Random = new DeterministicRandom(settings.Seed);
        Tilt = new TiltSensor();
    }

    /// <summary>The current ball state (mutated in place by <see cref="Substep"/>).</summary>
    public BallState Ball { get => _ball; set => _ball = value; }

    /// <summary>The world's colliders, in the stable order that seeds deterministic contact processing.</summary>
    public IReadOnlyList<ICollider> Colliders => _colliders;

    /// <summary>The world's deterministic PRNG (seeded from <see cref="PhysicsSettings.Seed"/>).</summary>
    public DeterministicRandom Random { get; }

    /// <summary>The tilt-bob sensor accumulating nudge energy (§6.1.6); decays every substep.</summary>
    public TiltSensor Tilt { get; }

    /// <summary>
    /// Applies a nudge (§6.1.6): an impulse <paramref name="impulse"/> (kg·m/s) to the ball (<c>Δv = J/m</c>)
    /// and a bump to the tilt sensor. The transient displacement of the whole table frame that also helps
    /// save a drain is a host-side refinement layered on top of this ball impulse.
    /// </summary>
    public void Nudge(Vector3D impulse)
    {
        _ball.LinearVelocity += impulse / PhysicsConstants.BallMass;
        Tilt.Bump(impulse.Length());
    }

    /// <summary>Number of speculative/resting contacts found in the most recent substep (diagnostics/tests).</summary>
    public int ContactCount => _contacts.Count;

    /// <summary>
    /// Advances the world by <paramref name="seconds"/> of real time using a fixed-<c>h</c> accumulator: runs
    /// whole substeps and carries the remainder, so <c>h</c> is always fixed and the result is deterministic
    /// (§6.1.5). The render loop calls this with the frame delta; tests usually call <see cref="Substep"/>.
    /// </summary>
    public void Advance(double seconds)
    {
        _accumulator += seconds;
        // Spiral-of-death guard: never try to catch up more than MaxCatchUpSeconds of real time in one call.
        // A huge dt (a stall, a paused debugger, the first frame) drops the excess rather than grinding
        // thousands of substeps and hanging; the sim briefly slows instead of freezing.
        if (_accumulator > MaxCatchUpSeconds)
            _accumulator = MaxCatchUpSeconds;
        while (_accumulator >= PhysicsConstants.SubstepSeconds)
        {
            Substep();
            _accumulator -= PhysicsConstants.SubstepSeconds;
        }
    }

    /// <summary>Max real time a single <see cref="Advance"/> will simulate (100 substeps) — the rest is dropped.</summary>
    private const double MaxCatchUpSeconds = 0.1;

    /// <summary>Advances the simulation by exactly one fixed substep <c>h</c> (§6.1.5).</summary>
    public void Substep()
    {
        double h = PhysicsConstants.SubstepSeconds;

        // 0. Advance kinematic actuators (flippers/plunger) and leak the tilt sensor, so this substep's
        //    contact sees each actuator's updated pose and surface velocity.
        for (int i = 0; i < _actuators.Length; i++)
            _actuators[i].Advance(h);
        Tilt.Decay(h);

        // 1. External forces → velocity (semi-implicit / symplectic Euler updates velocity first).
        Vector3D aExt = Forces.ExternalAcceleration(_ball, _settings);
        _ball.LinearVelocity += aExt * h;

        // 2. Gather speculative/resting contacts at the current position, in stable collider order.
        GatherContacts(_ball.Position);

        // 3. Resolve resting/speculative contacts on velocity. Applying gravity in step 1 first means a
        //    resting ball is (barely) approaching each step, so the normal impulse — and thus the friction
        //    cone — is non-zero: this is where a rolling ball spins up (v ⇄ ω) and a captured ball is held.
        for (int iter = 0; iter < SolverIterations; iter++)
            for (int k = 0; k < _contacts.Count; k++)
                ContactSolver.Resolve(ref _ball.LinearVelocity, ref _ball.AngularVelocity, _ball.Position, _contacts[k]);

        // 4. Integrate position with CCD (fast approach to *separated* colliders → no tunnelling).
        CcdAdvance(h);

        // 5. Baumgarte position correction of residual penetration (position only ⇒ injects no energy).
        DepenetratePositions();

        // 6. Rolling resistance (a real loss that sets coast distance; opposes rolling motion).
        if (_settings.EnableRollingResistance)
            ApplyRollingResistance(h);

        // 7. Orientation.
        _ball.Orientation = _ball.Orientation.IntegrateAngularVelocity(_ball.AngularVelocity, h);
    }

    private void GatherContacts(Vector3D center)
    {
        _contacts.Clear();
        double r = _settings.BallRadius;
        for (int i = 0; i < _colliders.Length; i++)
        {
            ContactQuery q = _colliders[i].Query(center, r);
            if (q.Separation < SpeculativeMargin)
                _contacts.Add(MakeContact(i, _colliders[i], q));
        }
    }

    private void CcdAdvance(double h)
    {
        double remaining = h;
        for (int iter = 0; iter < MaxCcdIterations && remaining > 1e-12; iter++)
        {
            if (!EarliestTimeOfImpact(remaining, out double toi, out Contact contact))
            {
                _ball.Position += _ball.LinearVelocity * remaining;
                return;
            }
            _ball.Position += _ball.LinearVelocity * toi;
            ContactSolver.Resolve(ref _ball.LinearVelocity, ref _ball.AngularVelocity, _ball.Position, contact);
            remaining -= toi;
        }
        // Fallback: if still not resolved after the iteration cap (a rare, degenerate multi-contact trap),
        // freeze position for the rest of this substep rather than advance — never tunnel. The speculative
        // resolve on the next substep untangles it.
    }

    private bool EarliestTimeOfImpact(double maxTime, out double toi, out Contact contact)
    {
        toi = 0;
        contact = default;
        Vector3D p = _ball.Position;
        Vector3D v = _ball.LinearVelocity;
        double speed = v.Length();
        if (speed < 1e-12)
            return false;

        double r = _settings.BallRadius;
        AabbD swept = AabbD.SweptSphere(p, p + v * maxTime, r).Expanded(CcdSkin);

        bool found = false;
        double best = maxTime;
        for (int i = 0; i < _colliders.Length; i++)
        {
            ICollider col = _colliders[i];
            if (!col.Bounds.Expanded(r).Intersects(swept))
                continue; // broadphase cull
            if (col.Query(p, r).Separation < SpeculativeMargin)
                continue; // already resting/touching — handled by the speculative solver, not CCD

            if (ConservativeToi(col, i, p, v, speed, best, r, out double candidate, out Contact candidateContact)
                && candidate < best)
            {
                best = candidate;
                contact = candidateContact;
                found = true;
            }
        }
        toi = best;
        return found;
    }

    // Conservative advancement (§6.1.5): repeatedly step forward by separation/|v|, which cannot overshoot a
    // contact, until the swept sphere first touches this collider (separation ≤ skin) or runs past maxTime.
    private static bool ConservativeToi(ICollider col, int index, Vector3D p, Vector3D v, double speed,
        double maxTime, double r, out double toi, out Contact contact)
    {
        double tau = 0;
        for (int i = 0; i < MaxAdvancementIterations; i++)
        {
            ContactQuery q = col.Query(p + v * tau, r);
            if (q.Separation <= CcdSkin)
            {
                toi = tau;
                contact = MakeContact(index, col, q);
                return true;
            }
            tau += q.Separation / speed;
            if (tau >= maxTime)
            {
                toi = 0;
                contact = default;
                return false; // won't reach this collider within the substep
            }
        }

        // Iteration cap hit while still advancing inside the substep (a grazing crawl near a tangent
        // contact). If the ball is now within the speculative margin, resolve here rather than report a
        // miss — reporting a miss would advance the full remaining step and clip through. Conservative.
        ContactQuery near = col.Query(p + v * tau, r);
        if (near.Separation < SpeculativeMargin)
        {
            toi = tau;
            contact = MakeContact(index, col, near);
            return true;
        }
        toi = 0;
        contact = default;
        return false;
    }

    private void DepenetratePositions()
    {
        double r = _settings.BallRadius;
        for (int i = 0; i < _colliders.Length; i++)
        {
            ContactQuery q = _colliders[i].Query(_ball.Position, r);
            if (q.Separation < -PenetrationSlop)
                _ball.Position += q.Normal * ((-q.Separation - PenetrationSlop) * BaumgarteBeta);
        }
    }

    private void ApplyRollingResistance(double h)
    {
        Vector3D gravityVec = _settings.GravityVector; // the actual gravity (may be incline-tilted), not (0,−g,0)
        for (int k = 0; k < _contacts.Count; k++)
        {
            Contact c = _contacts[k];
            Vector3D rc = c.Point - _ball.Position;
            Vector3D vc = _ball.LinearVelocity + Vector3D.Cross(_ball.AngularVelocity, rc) - c.SurfaceVelocity;
            if (vc.Length() >= RollingSlipThreshold)
                continue; // sliding, not rolling — Coulomb friction already handled it

            double normalLoad = Math.Max(0.0, -PhysicsConstants.BallMass * Vector3D.Dot(gravityVec, c.Normal));
            // Scaling both v and ω to preserve rolling removes translational + rotational KE together; the
            // (5/7) factor makes the total energy loss equal the physical rolling-resistance work C_rr·N·dx
            // (a plain C_rr·N/m over-damps by 7/5). Drill spin about the normal is over-damped slightly — a
            // known minor simplification; rolling ω is almost entirely about the roll axis.
            double decel = (5.0 / 7.0) * c.Material.RollingResistance * normalLoad / PhysicsConstants.BallMass;
            double speed = _ball.LinearVelocity.Length();
            if (speed > 1e-9)
            {
                double scale = Math.Max(0.0, (speed - Math.Min(decel * h, speed)) / speed);
                _ball.LinearVelocity *= scale;   // scale v and ω together so the rolling constraint is preserved
                _ball.AngularVelocity *= scale;
            }
            return; // apply once (the playfield contact)
        }
    }

    private static Contact MakeContact(int index, ICollider col, in ContactQuery q)
    {
        double activeImpulse = 0, triggerSpeed = 0;
        if (col is IActiveImpulse active)
        {
            activeImpulse = active.ImpulseStrength;
            triggerSpeed = active.TriggerSpeed;
        }
        return new Contact(index, q.Normal, q.ContactPoint, q.Separation,
            col.SurfaceVelocity(q.ContactPoint), col.Material, activeImpulse, triggerSpeed);
    }
}
