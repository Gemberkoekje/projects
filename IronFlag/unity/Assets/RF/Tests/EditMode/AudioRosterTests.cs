using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using IronFlag.Audio;
using IronFlag.Combat;
using IronFlag.Destruction;
using IronFlag.Editor.Gameplay;
using IronFlag.Objective;
using IronFlag.Vehicles;
using IronFlag.Vfx;

namespace IronFlag.Tests.EditMode
{
    /// <summary>
    /// What the game is allowed to make a noise about: that every sound has a clip, every
    /// clip has a sound, and every weapon and vehicle asks for one that exists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The gap this closes is the one the audio pipeline could never close on its own. That
    /// build validates the recipes and measures what it rendered, but it has no idea what the
    /// game asks for - so a weapon added without a recipe, or a recipe renamed after the
    /// catalog was built, is a gun that silently stops making a noise. Nothing about that is
    /// visible in a still, in a log, or in any other test: it is a hole in the sound of the
    /// game, and only a roster test can see it.
    /// </para>
    /// <para>
    /// Written the way <c>StructureRosterTests</c> and <c>WeaponRosterTests</c> are, and
    /// checked in both directions on purpose. One direction catches a sound the game wants
    /// and has not got; the other catches a clip that was rendered, committed, and then never
    /// used by anything - twenty megabytes of it are committed, so an orphan is worth naming.
    /// </para>
    /// </remarks>
    public sealed class AudioRosterTests
    {
        /// <summary>
        /// The rule that lets the catalog be built by computation rather than by hand: a
        /// sound is its enum value behind a prefix, and that is also the name the
        /// SuperCollider recipe is keyed by.
        /// </summary>
        [Test]
        public void EverySoundIsNamedAfterItsEnumValue()
        {
            Assert.That(AudioRoster.AssetNameOf(SfxKind.WeaponCannon), Is.EqualTo("RF_Sfx_WeaponCannon"));
            Assert.That(AudioRoster.AssetNameOf(MusicKind.MatchTank), Is.EqualTo("RF_Music_MatchTank"));
            Assert.That(AudioRoster.AssetNameOf(SfxKind.None), Is.Empty, "nothing is a file");
            Assert.That(AudioRoster.AssetNameOf(MusicKind.None), Is.Empty, "nothing is a file");
        }

        /// <summary>
        /// A sound the game asks for and the catalog has not got is a call site that does
        /// nothing at all, which is invisible everywhere except in play.
        /// </summary>
        [Test]
        public void TheCatalogHasAClipForEverySoundTheGameCanMake()
        {
            AudioCatalog catalog = AudioCatalogBuilder.Load();
            Assert.That(catalog, Is.Not.Null, "there is no audio catalog to play anything out of");

            List<string> problems = catalog.Problems();
            Assert.That(problems, Is.Empty, string.Join(" ", problems));
        }

        /// <summary>
        /// The other direction: every rendered clip on disk is something the game can
        /// actually play. A clip nobody asks for is committed weight - the music beds are
        /// three quarters of twenty megabytes - and the usual cause is a recipe renamed
        /// without its enum value following.
        /// </summary>
        [Test]
        public void EveryRenderedClipIsSomethingTheGameCanPlay()
        {
            var wanted = new HashSet<string>();
            foreach (SfxKind kind in AudioRoster.Sounds())
            {
                wanted.Add(AudioRoster.AssetNameOf(kind));
            }

            foreach (MusicKind kind in AudioRoster.Themes())
            {
                wanted.Add(AudioRoster.AssetNameOf(kind));
            }

            var orphans = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets(
                "t:AudioClip", new[] { AudioCatalogBuilder.Folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!wanted.Contains(name))
                {
                    orphans.Add(name);
                }
            }

            Assert.That(
                orphans,
                Is.Empty,
                $"rendered but unreachable: {string.Join(", ", orphans)}. Either the game "
                + "should be playing these or they should not be committed.");
        }

        /// <summary>
        /// Every gun in the game has a report. A weapon added to
        /// <see cref="WeaponTuning"/> without a recipe fires silently, and the roster
        /// deliberately has no fallback for it to borrow.
        /// </summary>
        [Test]
        public void EveryWeaponHasAShot()
        {
            foreach (VehicleKind kind in VehicleRoster.Kinds)
            {
                WeaponKind weapon = WeaponTuning.For(kind).Kind;
                if (weapon == WeaponKind.None)
                {
                    continue;
                }

                Assert.That(
                    AudioRoster.ShotOf(weapon),
                    Is.Not.EqualTo(SfxKind.None),
                    $"{kind}'s {weapon} fires without a sound");
            }

            Assert.That(
                AudioRoster.ShotOf(WeaponTuning.Emplacement().Kind),
                Is.EqualTo(SfxKind.WeaponAutocannon),
                "an automated turret fires without a sound");
        }

        /// <summary>
        /// Every vehicle has an engine and a theme. The four themes are the whole reason
        /// there is more than one piece of match music, so a vehicle without one would be a
        /// vehicle that turned the soundtrack off.
        /// </summary>
        [Test]
        public void EveryVehicleHasAnEngineAndATheme()
        {
            foreach (VehicleKind kind in VehicleRoster.Kinds)
            {
                Assert.That(
                    AudioRoster.EngineOf(kind),
                    Is.Not.EqualTo(SfxKind.None),
                    $"{kind} drives in silence");

                Assert.That(
                    AudioRoster.ThemeOf(kind),
                    Is.Not.EqualTo(MusicKind.None),
                    $"{kind} has no theme");
            }
        }

