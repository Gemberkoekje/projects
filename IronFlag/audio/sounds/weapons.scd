// -----------------------------------------------------------------------------
// weapons.scd - one sound per WeaponKind.
//
// Mirrors unity/Assets/RF/Scripts/Combat/WeaponKind.cs. Each of these fires
// from VehicleWeapon.TryFire, so they are short, front-loaded, and mixed to sit
// under an engine loop without fighting it.
//
// The design brief per weapon comes straight from the enum's own docs: the
// cannon has "the longest reach in the game", the chaingun fires "faster than
// anything else", the autocannon is "the only one with nobody behind it".
// -----------------------------------------------------------------------------
(
'RF_Sfx_WeaponGrenade': (
    // The jeep lobs these. Hollow and soft-edged, so the fastest vehicle in the
    // game does not sound like it is carrying artillery.
    dur: 0.45,
    channels: 1,
    graph: {
        var pop = ~rfv[\crack].(freq: 900, dur: 0.05, res: 0.5) * 0.5;
        var body = ~rfv[\boom].(from: 260, to: 90, dur: 0.22, curve: -5) * 0.7;
        var tube = ~rfv[\shell].(freq: 420, dur: 0.18, decay: 0.09);
        (pop + body + tube) * 0.5;
    }
),

'RF_Sfx_WeaponCannon': (
    // The tank. The loudest single report in the game, with a real tail on it.
    dur: 1.10,
    channels: 1,
    graph: {
        var snap = ~rfv[\crack].(freq: 1800, dur: 0.09, res: 0.25);
        var body = ~rfv[\boom].(from: 220, to: 34, dur: 0.75, curve: -4);
        var turret = ~rfv[\shell].(freq: 180, dur: 0.5, decay: 0.35);
        var wash = ~rfv[\air].(from: 3000, to: 200, dur: 0.55, bw: 1.2) * 0.3;
        ((snap * 0.8) + body + (turret * 0.8) + wash) * 0.42;
    }
),

'RF_Sfx_WeaponRocket': (
    // The ASV. "The heaviest single hit, and the easiest to see coming" - so it
    // is the one weapon whose sound keeps going after the trigger.
    dur: 1.30,
    channels: 1,
    graph: {
        var ignite = ~rfv[\crack].(freq: 700, dur: 0.14, res: 0.6);
        var thrust = ~rfv[\air].(from: 1800, to: 320, dur: 1.0, bw: 0.35) * 0.8;
        var kick = ~rfv[\boom].(from: 150, to: 48, dur: 0.35, curve: -3) * 0.8;
        var rail = ~rfv[\tick].(freq: 5000, dur: 0.03) * 0.6;
        ((ignite * 0.7) + thrust + kick + rail) * 0.45;
    }
),

'RF_Sfx_WeaponChaingun': (
    // The helicopter, "fired faster than anything else" - this plays many times
    // a second, so it is deliberately tiny. Anything with a tail turns into mud
    // at that rate.
    dur: 0.18,
    channels: 1,
    graph: {
        var snap = ~rfv[\crack].(freq: 2600, dur: 0.035, res: 0.2);
        var body = ~rfv[\boom].(from: 320, to: 120, dur: 0.09, curve: -6) * 0.5;
        var casing = ~rfv[\tick].(freq: 6000, dur: 0.015) * 0.4;
        ((snap * 0.9) + body + casing) * 0.5;
    }
),

'RF_Sfx_WeaponAutocannon': (
    // The automated turret. TURRETS_NOTES calls it a slow heavy gun, and it is
    // the only weapon with nobody behind it - so it is the coldest and most
    // mechanical of the five, with the servo audible before the shot.
    dur: 0.85,
    channels: 1,
    graph: {
        var servo = ~rfv[\tick].(freq: 3200, dur: 0.05) * 0.35;
        var snap = ~rfv[\crack].(freq: 1400, dur: 0.07, res: 0.22);
        var body = ~rfv[\boom].(from: 190, to: 44, dur: 0.5, curve: -4) * 0.9;
        var mount = ~rfv[\shell].(freq: 260, dur: 0.3, decay: 0.2) * 0.7;
        (servo + snap + body + mount) * 0.45;
    }
),
)
