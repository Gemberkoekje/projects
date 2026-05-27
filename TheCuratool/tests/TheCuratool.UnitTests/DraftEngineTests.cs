using System.IO;

using TheCuratool.Application;
using TheCuratool.Domain;

namespace TheCuratool.UnitTests;

public sealed class DraftEngineTests
{
    private readonly CharacterDatabase _characterDatabase = CharacterDatabase.LoadFromFile(GetCharactersFilePath());
    private readonly LoricDatabase _loricDatabase = LoricDatabase.LoadFromFile(GetLoricsFilePath());

    [Fact]
    public void NoVortox_SevenPlayer_DraftCompletesWithoutContradiction()
    {
        var script = LoadNoVortoxScript();
        var engine = CreateEngine();
        var session = engine.StartSession(script, 7, Array.Empty<string>());

        while (session.Status == GameStatus.Drafting)
        {
            var suggestions = engine.SuggestThree(session);
            Assert.NotEmpty(suggestions);

            var offered = suggestions.Select(c => c.Id).ToList().AsReadOnly();
            session = engine.RecordChoice(session.Id, GetCurrentDraftOrder(session), offered[0], offered, new HiddenFlags(false, false));
        }

        Assert.Equal(GameStatus.Completed, session.Status);
        Assert.All(session.Players, slot => Assert.IsType<PlayerChoice.ChosenChoice>(slot.Choice));

        var summary = engine.GetMakeupSummary(session);
        Assert.Equal(0, summary.RemainingSeats);
        Assert.NotEmpty(summary.TargetCounts);
    }

    [Fact]
    public void HuntsmanChosenEarly_LastSeatForcedToDamsel()
    {
        var script = CreateScript("huntsman", "damsel", "chef", "washerwoman", "librarian", "poisoner", "imp");
        var engine = CreateEngine();
        var session = engine.StartSession(script, 7, Array.Empty<string>());

        session = engine.CreateCuratedOffer(session.Id, GetCurrentDraftOrder(session), new[] { "huntsman", "chef", "washerwoman" });
        session = engine.RecordChoice(
            session.Id,
            GetCurrentDraftOrder(session),
            "huntsman",
            new[] { "huntsman", "chef", "washerwoman" },
            new HiddenFlags(false, false));

        var deterministicPicks = new[] { "poisoner", "imp", "chef", "washerwoman", "librarian" };
        foreach (var pick in deterministicPicks)
        {
            session = engine.RecordChoice(
                session.Id,
                GetCurrentDraftOrder(session),
                pick,
                new[] { pick },
                new HiddenFlags(false, false));
        }

        var forced = engine.GetRemainingValidCharacters(session);
        var damsel = Assert.Single(forced);
        Assert.Equal("damsel", damsel.Id, ignoreCase: true);
    }

    [Fact]
    public void KazaliAndSummoner_BecomeUnavailableAtExpectedTimes()
    {
        var script = CreateScript("kazali", "summoner", "poisoner", "imp", "chef", "washerwoman", "drunk");
        var engine = CreateEngine();
        var session = engine.StartSession(script, 7, Array.Empty<string>());

        var initial = engine.GetRemainingValidCharacters(session);
        Assert.Contains(initial, c => c.Id == "kazali");
        Assert.Contains(initial, c => c.Id == "summoner");

        session = engine.RecordChoice(
            session.Id,
            GetCurrentDraftOrder(session),
            "poisoner",
            new[] { "poisoner" },
            new HiddenFlags(false, false));

        var afterMinion = engine.GetRemainingValidCharacters(session);
        Assert.DoesNotContain(afterMinion, c => c.Id == "kazali");

        session = engine.RecordChoice(
            session.Id,
            GetCurrentDraftOrder(session),
            "imp",
            new[] { "imp" },
            new HiddenFlags(false, false));

        var afterDemon = engine.GetRemainingValidCharacters(session);
        Assert.DoesNotContain(afterDemon, c => c.Id == "summoner");
    }

