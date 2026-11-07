using System.IO;
using System.Text;
using System.Text.Json;
using CharacterCreator.infrastructure.Void;

namespace CharacterCreator.test.Void;

/// <summary>Reading VOID's <c>GET /skills</c> answer (void <c>feature/skills-requirements-api</c>).</summary>
public sealed class VoidSkillsJson_specs
{
    /// <summary>Two skills as VOID writes them: snake_case, nulls for absent numbers, requirements inline.</summary>
    internal const string Answer = """
        {
          "class": "List",
          "url": "/skills",
          "list": [
            {
              "class": "Skill", "url": "/skills/7", "id": 7, "name": "healer A (spiritual)",
              "cost": 4, "base_max": 1, "times_max": 1,
              "mana_amount": 3, "manatype": { "class": "Manatype", "name": "Spiritual" },
              "imbue_cap": null, "imbue_type": null,
              "loresheet": true, "blanks": false, "deprecated": false,
              "requirements": [
                { "class": "SkillRequirement", "url": "/admin/skills/edit/7", "skill_id": 7, "kind": "any", "amount": 1, "skills": [2, 3] },
                { "class": "SkillRequirement", "url": "/admin/skills/edit/7", "skill_id": 7, "kind": "extra", "amount": 2, "skills": null }
              ]
            },
            {
              "class": "Skill", "url": "/skills/8", "id": 8, "name": "old skill",
              "cost": "2", "base_max": 0, "times_max": 3,
              "mana_amount": null, "manatype": null, "imbue_cap": 20, "imbue_type": { "name": "Elemental" },
              "loresheet": false, "blanks": false, "deprecated": true
            }
          ]
        }
        """;

    [Test]
    public async Task a_skill_comes_with_its_numbers_energy_and_prerequisites()
    {
        var skill = (await Parse(Answer)).Skills[0];

        skill.Id.Should().Be(7);
        skill.Name.Should().Be("healer A (spiritual)");
        skill.Cost.Should().Be(4);
        skill.BaseMax.Should().Be(1);
        skill.TimesMax.Should().Be(1);
        skill.Manatype.Should().Be("Spiritual");
        skill.ManaAmount.Should().Be(3);
        skill.ImbueType.Should().BeEmpty();
        skill.ImbueCap.Should().Be(0);
        skill.Loresheet.Should().BeTrue();
        skill.Deprecated.Should().BeFalse();
        skill.Requirements.Should().HaveCount(2);
        skill.Requirements[0].Kind.Should().Be("any");
        skill.Requirements[0].Amount.Should().Be(1);
        skill.Requirements[0].Skills.Should().Equal(2, 3);
    }

    [Test]
    public async Task an_extra_group_has_no_skills()
    {
        var extra = (await Parse(Answer)).Skills[0].Requirements[1];

        extra.Kind.Should().Be("extra");
        extra.Amount.Should().Be(2);
        extra.Skills.Should().BeEmpty();
    }

    [Test]
    public async Task nulls_become_empty_and_numbers_may_come_as_text()
    {
        var skill = (await Parse(Answer)).Skills[1];

        skill.Cost.Should().Be(2, "PHP may write a decimal column as a string");
        skill.Manatype.Should().BeEmpty();
        skill.ManaAmount.Should().Be(0);
        skill.ImbueType.Should().Be("Elemental");
        skill.ImbueCap.Should().Be(20);
        skill.Deprecated.Should().BeTrue();
        skill.Requirements.Should().BeEmpty("a VOID without the requirements branch sends none");
    }

    [Test]
    public async Task voids_own_answer_carries_no_snapshot_fields()
    {
        var envelope = await Parse(Answer);

        envelope.Source.Should().BeEmpty();
        envelope.TakenAt.Should().BeNull();
        envelope.Synthesized.Should().BeFalse();
    }

    [Test]
    public async Task the_offline_snapshot_says_where_and_when_in_voids_own_style()
    {
        var envelope = await Parse("""{ "source": "offline", "taken_at": "2026-11-07T00:00:00Z", "synthesized": true, "list": [] }""");

        envelope.Source.Should().Be("offline");
        envelope.TakenAt.Should().Be(new DateTimeOffset(2026, 11, 07, 00, 00, 00, TimeSpan.Zero));
        envelope.Synthesized.Should().BeTrue();
    }

    [TestCase("[]")]
    [TestCase("null")]
    [TestCase("{ \"list\": 5 }")]
    public async Task anything_else_is_refused(string json)
    {
        var parse = () => Parse(json);

        await parse.Should().ThrowAsync<JsonException>();
    }

    private static async Task<VoidSkillsJson.Envelope> Parse(string json)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return await VoidSkillsJson.ParseAsync(stream, CancellationToken.None);
    }
}
