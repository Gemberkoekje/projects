using System.Text;
using System.Text.Json;
using CharacterCreator.core.Content;
using CharacterCreator.core.Plans;
using CharacterCreator.core.Rulesets;
using CharacterCreator.core.Skills;

namespace CharacterCreator.test.Plans;

/// <summary>
/// Reading plan files (DESIGN.md §4.3): the Character Creator's own, and the three save formats of
/// Thorfgin's builder, including his base64 <c>.dat</c> export.
/// </summary>
public sealed class PlanImport_specs
{
    private static readonly IReadOnlyList<LegacySkill> Legacy =
    [
        new () { Id = 101, Name = "Ambidextrie", Key = new ("ambidexterity") },
        new () { Id = 102, Name = "Lezen & Schrijven", Key = new ("literacy") },
        new () { Id = 140, Name = "Magiërspreuken A", Key = new ("mage-spells-a") },
        new () { Id = 160, Name = "Harnas II", Key = new ("armour-ii") },
    ];

    [Test]
    public void a_character_creator_file_comes_back_as_it_was_written()
    {
        var plan = Plan.Free(21.5m, Holdings.Of((new SkillKey("literacy"), 1), (new SkillKey("armour-ii"), 2)));
        var written = PlanFile.From("Ilse", plan, "2026-v07@20261107T084541Z", new DateTimeOffset(2026, 11, 07, 10, 00, 00, TimeSpan.Zero));

        var read = Read(JsonSerializer.Serialize(written, RulesetJsonContext.Default.PlanFile));

        read.Source.Should().Be(PlanSource.CharacterCreator);
        read.File.Should().BeEquivalentTo(written);
        read.File.ToPlan().Planned.Should().Equal(plan.Planned);
        read.LeftOut.Should().BeEmpty();
    }

    [Test]
    public void an_old_save_names_its_skills_by_thorfgins_ids()
    {
        var read = Read(OldSave("2023-10b", isNew: false, maxXp: 21.5m, ("101", "Ambidextrie", 1), ("160", "Harnas II", 2)));

        read.Source.Should().Be(PlanSource.OldBuilder);
        read.OldVersion.Should().Be("2023-10b");
        read.File.Name.Should().Be("Oude Bram");
        read.File.Mode.Should().Be(PlanMode.Free);
        read.File.Xp.Should().Be(21.5m);
        read.File.ToHoldings().Should().Equal(Holdings.Of((new SkillKey("ambidexterity"), 1), (new SkillKey("armour-ii"), 2)));
    }

    [Test]
    public void a_new_character_in_the_old_builder_is_a_new_character_here()
        => Read(OldSave("2023-10b", isNew: true, maxXp: 15m, ("102", "Lezen & Schrijven", 1))).File.Mode.Should().Be(PlanMode.NewCharacter);

    [Test]
    public void a_skill_with_no_entry_now_is_left_out_by_its_old_name()
    {
        var read = Read(OldSave("2023-10b", isNew: false, maxXp: 20m, ("101", "Ambidextrie", 1), ("999", "Verdwenen vaardigheid", 1)));

        read.LeftOut.Should().Equal("Verdwenen vaardigheid");
        read.File.Skills.Should().ContainSingle();
    }

    [Test]
    public void the_old_builders_dat_export_is_read_through_its_base64()
    {
        var json = OldSave("2023-10b", isNew: false, maxXp: 18m, ("140", "Magiërspreuken A", 1));
        var dat = Convert.ToBase64String(Encoding.ASCII.GetBytes(Uri.EscapeDataString(json)));

        var read = Read(dat);

        read.File.Skills.Single().Key.Should().Be(new SkillKey("mage-spells-a"));
        read.File.Name.Should().Be("Oude Bram");
    }

    [Test]
    public void a_2023_10a_save_names_skills_by_name_ignoring_case()
    {
        var read = Read("""{ "version": "2023-10a", "name": "A", "max_xp": 16, "is_checked": false, "Skills": [ { "skill": "harnas ii", "count": 1 }, { "skill": "Lezen", "count": 1 } ] }""");

        read.File.ToHoldings().Should().Equal(Holdings.Of((new SkillKey("armour-ii"), 1), (new SkillKey("literacy"), 1)), "a name the old builder knew in part matches the first that contains it, as it did");
    }

    [Test]
    public void the_oldest_2023_10_array_is_read_too()
    {
        var read = Read("""[ { "ruleset_version": "2023-10", "MAX_XP": 17.25, "isChecked": false, "data": [ { "skill": "Ambidextrie", "count": 1 } ] } ]""");

        read.OldVersion.Should().Be("2023-10");
        read.File.Xp.Should().Be(17.25m);
        read.File.Skills.Single().Key.Should().Be(new SkillKey("ambidexterity"));
    }

    [Test]
    public void the_same_skill_twice_adds_up_and_a_missing_count_is_one()
    {
        var read = Read("""{ "version": "2023-10b", "name": "", "max_xp": 20, "is_checked": false, "Skills": [ { "id": 160, "skill": "Harnas II" }, { "id": 160, "skill": "Harnas II", "count": 2 } ] }""");

        read.File.Skills.Should().Equal(new PlanSkill { Key = new ("armour-ii"), Times = 3 });
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("not a file at all")]
    [TestCase("{ \"hello\": \"world\" }")]
    [TestCase("[1, 2, 3]")]
    [TestCase("{ \"version\": \"2023-10b\"")]
    public void anything_else_is_refused(string text)
        => PlanImport.Read(text, Legacy).IsValid.Should().BeFalse();

    private static ImportedPlan Read(string text)
    {
        var result = PlanImport.Read(text, Legacy);
        result.IsValid.Should().BeTrue(string.Join("; ", result.Messages.Select(message => message.Message)));
        return result.Value;
    }

    private static string OldSave(string version, bool isNew, decimal maxXp, params (string Id, string Skill, int Count)[] skills)
        => JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["version"] = version,
            ["name"] = "Oude Bram",
            ["max_xp"] = maxXp,
            ["is_checked"] = isNew,
            ["Skills"] = skills.Select(skill => new Dictionary<string, object> { ["id"] = int.Parse(skill.Id, System.Globalization.CultureInfo.InvariantCulture), ["skill"] = skill.Skill, ["count"] = skill.Count }).ToList(),
        });
}
