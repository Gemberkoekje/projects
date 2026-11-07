using CharacterCreator.application.Rulesets;
using CharacterCreator.core.Content;
using CharacterCreator.core.Plans;
using CharacterCreator.core.Skills;
using CharacterCreator.infrastructure.Content;
using CharacterCreator.infrastructure.Void;
using CharacterCreator.WebApp.Client.Services;
using CharacterCreator.WebApp.Client.Words;
using CharacterCreator.WebApp.Printing;

namespace CharacterCreator.test.Printing;

/// <summary>What the printed sheet says (DESIGN.md §5, Print), worded as on the build screen.</summary>
public sealed class SheetComposer_specs
{
    private static readonly DateTimeOffset Now = new (2026, 11, 07, 10, 00, 00, TimeSpan.Zero);

    private static readonly LoadedRuleset Data = RulesetClient.Prepare(RulesetBuilder.Build(
        new EmbeddedContentSource().LoadAsync("2026-v07", CancellationToken.None).Result.Value,
        new EmbeddedOfflineSnapshot().LoadAsync("2026-v07", CancellationToken.None).Result.Value,
        null!,
        SeedRulesetHandler.FirstStart,
        Now).Ruleset).Value;

    private static readonly PlanFile Paladin = PresetPlan("paladijn") with { Name = "Ilse van de Vijfhoek" };

    [Test]
    public void the_sheet_names_the_character_the_plan_and_the_rules()
    {
        var sheet = SheetComposer.Compose(Data, Paladin, Ui.Dutch, Now);

        sheet.Title.Should().Be("Ilse van de Vijfhoek");
        sheet.Subtitle.Should().Be("Nieuw personage · 15 XP");
        sheet.Rules.Should().Be($"Regels {Data.Ruleset.Id} (voorlopig)");
        sheet.MadeOn.Should().Be("Gemaakt op 7-11-2026");
    }

    [Test]
    public void the_numbers_are_the_build_screens()
    {
        var sheet = SheetComposer.Compose(Data, Paladin, Ui.Dutch, Now);

        sheet.Facts.Select(fact => $"{fact.Label} {fact.Value}").Should().StartWith(["XP 15", "Uitgegeven 15", "Over 0", "Levenspunten 2", "Pantserpunten 4"]);
        sheet.Energy.Should().ContainSingle().Which.Should().Be(new application.Sheets.SheetFact { Label = "Spiritueel", Value = "10" });
    }

    [Test]
    public void a_clean_plan_has_nothing_to_note()
        => SheetComposer.Compose(Data, Paladin, Ui.Dutch, Now).Notes.Should().BeEmpty();

    [Test]
    public void the_skills_table_lists_every_purchase_and_the_total()
    {
        var table = SheetComposer.Compose(Data, Paladin, Ui.Dutch, Now).Skills;

        table.Rows.Should().HaveCount(Paladin.Skills.Count + 1);
        table.Rows[0].Should().Equal("Lezen & schrijven", "1×", "0", "11", string.Empty);
        table.Rows[^1].Should().Equal("Totaal", string.Empty, "15", string.Empty, string.Empty);
    }

    [Test]
    public void texts_spells_loresheets_and_xp_per_event_come_along()
    {
        var sheet = SheetComposer.Compose(Data, Paladin, Ui.Dutch, Now);

        sheet.SkillTexts.Should().HaveCount(Paladin.Skills.Count);
        sheet.SkillTexts.Single(text => text.Title == "Paladijn").Facts.Should().Contain(fact => fact.Label == "Vereist" && fact.Value.StartsWith("een Genezingsspreuken A (", StringComparison.Ordinal));
        sheet.Spells.Should().HaveCount(8);
        sheet.Loresheets.Should().ContainSingle().Which.Url.Should().StartWith("https://the-vortex.nl/");
        sheet.XpPerEvent.Rows.Should().HaveCount(SheetComposer.Events);
        sheet.XpPerEvent.Rows[0].Should().Equal("Evenement 1", "15", "+1,5", "16,5");
    }

    [Test]
    public void what_breaks_a_rule_is_noted_in_words()
    {
        var broken = new PlanFile { Name = "Bram", Mode = PlanMode.Free, Xp = 1m, Skills = [new () { Key = new SkillKey("armour-iii"), Times = 1 }] };

        var notes = SheetComposer.Compose(Data, broken, Ui.Dutch, Now).Notes;

        notes.Should().Contain("Harnas III: vereist Harnas II")
            .And.Contain(note => note.StartsWith("niet genoeg XP", StringComparison.Ordinal));
    }

    [Test]
    public void in_english_everything_is_english()
    {
        var sheet = SheetComposer.Compose(Data, Paladin, new Ui(Language.English), Now);

        sheet.Language.Should().Be("en");
        sheet.Subtitle.Should().Be("New character · 15 XP");
        sheet.Labels.Skills.Should().Be("Skills");
        sheet.Skills.Rows[0][0].Should().Be("Literacy");
        sheet.MadeOn.Should().Be("Made on 7 Nov 2026");
    }

    [Test]
    public void an_unnamed_character_gets_a_title_anyway()
        => SheetComposer.Compose(Data, Paladin with { Name = "  " }, Ui.Dutch, Now).Title.Should().Be("Naamloos personage");

    [Test]
    public void free_planning_shows_its_own_xp_and_looks_ahead_from_there()
    {
        var sheet = SheetComposer.Compose(Data, Paladin with { Mode = PlanMode.Free, Xp = 31.5m }, Ui.Dutch, Now);

        sheet.Subtitle.Should().Be("Vrij plannen · 31,5 XP");
        sheet.XpPerEvent.Rows[0].Should().Equal("Evenement 1", "31,5", "+1", "32,5");
    }

    private static PlanFile PresetPlan(string key)
    {
        var preset = Data.Content.Presets.Single(candidate => candidate.Key == key);
        return new PlanFile { Mode = PlanMode.NewCharacter, Skills = [.. preset.Skills.Select(skill => new PlanSkill { Key = skill.Key, Times = skill.Times })] };
    }
}
