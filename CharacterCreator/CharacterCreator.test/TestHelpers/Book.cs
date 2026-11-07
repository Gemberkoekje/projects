using CharacterCreator.core.Rules;
using CharacterCreator.core.Skills;

namespace CharacterCreator.test.TestHelpers;

/// <summary>
/// A small rule book of book skills, shaped like VOID's: chains (lifeforce, armour), a choice
/// (paladin), XP in a set (brew expertise), levels (ritualism), extra skills and energy.
/// </summary>
internal static class Book
{
    public static readonly SkillKey Literacy = new ("literacy");
    public static readonly SkillKey LifeforceA = new ("lifeforce-a");
    public static readonly SkillKey LifeforceB = new ("lifeforce-b");
    public static readonly SkillKey ArmourI = new ("armour-i");
    public static readonly SkillKey ArmourII = new ("armour-ii");
    public static readonly SkillKey ArmourIII = new ("armour-iii");
    public static readonly SkillKey HealerAElemental = new ("healer-a-elemental");
    public static readonly SkillKey HealerASpiritual = new ("healer-a-spiritual");
    public static readonly SkillKey Paladin = new ("paladin");
    public static readonly SkillKey HerbLore = new ("herb-lore");
    public static readonly SkillKey BrewPoisonA = new ("brew-poison-a");
    public static readonly SkillKey BrewExpertise = new ("brew-expertise");
    public static readonly SkillKey ElementalRitualism = new ("elemental-ritualism");
    public static readonly SkillKey SpiritualRitualism = new ("spiritual-ritualism");
    public static readonly SkillKey GroupRitualSpecialist = new ("group-ritual-specialist");
    public static readonly SkillKey RitualMaster = new ("ritual-master");
    public static readonly SkillKey Perseverance = new ("perseverance");
    public static readonly SkillKey StrengthB = new ("strength-b");
    public static readonly SkillKey TeachingExpertise = new ("teaching-expertise");
    public static readonly SkillKey GlyphUse = new ("glyph-use");
    public static readonly SkillKey OldSkill = new ("old-skill");
    public static readonly SkillKey Unknown = new ("not-in-the-book");

    public static RuleBook Standard { get; } = RuleBook.Create(Skills()).Value;

    private static IEnumerable<RuleSkill> Skills()
    {
        yield return Skill(Literacy, cost: 1);
        yield return Skill(LifeforceA, cost: 2);
        yield return Skill(LifeforceB, cost: 2) with { Requirements = [RequirementGroup.Any(1, LifeforceA)] };
        yield return Skill(ArmourI, cost: 1);
        yield return Skill(ArmourII, cost: 2) with { Requirements = [RequirementGroup.Any(1, ArmourI)] };
        yield return Skill(ArmourIII, cost: 3) with { Requirements = [RequirementGroup.Any(1, ArmourII)] };
        yield return Skill(HealerAElemental, cost: 4) with { Energy = [Grant(EnergyKind.Energy, "Elemental", 3)] };
        yield return Skill(HealerASpiritual, cost: 4) with { Energy = [Grant(EnergyKind.Energy, "Spiritual", 3)] };
        yield return Skill(Paladin, cost: 2) with
        {
            Requirements = [RequirementGroup.Any(1, HealerAElemental, HealerASpiritual), RequirementGroup.Any(1, ArmourI)],
        };
        yield return Skill(HerbLore, cost: 1);
        yield return Skill(BrewPoisonA, cost: 2) with { Requirements = [RequirementGroup.Any(1, HerbLore)] };
        yield return Skill(BrewExpertise, cost: 1, baseMax: 0) with
        {
            Requirements = [RequirementGroup.Points(3, HerbLore, BrewPoisonA, BrewExpertise)],
        };
        yield return Skill(ElementalRitualism, cost: 1, baseMax: 5, timesMax: 5) with { Energy = [Grant(EnergyKind.Energy, "Elemental ritual", 1)] };
        yield return Skill(SpiritualRitualism, cost: 1, baseMax: 5, timesMax: 5);
        yield return Skill(GroupRitualSpecialist, cost: 2, baseMax: 0) with
        {
            Requirements = [RequirementGroup.Any(3, ElementalRitualism, SpiritualRitualism)],
        };
        yield return Skill(RitualMaster, cost: 2, baseMax: 0) with { Requirements = [RequirementGroup.Any(3, ElementalRitualism)] };
        yield return Skill(Perseverance, cost: 1) with { Energy = [Grant(EnergyKind.Energy, "Willpower", 1)] };
        yield return Skill(StrengthB, cost: 3, baseMax: 0) with { Requirements = [RequirementGroup.Any(1, Perseverance)] };
        yield return Skill(TeachingExpertise, cost: 2, baseMax: 0, timesMax: 3) with { Requirements = [RequirementGroup.Extra(1)] };
        yield return Skill(GlyphUse, cost: 1) with { Energy = [Grant(EnergyKind.ImbueCap, "Elemental", 2)] };
        yield return Skill(OldSkill, cost: 1) with { Deprecated = true };
    }

    private static RuleSkill Skill(SkillKey key, int cost, int baseMax = 1, int timesMax = 1)
        => new () { Key = key, Cost = cost, BaseMax = baseMax, TimesMax = timesMax };

    private static EnergyGrant Grant(EnergyKind kind, string type, int amount)
        => new () { Kind = kind, Type = type, Amount = amount };
}
