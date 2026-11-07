using System.Collections;
using CharacterCreator.core.Skills;

namespace CharacterCreator.core.Plans;

/// <summary>Skills with how often each is bought: a character's skills, or the purchases of a plan.</summary>
/// <remarks>
/// Immutable and thread-safe: every change returns new holdings. A skill bought zero times is not
/// held at all, so a skill is in <see cref="Skills"/> exactly when it is bought at least once.
/// </remarks>
public sealed class Holdings : IEnumerable<KeyValuePair<SkillKey, int>>
{
    private readonly Dictionary<SkillKey, int> times;
    private readonly SkillKey[] order;

    private Holdings(Dictionary<SkillKey, int> times, SkillKey[] order)
    {
        this.times = times;
        this.order = order;
    }

    /// <summary>No skills.</summary>
    public static Holdings None { get; } = new ([], []);

    /// <summary>The skills held, in the order they were first added.</summary>
    public IReadOnlyList<SkillKey> Skills => order;

    /// <summary>Whether no skill is held.</summary>
    public bool IsEmpty => order.Length == 0;

    /// <summary>How often <paramref name="skill"/> is bought; 0 when it is not held.</summary>
    /// <param name="skill">The skill.</param>
    /// <returns>The number of purchases.</returns>
    public int this[SkillKey skill] => times.TryGetValue(skill, out var n) ? n : 0;

    /// <summary>Holdings from skills and times. A skill named twice counts both; times below 1 are left out.</summary>
    /// <param name="skills">The skills with their times.</param>
    /// <returns>The holdings.</returns>
    public static Holdings Of(IEnumerable<KeyValuePair<SkillKey, int>> skills)
    {
        ArgumentNullException.ThrowIfNull(skills);

        var result = None;
        foreach (var (skill, n) in skills)
        {
            result = result.Add(skill, n);
        }

        return result;
    }

    /// <summary>Holdings from pairs, for short call sites and specs.</summary>
    /// <param name="skills">The skills with their times.</param>
    /// <returns>The holdings.</returns>
    public static Holdings Of(params (SkillKey Skill, int Times)[] skills)
        => Of((skills ?? []).Select(pair => new KeyValuePair<SkillKey, int>(pair.Skill, pair.Times)));

    /// <summary>These holdings with <paramref name="n"/> more purchases of <paramref name="skill"/>; a negative number removes purchases.</summary>
    /// <param name="skill">The skill.</param>
    /// <param name="n">Purchases to add, or to remove when negative.</param>
    /// <returns>The new holdings.</returns>
    public Holdings Add(SkillKey skill, int n)
    {
        if (skill.IsEmpty || n == 0)
        {
            return this;
        }

        var next = new Dictionary<SkillKey, int>(times);
        var total = this[skill] + n;
        if (total > 0)
        {
            next[skill] = total;
        }
        else
        {
            next.Remove(skill);
        }

        var nextOrder = order.Contains(skill) ? order : [.. order, skill];
        return new Holdings(next, [.. nextOrder.Where(next.ContainsKey)]);
    }

    /// <summary>These holdings without any purchase of <paramref name="skill"/>.</summary>
    /// <param name="skill">The skill.</param>
    /// <returns>The new holdings.</returns>
    public Holdings Without(SkillKey skill) => Add(skill, -this[skill]);

    /// <summary>These holdings and <paramref name="other"/> together.</summary>
    /// <param name="other">The holdings to add.</param>
    /// <returns>The sum.</returns>
    public Holdings Plus(Holdings other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var result = this;
        foreach (var (skill, n) in other)
        {
            result = result.Add(skill, n);
        }

        return result;
    }

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<SkillKey, int>> GetEnumerator()
        => order.Select(skill => new KeyValuePair<SkillKey, int>(skill, times[skill])).GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
