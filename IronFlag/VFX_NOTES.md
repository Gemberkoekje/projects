# Combat & Movement VFX

**To understand this, start by reading
[`MuzzleFlash.cs`](unity/Assets/RF/Scripts/Combat/MuzzleFlash.cs) and
[`ImpactSparks.cs`](unity/Assets/RF/Scripts/Combat/ImpactSparks.cs) for the hand-coded half,
then [`ParticleRig.cs`](unity/Assets/RF/Editor/ArtPipeline/ParticleRig.cs) and
[`VfxPrefabBuilder.cs`](unity/Assets/RF/Editor/Gameplay/VfxPrefabBuilder.cs) for the particle
half. Then the two preview scenes, which are how you look at any of it.** Everything else is
wiring.

This is all six items of
[MASTER_PLAN.md § 5](MASTER_PLAN.md#5-combat--movement-vfx), shipped in two passes: the two
deterministic ones first, then — once the standing question about particle systems was
answered *yes* — the four cosmetic ones.

| Effect | How it is made | Fires when |
|---|---|---|
| Muzzle flash | Closed-form, one primitive | Every shot, from `VehicleWeapon.TryFire` |
| Impact sparks | Closed-form, seven primitives | A direct hit the target survives |
| Damage smoke | ParticleSystem | Anything below half health, vehicle or building |
| Wreck column | ParticleSystem | The moment it is destroyed, detached from the wreck |
| Dust trail | ParticleSystem | A ground vehicle moving, scaled by speed and by surface |
| Water splash | ParticleSystem | A round that goes off in the sea, instead of a fireball |

**Look at it**: `Tools > IronFlag > Build Combat VFX Preview Scene` and
`Build Particle VFX Preview Scene`, or headless via
`-executeMethod IronFlag.Editor.Gameplay.CombatVfxPreviewScene.RenderToFile -vfxOutput <path>`
and `ParticleVfxPreviewScene.RenderToFile -particleOutput <path>`. Committed evidence:
`vfx-strips.png` and `vfx-particles.png`.

---

## The particle decision, and how the old objection was answered

The project refused particle systems for nine milestones, and the reason is written in
`DebrisBurst.cs`: *a particle system is an asset nobody can review in a diff.* That objection
was **answered rather than waived**.

[`ParticleRig.Create`](unity/Assets/RF/Editor/ArtPipeline/ParticleRig.cs) is the only code in
the project that touches Unity's particle module API. Everything else asks it for a system
described by a `ParticleRig.Look` — a dozen named numbers: tint, opacity, lifetime, start
size, growth, rate, burst, speed, fall, radius, cone angle, flat. All five `Look`s in the game
sit in one file, [`VfxPrefabBuilder.cs`](unity/Assets/RF/Editor/Gameplay/VfxPrefabBuilder.cs),
where they can be read against each other exactly the way `SurfaceTuning`'s rows are. **What a
diff shows is the numbers.** A `.prefab` full of serialised curves would have shown nothing.

Three consequences worth knowing:

- **Mesh particles, not billboards.** Every particle is a sphere drawn flat, which is what the
  rest of this game is made of. A soft photographic puff would be the one thing in the frame
  that came from somewhere else — and it would need a texture, which this project also does
  not have.
- **One material for all of it.** `RF_Particle` is plain white and transparent; the shader
  multiplies by each particle's own colour, so an effect's colour is a field rather than an
  asset. That is also the only reason a dust trail can take its colour from the ground it is
  being kicked off.
- **Everything simulates in world space.** Smoke that follows the wreck it came off and dust
  that follows the wheel that raised it are the same bug, and it is the default.

## Two premises of the plan were wrong

**`Explosion` was never kill-only.** The plan describes impact sparks as feedback *"distinct
from the kill-only `Explosion`"*. `Projectile.Detonate` spawns one on every detonation — a
hit, a miss into the dirt, and a round that ran out of range all drew the same expanding ball.
So the gap was not "kills are loud and non-kills are quiet"; it was that **nothing
distinguished a hit from a miss at all**. Sparks now fire only on a direct hit whose target is
still standing after taking the damage, which makes them mean one specific thing: *connected,
still there*.

**There is no such thing as a vehicle moving through shallow water.** The plan asks for "a
small persistent wake/foam behind vehicles moving through shallow water". Both water rows in
`SurfaceTuning` have `Drowns = true`, and `WaterLine.Update` self-destructs anything that
crosses the line — so a vehicle in water is a vehicle sinking, and a cheerful wake behind it
would be the wrong feeling entirely. The splash-on-impact half of that item shipped; the wake
half is void, not deferred.

A third, smaller correction: the plan scopes sparks to *"`METAL`-palette surfaces"*. They fire
off anything that implements `IDamageable` and survives, walls included. A hit is a hit, and a
surface test would be a second rule that can disagree with the first.

## Decisions worth knowing

**`MuzzleFlash.Flare` is deliberately the inverse of `Explosion.Scale`.** A detonation swells
then fades because something is expanding; a flash is already at full extent before the eye
finds it, because the propellant has finished burning by the time the round clears the barrel.
`CombatRulesTests.AFlashCollapsesWhereAnExplosionSwells` asserts the pair against each other,
because the *contrast* is the design.

**The flash hangs off the muzzle; the sparks stand in the world.** 65 ms is long enough for a
strafing helicopter to travel a metre and a half, and a flash left behind in the air is
visibly detached from the gun. Sparks came off something standing still enough to be hit.

**One `DamageSmoke` owns both the plume and the column**, because they are one story at two
volumes: a hull starts smoking at half health, keeps smoking while it is standing, and stops
when there is nothing left to smoke — and the moment it stops is the moment the column goes
up. They cannot overlap or leave a gap, and neither can be forgotten separately.

**It polls `IDamageable.Fraction` rather than subscribing.** `VehicleHealth` raises events for
death and repair and nothing for being hit; adding a third event so that smoke could hear
about it would put a cosmetic concern into the damage model, which that class's own remarks
argue against. `Fraction` is new on `IDamageable` — both implementers already had the property.

**The wreck column is spawned detached.** A wrecked vehicle is taken off the field and sent
home within the second, and smoke parented to it would leave with it — so the one effect whose
job is to mark where something died must stop belonging to the thing that died. A PlayMode
test disables the hull and checks the column is still there.

**Dust is derived from the surface table, not added to it.** The colour is the ground's own
lifted towards white; the *amount* is read off `SurfaceTuning.Grip`, because soft ground is
loose ground — the same property that costs the jeep a fifth of its top speed on a beach is
the property that puts the beach in the air behind it. It falls out at a full cloud on sand,
two fifths over grass, a quarter on asphalt, and nothing on water (`Drowns`, asked once). Three
numbers nobody had to write down, off a table balanced for something else entirely.

**The splash replaces the fireball rather than joining it.** A shell landing in the sea and one
landing on the beach beside it drew the same orange explosion, which is simply wrong: the sea
is the darkest thing on the map and the one place fire cannot happen.

**`PoseAt` exists on all five effects so a still can show a curve.** Neither `Update` nor a
particle system ticks outside play mode, so an effect dropped into a generated scene is
invisible in exactly the picture that exists to show it. The preview scenes spawn everything
through the real `Spawn` methods off the real prefabs bound to a real tank, and only pose it.

## Gotchas

**A negative `gravityModifier` accelerates for ever.** The first wreck column used `-0.5`,
which is 4.9 m/s² of *upward* acceleration — by three seconds the smoke was thirty metres up
and visibly detached from the wreck it came off. Smoke wants a large initial speed and almost
no buoyancy (`-0.03`), not the other way round. This is the single easiest thing to get wrong
here and it is invisible in the numbers.

**The muzzle flash's point light will eat the whole effect.** The first pass used an
explosion's numbers — intensity 14, range four barrel-lengths — and floodlit the tank's entire
flank; the flame itself was a dot lost inside it. It is 2.5 at 1.6 barrel-lengths now,
measured off a render twice.

**URP's plain `Unlit` shader ignores vertex colour.** It looks like the obvious choice for
particles and silently makes every particle in a system the same shade with no fade at all.
`Universal Render Pipeline/Particles/Unlit` is the one that multiplies by the particle colour,
and it has to be switched to a transparent surface by hand — six properties, a keyword and a
render queue, which is what the material inspector's Surface Type drop-down actually does.
Getting one wrong renders solid white boxes. `VfxTests.TheParticleMaterialIsTransparent` locks
it down.

**A `Projectile` prefab's `Weapon` is a blank row.** `WeaponTuning` is stamped on by
`Projectile.Fire`, so a prefab that has never been fired reports the default tuning, not the
cannon's. Read calibre off the gun, not the round.

**Coplanar slabs z-fight by distance.** The preview sheet's surface slabs were laid flush with
the ground plane; the far rows drew their slabs and the near rows drew the ground straight
through them, which reads as a missing slab rather than as z-fighting. Four centimetres of lip
fixed it.

**`SurfaceDrivingTests.AParkedVehiclePaysNothingForTheGroundUnderIt` is timing-flaky.** It
failed once in a full PlayMode run (0.831 against 0.784 ± 5 %) and passed eight times since.
It measures fuel burned over a wall-clock interval and compares two such intervals, so the
measurement window is what varies. Nothing here touches fuel. Worth knowing before someone
spends an hour on it.

**"The only code that touches Unity's particle module API" above is true of *building* one, not
of *tuning* one already running.** `ParticleRig` lives in the editor-only assembly, so
`DustTrail` - a runtime component that has to retint and re-rate its trail every time the
ground under a vehicle changes - cannot route through it and pokes `main`/`emission` directly,
in `Retint` and in `Update`. That is the scoped exception the sentence means rather than
states: one file owns *authoring* a look, and a handful of runtime effects still own *living*
with one. Worth knowing before a sixth effect needs the same live tuning and reaches for
`ParticleRig` expecting it to be reachable from play mode.

## File map

**New — runtime**

| File | What it is |
|---|---|
| `Scripts/Combat/MuzzleFlash.cs` | The flash. `Flare`, `PoseAt`, `Spawn`. |
| `Scripts/Combat/ImpactSparks.cs` | The spark burst. `Offset`, `Fade`, `PoseAt`, `Spawn`. |
| `Scripts/Vfx/ParticleBurst.cs` | A one-shot cloud that sizes and times itself out. Two prefabs use it. |
| `Scripts/Vfx/DamageSmoke.cs` | The plume while hurt and the column when dead. `ShouldSmoke`. |
| `Scripts/Vfx/DustTrail.cs` | Speed- and surface-driven dust. `Dustiness`, `TintFor`, `RateFor`, `PoseAt`. |

**New — editor**

| File | What it is |
|---|---|
| `Editor/ArtPipeline/ParticleRig.cs` | The only place that touches Unity's particle API. `Look` is the numbers. |
| `Editor/Gameplay/VfxPrefabBuilder.cs` | Every effect's numbers; builds `RF_SmokeColumn`/`RF_Splash`, bolts on the two rigs. |
| `Editor/Gameplay/CombatVfxPreviewScene.cs` | Flash and sparks as filmstrips. |
| `Editor/Gameplay/ParticleVfxPreviewScene.cs` | The four particle effects as a contact sheet. |

**New — assets** (all generated): `Art/Materials/RF_Particle.mat`,
`Prefabs/Combat/RF_MuzzleFlash.prefab`, `Prefabs/Combat/RF_Sparks.prefab`,
`Prefabs/Vfx/RF_SmokeColumn.prefab`, `Prefabs/Vfx/RF_Splash.prefab`.

**Changed**

| File | Why |
|---|---|
| `Scripts/Combat/VehicleWeapon.cs` | Holds and spawns the flash in `TryFire`. `Flash` accessor. |
| `Scripts/Combat/Projectile.cs` | Sparks on a survived hit, spray over water. `DrawsSpray`, `Sparks`, `Splash`. |
| `Scripts/Combat/IDamageable.cs` | Gained `Fraction`, for `DamageSmoke`. |
| `Editor/Gameplay/CombatPrefabBuilder.cs` | Builds the flash and sparks; binds the splash to every round. 9 prefabs, not 7. |
| `Editor/Gameplay/VehiclePrefabBuilder.cs` | Binds the flash; bolts on smoke, and dust for ground vehicles only. |
| `Editor/Gameplay/DestructiblePrefabBuilder.cs` | Binds the flash to the emplacement; bolts smoke onto every structure. |
| `Editor/ArtPipeline/GeneratedMaterials.cs` | New `EnsureParticle`; `RF_Particle`. |
| `Editor/ArtPipeline/CameraCapture.cs` | New shared `FrameOrthographic`. |
| `Editor/ArtPipeline/ArtPreviewScene.cs` | Calls it instead of its own copy. |
| `Tests/EditMode/CombatRulesTests.cs` | Ten curve/geometry tests for the flash and sparks. |
| `Tests/EditMode/VfxTests.cs` | Fifteen: the three tuning rules, the spray rule, and prefab wiring. |
| `Tests/EditMode/VehiclePrefabTests.cs`, `StructureRosterTests.cs` | Flash and spark wiring. |
| `Tests/PlayMode/SmokeTests.cs` | Four: smoking, repair, the detached column, and the one-column latch. |
| `Tests/PlayMode/{Combat,Destruction,Supply,Turret}Tests.cs` | Two `Configure` signatures. |

**Rebuild order after touching any of this**: `VfxPrefabBuilder.BuildAll` →
`CombatPrefabBuilder.BuildAll` → `DestructiblePrefabBuilder.BuildAll` →
`VehiclePrefabBuilder.BuildAll`. Everything downstream holds the thing upstream by reference,
so a rebuilt effect that the vehicles were not rebuilt against is an effect nothing fires.

## Tests

**EditMode 465/465, PlayMode 164/164** — 27 new (23 EditMode, 4 PlayMode).

What is asserted is deliberately not the particles. A particle system cannot be checked frame
by frame, so the tests cover the half that is not particles at all: the two closed-form curves
and their geometry, *when* smoke starts, *how much* dust a surface gives up and what colour,
*which* impacts throw spray, that the shared material really is transparent, and that every
prefab that should carry an effect does. Those are the places a mistake is invisible in a
still and obvious in play.

**Looked at, not only asserted.** `vfx-strips.png` and `vfx-particles.png`, both shot with
post-processing on — unlike the art contact sheet, which deliberately runs no grade. These are
emissive and transparent effects whose whole job is to bloom and blend, and judging one with
the grade off is judging something the player never sees.

## What this does not do

**No balance changed.** Nothing here touches damage, reach, rate of fire, grip, fuel or what a
round can hit. Every effect is drawn after the decision it illustrates has already been made.

**Nobody has seen any of it in motion.** The sheets are stills of curves and the suites assert
the maths. What neither can tell you: whether 65 ms of flash registers at 34 metres up in half
a screen; whether a chaingun at eight flashes a second reads as a gun or a strobe; whether the
smoke on a damaged tank is legible against a green field or lost in it. **The dust sheet in
particular cannot show the thing dust is for** — the preview vehicles are stationary, so the
puffs bunch at the emitter instead of being left behind in a trail. A play-test is the next
step and should come before any further tuning.

**Performance is unmeasured.** Forty destructibles each polling a float per frame is nothing;
a dozen simultaneous plumes at 24 particles each, plus four dust trails at 40, has not been
profiled on the low tier.
