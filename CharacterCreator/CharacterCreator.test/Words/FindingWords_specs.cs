using CharacterCreator.core.Content;
using CharacterCreator.core.Rules;
using CharacterCreator.core.Skills;
using CharacterCreator.WebApp.Client.Words;

namespace CharacterCreator.test.Words;

/// <summary>The engine's findings in words, Dutch and English (DESIGN.md §6: the UI words the facts).</summary>
public sealed class FindingWords_specs
{
    private static readonly SkillKey ArmourI = new ("armour-i");
    private static readonly SkillKey HealerEl = new ("healer-a-elemental");
    private static readonly SkillKey HealerSp = new ("healer-a-spiritual");

    private static readonly ContentBook Content = ContentBook.Create(
        "r",
        "b",
        [Skill(ArmourI, "Harnas I", "Armour I"), Skill(HealerEl, "Genezingsspreuken A (elementair)", "Healing Spells A (elemental)"), Skill(HealerSp, "Genezingsspreuken A (spiritueel)", "Healing Spells A (spiritual)")],
        [],
        []).Value;

    private static readonly FindingWords Dutch = new (Ui.Dutch, Content);
    private static readonly FindingWords English = new (new Ui(Language.English), Content);

    [Test]
    public void a_missing_skill_is_named_in_the_screens_language()
    {
        var missing = new MissingPrerequisite { Skill = HealerEl, Group = RequirementGroup.Any(1, ArmourI) };

        Dutch.Describe(missing).Should().Be("vereist Harnas I");
        English.Describe(missing).Should().Be("needs Armour I");
    }

    [Test]
    public void a_choice_says_one_of()
        => Dutch.Requirement(RequirementGroup.Any(1, HealerEl, HealerSp))
            .Should().Be("één van: Genezingsspreuken A (elementair), Genezingsspreuken A (spiritueel)");

    [Test]
    public void a_group_needing_more_than_one_purchase_says_how_many()
        => English.Requirement(RequirementGroup.Any(3, ArmourI)).Should().Be("Armour I 3×");

    [Test]
    public void xp_in_a_set_and_extra_skills_are_worded_too()
    {
        Dutch.Requirement(RequirementGroup.Points(4, ArmourI, HealerEl)).Should().Be("4 XP in: Harnas I, Genezingsspreuken A (elementair)");
        English.Requirement(RequirementGroup.Extra(1)).Should().Be("an extra skill");
        Dutch.Requirement(RequirementGroup.Extra(2)).Should().Be("2 extra vaardigheden");
    }

    [Test]
    public void a_skill_outside_the_book_is_never_named()
    {
        Dutch.Name(new ("hidden-42")).Should().Be("een vaardigheid buiten het boek");
        English.Describe(new MissingPrerequisite { Skill = ArmourI, Group = RequirementGroup.Any(1, new SkillKey("hidden-42")) })
            .Should().Be("needs a skill outside the book");
    }

    [Test]
    public void xp_is_written_with_a_decimal_comma_in_dutch()
    {
        var left = new TooMuchLeftOver { Left = 3.5m, Limit = 3m };

        Dutch.Describe(left).Should().Be("3,5 XP over; alles boven 3 vervalt");
        English.Describe(left).Should().Be("3.5 XP left over; everything above 3 is lost");
    }

    [Test]
    public void every_finding_has_words_in_both_languages()
    {
        Finding[] findings =
        [
            new NotEnoughXp { Needed = 4, Available = 1.5m },
            new AboveMaximum { Skill = ArmourI, Times = 2, Maximum = 1 },
            new AboveCreationMaximum { Skill = ArmourI, Times = 2, Maximum = 1 },
            new NotAtCreation { Skill = ArmourI },
            new DeprecatedSkill { Skill = ArmourI },
            new UnknownSkill { Skill = ArmourI, Held = false },
            new NeedsTeacher { Skill = ArmourI },
            new TeachingNotPaidOff { Skill = ArmourI, Owed = 2 },
            new MoreThanOneTeaching { Skills = [ArmourI, HealerEl] },
            new TooMuchLeftOver { Left = 4, Limit = 3 },
        ];

        foreach (var finding in findings)
        {
            var nl = Dutch.Describe(finding);
            var en = English.Describe(finding);
            nl.Should().NotBeNullOrWhiteSpace().And.NotBe(finding.GetType().Name);
            en.Should().NotBeNullOrWhiteSpace().And.NotBe(nl, finding.GetType().Name);
        }
    }

    [Test]
    public void a_skill_that_can_simply_be_bought_says_so()
        => English.State(new SkillOption { Skill = ArmourI, Offered = true, Reasons = [] }).Should().Be("can buy");

    [TestCase("Elemental", "Elementair")]
    [TestCase("Spiritual ritual", "Spiritueel ritueel")]
    [TestCase("Willpower", "Wilskracht")]
    [TestCase("Something new", "Something new")]
    public void voids_energy_types_are_named_in_dutch(string type, string dutch)
        => Dutch.EnergyType(type).Should().Be(dutch);

    [TestCase("en", Language.English)]
    [TestCase("EN", Language.English)]
    [TestCase("nl", Language.Dutch)]
    [TestCase("fr", Language.Dutch)]
    [TestCase("", Language.Dutch)]
    public void a_language_code_falls_back_to_dutch(string code, Language language)
        => Ui.For(code).Language.Should().Be(language);

    private static SkillContent Skill(SkillKey key, string nl, string en) => new ()
    {
        Key = key,
        Void = key.ToString(),
        Book = [nl],
        Name = new LocalizedText { Nl = nl, En = en },
        Category = LocalizedText.Empty,
        Page = 0,
        PageFrom = string.Empty,
        BookCost = 0,
        Description = LocalizedText.Empty,
        Properties = [],
        Spells = [],
        Recipes = [],
        Loresheet = Loresheet.None,
    };
}