        /// <summary>
        /// The states that are events have a sound and the states that are setup do not.
        /// Getting this backwards is what makes a map full of pre-damaged walls open with
        /// nine collapses.
        /// </summary>
        [Test]
        public void OnlyTheStatesThatAreEventsMakeANoise()
        {
            Assert.That(AudioRoster.DamageOf(DestructionState.Damaged), Is.EqualTo(SfxKind.StructureDamaged));
            Assert.That(AudioRoster.DamageOf(DestructionState.Destroyed), Is.EqualTo(SfxKind.StructureDestroyed));
            Assert.That(
                AudioRoster.DamageOf(DestructionState.Intact),
                Is.EqualTo(SfxKind.None),
                "a structure that is standing is not an event");

            Assert.That(AudioRoster.FlagOf(FlagState.Carried), Is.EqualTo(SfxKind.FlagPickup));
            Assert.That(AudioRoster.FlagOf(FlagState.Dropped), Is.EqualTo(SfxKind.FlagDropped));
            Assert.That(AudioRoster.FlagOf(FlagState.AtTower), Is.EqualTo(SfxKind.FlagReturned));
            Assert.That(AudioRoster.FlagOf(FlagState.Captured), Is.EqualTo(SfxKind.FlagCaptured));
            Assert.That(AudioRoster.FlagOf(FlagState.None), Is.EqualTo(SfxKind.None));
        }

        /// <summary>
        /// The four themes are themes and nothing else is. A match with nobody deployed holds
        /// the last one played, so anything wrongly counted as a theme is something a rematch
        /// would open on - the victory fanfare being the one that matters.
        /// </summary>
        [Test]
        public void OnlyTheFourMatchThemesAreThemes()
        {
            foreach (VehicleKind kind in VehicleRoster.Kinds)
            {
                Assert.That(
                    AudioRoster.IsMatchTheme(AudioRoster.ThemeOf(kind)),
                    Is.True,
                    $"{kind}'s theme is not counted as one");
            }

            Assert.That(AudioRoster.IsMatchTheme(MusicKind.MenuTheme), Is.False);
            Assert.That(AudioRoster.IsMatchTheme(MusicKind.Victory), Is.False);
            Assert.That(AudioRoster.IsMatchTheme(MusicKind.Defeat), Is.False);
            Assert.That(AudioRoster.IsMatchTheme(MusicKind.None), Is.False);
        }

        /// <summary>
        /// The beds loop and the end cues do not. A victory fanfare that came round again
        /// every twelve seconds would turn the moment the game exists for into a jingle.
        /// </summary>
        [Test]
        public void TheBedsLoopAndTheEndCuesDoNot()
        {
            Assert.That(AudioRoster.Loops(MusicKind.MenuTheme), Is.True);
            Assert.That(AudioRoster.Loops(MusicKind.MatchJeep), Is.True);
            Assert.That(AudioRoster.Loops(MusicKind.Victory), Is.False);
            Assert.That(AudioRoster.Loops(MusicKind.Defeat), Is.False);
            Assert.That(AudioRoster.Loops(MusicKind.None), Is.False);
        }

        /// <summary>
        /// Only the shots that are events are heard as explosions. Every round in the game
        /// goes off through <see cref="Explosion.Spawn"/>, and most of them are bullets: the
        /// ASV fires eight a second, so without a floor a single one of them would sound like
        /// an artillery barrage. Rounds under it are still heard, as an impact off the armour
        /// they struck, which is the right sound for a bullet.
        /// </summary>
        [Test]
        public void ABulletGoingOffIsNotHeardAsAnExplosion()
        {
            foreach (VehicleKind kind in VehicleRoster.Kinds)
            {
                WeaponTuning weapon = WeaponTuning.For(kind);
                if (weapon.Kind == WeaponKind.None)
                {
                    continue;
                }

                float blast = Projectile.BlastRadius(weapon);
                bool shell = weapon.SplashRadius > 0.0f;

                Assert.That(
                    blast >= Explosion.HeardAbove,
                    Is.EqualTo(shell),
                    $"{kind}'s {weapon.Kind} is on the wrong side of the line at {blast} m");
            }

            Assert.That(
                Projectile.BlastRadius(WeaponTuning.Emplacement()),
                Is.LessThan(Explosion.HeardAbove),
                "an automated turret sounds like artillery");
        }

        /// <summary>
        /// The line between a bang and a bullet is the same line between a scorch and no
        /// scorch. Two thresholds for one question would eventually disagree, and the version
        /// where they disagree is a bang with no mark under it.
        /// </summary>
        [Test]
        public void WhatIsHeardIsExactlyWhatLeavesAMark()
        {
            float mark = Explosion.HeardAbove * GroundMark.BlastShare * 2.0f;
            Assert.That(mark, Is.EqualTo(GroundMark.SmallestBlast).Within(0.0001f));
        }

        /// <summary>
        /// The importer's own claim, checked against what actually landed: sound effects are
        /// mono. A stereo one would be the single clip in the game whose level was not the
        /// level it was rendered at.
        /// </summary>
        [Test]
        public void EverySoundEffectIsMono()
        {
            AudioCatalog catalog = AudioCatalogBuilder.Load();

            foreach (SfxKind kind in AudioRoster.Sounds())
            {
                AudioClip clip = catalog.ClipFor(kind);
                if (clip == null)
                {
                    continue;
                }

                Assert.That(clip.channels, Is.EqualTo(1), $"{kind} is not mono");
            }
        }
    }
}
