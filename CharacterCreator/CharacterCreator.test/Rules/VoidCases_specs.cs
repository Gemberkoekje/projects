using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CharacterCreator.core.Plans;
using CharacterCreator.core.Rules;
using CharacterCreator.core.Skills;

namespace CharacterCreator.test.Rules;

/// <summary>
/// VOID's shared cases (<c>tests/data/skill-rules-cases.json</c>, DESIGN.md §3.5): the prerequisites,
/// whole XP and the XP table, run against this engine exactly as VOID's PHPUnit runs them against
/// <c>SkillRules</c> and <c>XpAward</c>. If one fails, the two engines disagree, and VOID is right.
/// </summary>
public sealed class VoidCases_specs
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Rules");
    private static readonly string CasesPath = Path.Combine(Folder, "skill-rules-cases.json");
    private static readonly string SourcePath = Path.Combine(Folder, "skill-rules-cases.source");

    private static readonly JsonSerializerOptions Json = new () { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private static readonly CasesFile Cases = JsonSerializer.Deserialize<CasesFile>(File.ReadAllText(CasesPath), Json)!;

    public static IEnumerable<TestCaseData> MissingCases()
        => Cases.Missing.Select(@case => new TestCaseData(@case).SetName($"missing: {@case.Case}"));

    public static IEnumerable<TestCaseData> WholeCases()
        => Cases.Whole.Select(@case => new TestCaseData(@case.Xp, @case.Whole).SetName(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"whole: {@case.Xp} XP buys with {@case.Whole}")));

    public static IEnumerable<TestCaseData> AwardCases()
        => Cases.Award.Select(@case => new TestCaseData(@case.Xp, @case.Award).SetName(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"award: an event started at {@case.Xp} XP adds {@case.Award}")));

    [TestCaseSource(nameof(MissingCases))]
    public void the_engine_finds_the_groups_void_finds_missing(MissingCase @case)
    {
        var book = RuleBook.Create(Cases.Skills.Select(skill => new RuleSkill
        {
            Key = Key(skill.Id),
            Cost = skill.Cost,
            BaseMax = skill.BaseMax,
            TimesMax = 5,
        })).Value;
        var groups = @case.Requirements
            .Select(group => new RequirementGroup(RequirementKinds.FromVoid(group.Kind), group.Amount, (group.Skills ?? []).Select(Key)))
            .ToList();
        var holdings = Holdings.Of(@case.Character.Select(pair => new KeyValuePair<SkillKey, int>(new SkillKey(pair.Key), pair.Value)));

        var missing = SkillRules.Missing(groups, holdings, book);

        var indexes = groups.Select((group, index) => (group, index))
            .Where(pair => missing.Any(found => ReferenceEquals(found, pair.group)))
            .Select(pair => pair.index);
        indexes.Should().Equal(@case.Missing, @case.Case);
    }

    [TestCaseSource(nameof(WholeCases))]
    public void whole_xp_is_what_void_calls_whole(decimal xp, int whole)
        => SkillRules.Whole(xp).Should().Be(whole);

    [TestCaseSource(nameof(AwardCases))]
    public void an_event_awards_what_void_awards(decimal xp, decimal award)
        => XpTable.Vortex.AwardFor(xp).Should().Be(award);

    [Test]
    public void the_copy_is_the_one_its_source_file_names()
    {
        var text = File.ReadAllText(CasesPath).ReplaceLineEndings("\n");
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        var recorded = File.ReadAllLines(SourcePath).Single(line => line.StartsWith("sha256=", StringComparison.Ordinal))["sha256=".Length..];

        hash.Should().Be(recorded, "the copy is only ever changed by scripts/sync-void-cases.ps1, which records VOID's commit and the hash");
    }

    private static SkillKey Key(int id) => new (id.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private sealed record CasesFile
    {
        public List<CaseSkill> Skills { get; init; } = [];

        public List<MissingCase> Missing { get; init; } = [];

        public List<WholeCase> Whole { get; init; } = [];

        public List<AwardCase> Award { get; init; } = [];
    }

    public sealed record CaseSkill
    {
        public int Id { get; init; }

        public int Cost { get; init; }

        public int BaseMax { get; init; }
    }

    public sealed record MissingCase
    {
        public string Case { get; init; } = string.Empty;

        public List<CaseGroup> Requirements { get; init; } = [];

        public Dictionary<string, int> Character { get; init; } = [];

        public List<int> Missing { get; init; } = [];

        public override string ToString() => Case;
    }

    public sealed record CaseGroup
    {
        public string Kind { get; init; } = string.Empty;

        public int Amount { get; init; }

        public List<int>? Skills { get; init; }
    }

    public sealed record WholeCase
    {
        public decimal Xp { get; init; }

        public int Whole { get; init; }
    }

    public sealed record AwardCase
    {
        public decimal Xp { get; init; }

        public decimal Award { get; init; }
    }
}
