// -----------------------------------------------------------------------------
// impacts.scd - what happens when something lands.
//
// Wire-up points: Explosion.Spawn for the big one, ImpactSparks for the small
// one, and Destructible's damage-state transitions for the two structure
// sounds. The three-state model (intact / damaged / destroyed) is why there are
// two structure sounds and not one: the middle state has to be audible or the
// player cannot tell a hit that mattered from one that did not.
// -----------------------------------------------------------------------------
(
'RF_Sfx_Explosion': (
    // The full-size detonation: a vehicle dying, a rocket connecting. The
    // loudest thing in the game, and the only one with a real debris tail.
    dur: 2.20,
    channels: 1,
    graph: {
        var flash = ~rfv[\crack].(freq: 1200, dur: 0.12, res: 0.4);
        var core = ~rfv[\boom].(from: 300, to: 26, dur: 1.4, curve: -3);
        var blast = ~rfv[\air].(from: 4000, to: 120, dur: 1.1, bw: 1.5);
        var rubble = ~rfv[\debris].(dur: 1.6, density: 90, freq: 2000) * 0.7;
        ((flash * 0.7) + core + (blast * 0.5) + rubble) * 0.4;
    }
),

'RF_Sfx_Impact': (
    // A projectile hitting armour without killing it. Short and metallic - this
    // plays constantly during a firefight, so it has to stay out of the way.
    dur: 0.35,
    channels: 1,
    graph: {
        var strike = ~rfv[\crack].(freq: 3200, dur: 0.04, res: 0.2);
        var plate = ~rfv[\shell].(freq: 620, dur: 0.22, decay: 0.14);
        var spark = ~rfv[\tick].(freq: 7000, dur: 0.02) * 0.5;
        ((strike * 0.6) + plate + spark) * 0.5;
    }
),

'RF_Sfx_StructureDamaged': (
    // Intact -> Damaged. Structural rather than explosive: something inside the
    // building gave way. Deliberately duller than an explosion so the two never
    // get confused.
    dur: 1.00,
    channels: 1,
    graph: {
        var crunch = ~rfv[\crack].(freq: 500, dur: 0.18, res: 0.8);
        var settle = ~rfv[\boom].(from: 120, to: 40, dur: 0.5, curve: -4) * 0.8;
        var grit = ~rfv[\debris].(dur: 0.7, density: 40, freq: 1400) * 0.6;
        (crunch + settle + grit) * 0.42;
    }
),

'RF_Sfx_StructureDestroyed': (
    // Damaged -> Destroyed. The collapse: longer, lower, and mostly debris,
    // because the rubble is what the player actually sees appear.
    dur: 2.00,
    channels: 1,
    graph: {
        var give = ~rfv[\crack].(freq: 340, dur: 0.25, res: 1.0);
        var fall = ~rfv[\boom].(from: 90, to: 24, dur: 1.0, curve: -3);
        var collapse = ~rfv[\debris].(dur: 1.7, density: 130, freq: 1100);
        var dust = ~rfv[\air].(from: 1200, to: 200, dur: 1.4, bw: 2.0) * 0.3;
        (give + fall + collapse + dust) * 0.4;
    }
),
)
