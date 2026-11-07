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
    private static readonly SkillKey MageWhite = new ("mage-a-white");
    private static readonly SkillKey MageGrey = new ("mage-a-grey");
    private static readonly SkillKey MageBlack = new ("mage-a-black");

    private static readonly ContentBook Content = ContentBook.Create(
        "r",
        "b",
        [
            Skill(ArmourI, "Harnas I", "Armour I"),
            Skill(HealerEl, "Genezingsspreuken A (elementair)", "Healing Spells A (elemental)"),
            Skill(HealerSp, "Genezingsspreuken A (spiritueel)", "Healing Spells A (spiritual)"),
            Skill(MageWhite, "Magiërspreuken A - Wit", "Mage Spells A - White"),
            Skill(MageGrey, "Magiërspreuken A - Grijs", "Mage Spells A - Gray"),
            Skill(MageBlack, "Magiërspreuken A - Zwart", "Mage Spells A - Black"),
        ],
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
    public void a_choice_between_variants_of_one_skill_names_it_once()
    {
        Dutch.Requirement(RequirementGroup.Any(1, HealerEl, HealerSp)).Should().Be("een Genezingsspreuken A (elementair of spiritueel)");
        English.Requirement(RequirementGroup.Any(1, MageWhite, MageGrey, MageBlack)).Should().Be("a Mage Spells A (White, Gray or Black)");
    }

    [Test]
    public void a_choice_between_different_skills_says_one_of_and_still_folds_the_variants()
        => Dutch.Requirement(RequirementGroup.Any(1, ArmourI, MageWhite, MageGrey))
            .Should().Be("één van: Harnas I, Magiërspreuken A (Wit of Grijs)");

    [Test]
    public void xp_in_variants_of_one_skill_is_folded_too()
        => Dutch.Requirement(RequirementGroup.Points(4, MageWhite, MageGrey, MageBlack))
            .Should().Be("4 XP in: Magiërspreuken A (Wit, Grijs of Zwart)");

    [Test]
    public void a_status_pill_says_nothing_for_a_skill_that_can_simply_be_bought()
        => Dutch.Status(new SkillOption { Skill = ArmourI, Offered = true, Reasons = [] }, held: false).Should().Be(SkillStatus.None);

    [Test]
    public void a_status_pill_says_chosen_for_a_skill_the_plan_has()
        => Dutch.Status(new SkillOption { Skill = ArmourI, Offered = true, Reasons = [new NotEnoughXp { Skill = ArmourI, Needed = 2, Available = 0 }] }, held: true)
            .Should().BeEquivalentTo(new { Kind = StatusKind.Chosen, Text = "gekozen" });

    [Test]
    public void a_status_pill_puts_a_missing_prerequisite_first()
    {
        var option = new SkillOption
        {
            Skill = HealerEl,
            Offered = true,
            Reasons = [new NotEnoughXp { Skill = HealerEl, Needed = 4, Available = 1 }, new MissingPrerequisite { Skill = HealerEl, Group = RequirementGroup.Any(1, ArmourI) }],
        };

        var status = Dutch.Status(option, held: false);

        status.Kind.Should().Be(StatusKind.Warning);
        status.Text.Should().Be("vereist Harnas I");
        status.Full.Should().Contain("niet genoeg XP").And.Contain("vereist Harnas I");
    }

    [Test]
    public void a_status_pill_says_too_dear_in_few_words()
    {
        var status = English.Status(new SkillOption { Skill = ArmourI, Offered = true, Reasons = [new NotEnoughXp { Skill = ArmourI, Needed = 2, Available = 1 }] }, held: false);

        status.Should().BeEquivalentTo(new { Kind = StatusKind.Blocked, Text = "too dear", Full = "not enough XP: needs 2, 1 left" });
    }

    [Test]
    public void a_status_pill_says_a_teacher_is_needed_as_something_to_know()
        => English.Status(new SkillOption { Skill = ArmourI, Offered = true, Reasons = [new NeedsTeacher { Skill = ArmourI }] }, held: false)
            .Should().BeEquivalentTo(new { Kind = StatusKind.Info, Text = "needs a teacher" });

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

    [Test]
    public void the_verdict_counts_problems_and_warnings_but_not_what_is_only_to_know()
    {
        Dutch.Verdict(Evaluation(10, 15, new NeedsTeacher { Skill = ArmourI })).Should().Be("Alles klopt");
        Dutch.Verdict(Evaluation(10, 15, new TooMuchLeftOver { Left = 5, Limit = 3 })).Should().Be("1 ding klopt nog niet");
        English.Verdict(Evaluation(17, 15, new NotEnoughXp { Needed = 17, Available = 15 }, new MissingPrerequisite { Skill = HealerEl, Group = RequirementGroup.Any(1, ArmourI) }))
            .Should().Be("2 things aren't right yet");
    }

    [Test]
    public void the_open_findings_put_problems_first()
    {
        var evaluation = Evaluation(17, 15, new NotEnoughXp { Needed = 17, Available = 15 }, new NeedsTeacher { Skill = ArmourI });

        FindingWords.Open(evaluation).Should().ContainSingle().Which.Should().BeOfType<NotEnoughXp>();
    }

    [TestCase(13, "13 / 15 XP", "nog 2 over")]
    [TestCase(15, "15 / 15 XP", "alles besteed")]
    [TestCase(16.5, "16,5 / 15 XP", "1,5 te veel")]
    public void the_budget_says_what_is_spent_and_what_is_left(decimal spent, string numbers, string left)
    {
        var evaluation = Evaluation(spent, 15);

        Dutch.BudgetSpent(evaluation).Should().Be(numbers);
        Dutch.BudgetLeft(evaluation).Should().Be(left);
    }

    [Test]
    public void the_budget_is_english_too()
        => English.BudgetLeft(Evaluation(13, 15)).Should().Be("2 left");

    [Test]
    public void a_plan_that_keeps_every_rule_ticks_all_three_lines()
    {
        var lines = Dutch.Checklist(Evaluation(13, 15, new NeedsTeacher { Skill = ArmourI }), CreationRules.Vortex);

        lines.Select(line => line.Kind).Should().Equal(CheckKind.Budget, CheckKind.Prerequisites, CheckKind.Rules);
        lines.Should().AllSatisfy(line => line.IsOk.Should().BeTrue());
        lines[0].Text.Should().Be("13 van 15 XP besteed (hoogstens 3 over)");
    }

    [Test]
    public void each_open_finding_lands_on_one_line_named_with_its_skill()
    {
        var lines = English.Checklist(
            Evaluation(
                12,
                15,
                new TooMuchLeftOver { Left = 3.5m, Limit = 3 },
                new MissingPrerequisite { Skill = HealerEl, Group = RequirementGroup.Any(1, ArmourI) },
                new MissingPrerequisite { Skill = HealerSp, Group = RequirementGroup.Any(1, ArmourI) },
                new AboveCreationMaximum { Skill = ArmourI, Times = 2, Maximum = 1 }),
            CreationRules.Vortex);

        lines[0].Should().BeEquivalentTo(new { IsOk = false, Text = "3.5 XP left over; everything above 3 is lost", More = 0 });
        lines[1].Should().BeEquivalentTo(new { IsOk = false, Text = "Healing Spells A (elemental): needs Armour I", More = 1 });
        lines[2].Should().BeEquivalentTo(new { IsOk = false, Text = "Armour I: at most 1× at creation", More = 0 });
    }

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

    private static PlanEvaluation Evaluation(decimal spent, decimal xp, params Finding[] findings) => new ()
    {
        Plan = core.Plans.Plan.NewCharacter(core.Plans.Holdings.None),
        Xp = xp,
        Spent = spent,
        Remaining = xp - spent,
        Findings = [.. findings.OrderByDescending(finding => finding.Severity)],
        Energy = [],
    };

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
