using System.Text.Json;
using CharacterCreator.core.Plans;
using CharacterCreator.core.Rulesets;
using CharacterCreator.core.Skills;

namespace CharacterCreator.test.Plans;

/// <summary>The plan as it is saved in the browser and exported (DESIGN.md §4.3).</summary>
public sealed class PlanFile_specs
{
    [Test]
    public void it_keeps_the_purchases_in_the_order_they_were_added()
    {
        var plan = Plan.NewCharacter(Holdings.Of((Book.ArmourII, 1), (Book.Literacy, 1), (Book.ArmourI, 1)));

        var file = PlanFile.From("Ilse", plan, "r1", DateTimeOffset.UnixEpoch);

        file.Skills.Select(skill => skill.Key).Should().Equal(Book.ArmourII, Book.Literacy, Book.ArmourI);
        file.ToPlan().Planned.Should().Equal(plan.Planned);
    }

    [Test]
    public void a_new_character_comes_back_as_one()
        => PlanFile.From(string.Empty, Plan.NewCharacter(Holdings.None), "r1", DateTimeOffset.UnixEpoch).ToPlan().Mode.Should().Be(PlanMode.NewCharacter);

    [Test]
    public void free_planning_keeps_its_xp()
    {
        var plan = PlanFile.From(string.Empty, Plan.Free(23.75m, Holdings.None), "r1", DateTimeOffset.UnixEpoch).ToPlan();

        plan.Mode.Should().Be(PlanMode.Free);
        plan.Xp.Should().Be(23.75m);
    }

    [Test]
    public void an_existing_characters_file_is_planned_freely_against_its_xp()
    {
        var plan = new PlanFile { Mode = PlanMode.Existing, Xp = 30m, Skills = [new () { Key = Book.Literacy, Times = 1 }] }.ToPlan();

        plan.Mode.Should().Be(PlanMode.Free, "VOID's skills for the character are not in the file");
        plan.Xp.Should().Be(30m);
    }

    [Test]
    public void a_negative_xp_from_a_hand_edited_file_counts_as_none()
        => new PlanFile { Mode = PlanMode.Free, Xp = -5m }.ToPlan().Xp.Should().Be(0m);

    [Test]
    public void as_json_it_says_what_it_is_and_carries_nothing_computed()
    {
        var json = JsonSerializer.Serialize(PlanFile.From("Ilse", Plan.Free(20m, Holdings.Of((Book.Literacy, 1))), "r1", DateTimeOffset.UnixEpoch), RulesetJsonContext.Default.PlanFile);

        json.Should().Contain("\"format\":\"vortex-character-creator\"")
            .And.Contain("\"mode\":\"Free\"")
            .And.Contain("\"skills\":[{\"key\":\"literacy\",\"times\":1}]")
            .And.NotContain("holdings");
    }

    [Test]
    public void a_file_missing_fields_still_reads()
    {
        var file = JsonSerializer.Deserialize("""{ "format": "vortex-character-creator" }""", RulesetJsonContext.Default.PlanFile)!;

        file.Name.Should().BeEmpty();
        file.Mode.Should().Be(PlanMode.NewCharacter);
        file.Skills.Should().BeEmpty();
    }
}