    [Fact]
    public void AtheistRequiresCommitmentUnlessChosenAsDrunk()
    {
        var script = CreateScript("atheist", "poisoner", "imp", "chef", "washerwoman", "drunk", "librarian", "investigator");
        var engine = CreateEngine();
        var session = engine.StartSession(script, 8, Array.Empty<string>());

        session = engine.RecordChoice(
            session.Id,
            GetCurrentDraftOrder(session),
            "poisoner",
            new[] { "poisoner" },
            new HiddenFlags(false, false));

        var valid = engine.GetRemainingValidCharacters(session);
        Assert.DoesNotContain(valid, c => c.Id == "atheist");

        Assert.Throws<InvalidOperationException>(() => engine.RecordChoice(
            session.Id,
            GetCurrentDraftOrder(session),
            "atheist",
            new[] { "atheist" },
            new HiddenFlags(false, false)));

        session = engine.RecordChoice(
            session.Id,
            GetCurrentDraftOrder(session),
            "atheist",
            new[] { "atheist" },
            new HiddenFlags(true, false));

        Assert.Equal(6, GetRemainingSeatCount(session));
    }

    [Fact]
    public void RemainingValidCharacters_ExcludeDrunkLunaticAndMarionette()
    {
        var script = CreateScript("drunk", "lunatic", "marionette", "chef", "poisoner", "imp", "washerwoman");
        var engine = CreateEngine();
        var session = engine.StartSession(script, 7, Array.Empty<string>());

        var valid = engine.GetRemainingValidCharacters(session);
        var suggestions = engine.SuggestThree(session);

        Assert.DoesNotContain(valid, c => string.Equals(c.Id, "drunk", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(valid, c => string.Equals(c.Id, "lunatic", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(valid, c => string.Equals(c.Id, "marionette", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(suggestions, c => string.Equals(c.Id, "marionette", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CreateCuratedOffer_RejectsDraftExcludedCharacters()
    {
        var script = CreateScript("marionette", "chef", "poisoner", "imp", "washerwoman", "librarian", "investigator");
        var engine = CreateEngine();
        var session = engine.StartSession(script, 7, Array.Empty<string>());

        Assert.Throws<InvalidOperationException>(() => engine.CreateCuratedOffer(session.Id, GetCurrentDraftOrder(session), new[] { "marionette", "chef" }));
    }

    [Fact]
    public void RecordChoice_DrunkFlagRequiresTownsfolk()
    {
        var script = CreateScript("poisoner", "imp", "chef", "washerwoman", "librarian", "investigator", "baron");
        var engine = CreateEngine();
        var session = engine.StartSession(script, 7, Array.Empty<string>());

        Assert.Throws<InvalidOperationException>(() => engine.RecordChoice(
            session.Id,
            GetCurrentDraftOrder(session),
            "poisoner",
            new[] { "poisoner" },
            new HiddenFlags(true, false)));
    }

    [Fact]
    public void RecordChoice_LunaticFlagRequiresDemon()
    {
        var script = CreateScript("poisoner", "imp", "chef", "washerwoman", "librarian", "investigator", "baron");
        var engine = CreateEngine();
        var session = engine.StartSession(script, 7, Array.Empty<string>());

        Assert.Throws<InvalidOperationException>(() => engine.RecordChoice(
            session.Id,
            GetCurrentDraftOrder(session),
            "chef",
            new[] { "chef" },
            new HiddenFlags(false, true)));
    }

    [Fact]
    public void SuggestThree_VarietyDegradesGracefullyWhenPoolConstrained()
    {
        var script = CreateScript("chef", "washerwoman", "librarian", "empath", "investigator", "poisoner", "imp");
        var engine = CreateEngine();
        var session = engine.StartSession(script, 7, Array.Empty<string>());

        var suggestions = engine.SuggestThree(session);
        Assert.Equal(3, suggestions.Count);

        while (session.Status == GameStatus.Drafting)
        {
            var nextChoice = suggestions.First();
            session = engine.RecordChoice(
                session.Id,
                GetCurrentDraftOrder(session),
                nextChoice.Id,
                new[] { nextChoice.Id },
                new HiddenFlags(false, false));

            if (session.Status == GameStatus.Drafting)
            {
                suggestions = engine.SuggestThree(session);
                Assert.InRange(suggestions.Count, 1, 3);
            }
        }
    }

    [Fact]
    public void HiddenLunaticAndDrunkFlags_CountAsOutsidersForSetupMath()
    {
        var script = CreateScript("imp", "vortox", "drunk", "chef", "washerwoman", "poisoner", "baron", "librarian", "investigator");
        var engine = CreateEngine();
        var session = engine.StartSession(script, 9, Array.Empty<string>());

        session = engine.RecordChoice(session.Id, GetCurrentDraftOrder(session), "imp", new[] { "imp" }, new HiddenFlags(false, true));
        session = engine.RecordChoice(session.Id, GetCurrentDraftOrder(session), "chef", new[] { "chef" }, new HiddenFlags(true, false));

        var summary = engine.GetMakeupSummary(session);

        Assert.Equal(2, summary.CurrentCounts.Outsiders);
        Assert.Equal(0, summary.CurrentCounts.Demons);
        Assert.Equal(0, summary.CurrentCounts.Townsfolk);
    }

    [Fact]
    public void LunaticFlaggedDemon_StillRequiresRealDemonBeforeCompletion()
    {
        var script = CreateScript("imp", "vortox", "baron", "drunk", "chef", "washerwoman", "librarian");
        var engine = CreateEngine();
        var session = engine.StartSession(script, 7, Array.Empty<string>());

        session = engine.RecordChoice(session.Id, GetCurrentDraftOrder(session), "imp", new[] { "imp" }, new HiddenFlags(false, true));

        while (session.Status == GameStatus.Drafting)
        {
            var valid = engine.GetRemainingValidCharacters(session);
            if (valid.Count == 0)
            {
                break;
            }

            var nonDemon = valid.FirstOrDefault(c => c.Type != CharacterType.Demon);
            if (nonDemon is null)
            {
                break;
            }

            session = engine.RecordChoice(session.Id, GetCurrentDraftOrder(session), nonDemon.Id, new[] { nonDemon.Id }, new HiddenFlags(false, false));
        }

        var hasRealDemon = session.Players
            .Select(slot => slot.Choice)
            .OfType<PlayerChoice.ChosenChoice>()
            .Any(choice =>
            {
                var role = script.Characters.Single(c => string.Equals(c.Id, choice.CharacterId, StringComparison.OrdinalIgnoreCase));
                return role.Type == CharacterType.Demon && !choice.HiddenFlags.IsLunatic;
            });

        Assert.False(hasRealDemon);
        Assert.NotEqual(GameStatus.Completed, session.Status);

        var remaining = engine.GetRemainingValidCharacters(session);
        Assert.True(remaining.Count == 0 || remaining.All(c => c.Type == CharacterType.Demon));
    }

    [Fact]
    public void RecordChoice_RejectsCuratedOfferMismatch()
    {
        var script = CreateScript("chef", "washerwoman", "poisoner", "imp", "drunk", "baron", "librarian");
        var engine = CreateEngine();
        var session = engine.StartSession(script, 7, Array.Empty<string>());

        session = engine.CreateCuratedOffer(session.Id, GetCurrentDraftOrder(session), new[] { "chef", "washerwoman", "librarian" });

        Assert.Throws<InvalidOperationException>(() => engine.RecordChoice(
            session.Id,
            GetCurrentDraftOrder(session),
            "chef",
            new[] { "chef", "washerwoman", "drunk" },
            new HiddenFlags(false, false)));
    }

    [Fact]
    public void CreateCuratedOffer_AllowsEvilSentinelAlongsideCharacters()
    {
        var script = CreateScript("fortune_teller", "goon", "drunk", "chef", "poisoner", "imp", "baron", "washerwoman", "librarian");
        var engine = CreateEngine();
        var session = engine.StartSession(script, 9, Array.Empty<string>());
        var slot = GetCurrentDraftOrder(session);

        session = engine.CreateCuratedOffer(session.Id, slot, new[] { "fortune_teller", "goon", "evil" });

        var updatedSlot = session.Players.Single(player => player.DraftOrder == slot);
        var offer = Assert.IsType<PlayerChoice.UnchosenChoice>(updatedSlot.Choice);
        Assert.Contains("fortune_teller", offer.OfferedIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("goon", offer.OfferedIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("evil", offer.OfferedIds, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void RecordChoice_EvilSentinelCanBeResolvedOutsideLegionMode()
    {
        var script = CreateScript("chef", "washerwoman", "librarian", "poisoner", "baron", "imp", "investigator");
        var engine = CreateEngine();
        var session = engine.StartSession(script, 7, Array.Empty<string>());
        var slot = GetCurrentDraftOrder(session);

        session = engine.CreateCuratedOffer(session.Id, slot, new[] { "chef", "poisoner", "evil" });
        session = engine.RecordChoice(session.Id, slot, "evil", new[] { "chef", "poisoner", "evil" }, new HiddenFlags(true, true));

        var pendingResolution = session.Players.Single(player => player.DraftOrder == slot);
        var pendingChoice = Assert.IsType<PlayerChoice.ChosenChoice>(pendingResolution.Choice);
        Assert.Equal("evil", pendingChoice.CharacterId, ignoreCase: true);
        Assert.False(pendingChoice.HiddenFlags.IsDrunk);
        Assert.False(pendingChoice.HiddenFlags.IsLunatic);

        session = engine.ResolveEvilSlot(session.Id, slot, "imp", new HiddenFlags(false, false));

        var resolvedSlot = session.Players.Single(player => player.DraftOrder == slot);
        var resolvedChoice = Assert.IsType<PlayerChoice.ChosenChoice>(resolvedSlot.Choice);
        Assert.Equal("imp", resolvedChoice.CharacterId, ignoreCase: true);
    }

    [Fact]
    public void ChoirboyAndKing_OnScript_NeitherChosen_BothRemainAvailable()
    {
        var script = CreateScript("choirboy", "king", "chef", "washerwoman", "librarian", "poisoner", "imp");
        var engine = CreateEngine();
        var session = engine.StartSession(script, 7, Array.Empty<string>());

        var valid = engine.GetRemainingValidCharacters(session);

        Assert.Contains(valid, character => string.Equals(character.Id, "choirboy", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(valid, character => string.Equals(character.Id, "king", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ChoirboyChosen_WhenKingOnScript_DoesNotAddOutOfScriptCharacter()
    {
        var script = CreateScript("choirboy", "king", "chef", "washerwoman", "librarian", "poisoner", "imp");
        var engine = CreateEngine();
        var session = engine.StartSession(script, 7, Array.Empty<string>());

        session = engine.RecordChoice(
            session.Id,
            GetCurrentDraftOrder(session),
            "choirboy",
            new[] { "choirboy" },
            new HiddenFlags(false, false));

        var outOfScriptCharacters = session.Script.Characters.Where(character => character.IsOutOfScript).ToList();

        Assert.Empty(outOfScriptCharacters);
        Assert.Equal(1, session.Script.Characters.Count(character => string.Equals(character.Id, "king", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void ChoirboyChosen_WhenKingMissing_AddsKingAsOutOfScriptAndIncludesInSummary()
    {
        var script = CreateScript("choirboy", "chef", "washerwoman", "librarian", "investigator", "poisoner", "imp");
        var engine = CreateEngine();
        var session = engine.StartSession(script, 7, Array.Empty<string>());

        session = engine.RecordChoice(
            session.Id,
            GetCurrentDraftOrder(session),
            "choirboy",
            new[] { "choirboy" },
            new HiddenFlags(false, false));

        var addedKing = session.Script.Characters.Single(character => string.Equals(character.Id, "king", StringComparison.OrdinalIgnoreCase));
        Assert.True(addedKing.IsOutOfScript);

        var valid = engine.GetRemainingValidCharacters(session);
        Assert.Contains(valid, character => string.Equals(character.Id, "king", StringComparison.OrdinalIgnoreCase));

        var summary = engine.GetMakeupSummary(session);
        Assert.Contains("king", summary.OutOfScriptCharacterIds, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void LegionOnScript_WhenLegionModeDisabled_ExcludesLegionFromRemainingPool()
    {
        var script = CreateScript("legion", "chef", "washerwoman", "librarian", "poisoner", "baron", "imp");
        var engine = CreateEngine();
        var session = engine.StartSession(script, 7, Array.Empty<string>(), isLegionGame: false);

        var remaining = engine.GetRemainingValidCharacters(session);

        Assert.DoesNotContain(remaining, character => string.Equals(character.Id, "legion", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LegionMode_EvilSlotsOfferOnlyEvilSentinel()
    {
        var script = CreateScript("legion", "chef", "washerwoman", "librarian", "poisoner", "baron", "imp");
        var engine = CreateEngine();
        var session = engine.StartSession(script, 9, Array.Empty<string>(), isLegionGame: true);

        var suggestions = engine.SuggestThree(session);
        var evilOnly = Assert.Single(suggestions);
        Assert.Equal("evil", evilOnly.Id, ignoreCase: true);
        Assert.Equal(CharacterType.Demon, evilOnly.Type);
    }

    [Fact]
    public void ResolveEvilSlot_ReplacesSentinelAndUpdatesSummaryGrouping()
    {
        var script = CreateScript("legion", "imp", "poisoner", "chef", "washerwoman", "librarian", "investigator");
        var engine = CreateEngine();
        var session = engine.StartSession(script, 7, Array.Empty<string>(), isLegionGame: true, legionCount: 1);
        var evilDraftOrder = GetCurrentDraftOrder(session);

        session = engine.RecordChoice(
            session.Id,
            evilDraftOrder,
            "evil",
            new[] { "evil" },
            new HiddenFlags(false, false));

        var unresolvedSummary = engine.GetMakeupSummary(session);
        Assert.Contains(unresolvedSummary.ChosenCharactersByType[CharacterType.Demon], name => string.Equals(name, "Evil (ST-assigned)", StringComparison.Ordinal));

        session = engine.ResolveEvilSlot(session.Id, evilDraftOrder, "imp", new HiddenFlags(false, false));
        var resolvedSummary = engine.GetMakeupSummary(session);

        Assert.DoesNotContain(resolvedSummary.ChosenCharactersByType[CharacterType.Demon], name => string.Equals(name, "Evil (ST-assigned)", StringComparison.Ordinal));
        Assert.Contains(resolvedSummary.ChosenCharactersByType[CharacterType.Demon], name => string.Equals(name, "Imp", StringComparison.Ordinal));
    }

    private DraftEngine CreateEngine()
    {
        return new DraftEngine(_characterDatabase, _loricDatabase, new SetupCalculator());
    }

    private Script LoadNoVortoxScript()
    {
        var parser = new ScriptParser();
        var json = File.ReadAllText(GetNoVortoxFilePath());
        var parsed = parser.Parse(json, _characterDatabase);
        Assert.True(parsed.IsSuccess);
        return parsed.Script;
    }

    private Script CreateScript(params string[] ids)
    {
        var characters = ids.Select(_characterDatabase.Resolve).ToList().AsReadOnly();
        return new Script("Draft Test", "UnitTest", characters);
    }

    private static int GetCurrentDraftOrder(GameSession session)
    {
        return session.Players
            .Where(slot => slot.Choice is not PlayerChoice.ChosenChoice)
            .Select(slot => slot.DraftOrder)
            .First();
    }

    private static int GetRemainingSeatCount(GameSession session)
    {
        return session.Players.Count(slot => slot.Choice is not PlayerChoice.ChosenChoice);
    }

    private static string GetCharactersFilePath()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "data", "characters.json"));
    }

    private static string GetLoricsFilePath()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "data", "lorics.json"));
    }

    private static string GetNoVortoxFilePath()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "NoVortox.json"));
    }
}
