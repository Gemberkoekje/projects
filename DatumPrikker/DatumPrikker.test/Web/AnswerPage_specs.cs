using DatumPrikker.WebApp.Pages.P;
using System.Reflection;

namespace DatumPrikker.test.Web;

public sealed class AnswerPage_specs
{
    /// <summary>D21: the answer page shows the leading tallies, but never anybody else's answers.</summary>
    [Test]
    public void the_answer_page_holds_no_answers_but_your_own()
    {
        var properties = typeof(PrikkenModel)
            .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(property => property.Name != nameof(PrikkenModel.Earlier)); // your own answer, to fill the form

        var reachable = new HashSet<Type>();
        foreach (var property in properties)
        {
            Collect(property.PropertyType, reachable);
        }

        reachable.Should().Contain(typeof(LikelyDate)).And.Contain(typeof(OptionTally));
        reachable.Should().NotContain(typeof(Answer)).And.NotContain(typeof(PollResults));
    }

    /// <summary>Every type a value of <paramref name="type"/> exposes through its properties, within this app.</summary>
    private static void Collect(Type type, HashSet<Type> reachable)
    {
        if (type.IsArray)
        {
            Collect(type.GetElementType()!, reachable);
            return;
        }

        foreach (var argument in type.IsGenericType ? type.GetGenericArguments() : [])
        {
            Collect(argument, reachable);
        }

        if (type.Namespace?.StartsWith("DatumPrikker", StringComparison.Ordinal) != true || !reachable.Add(type))
        {
            return;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            Collect(property.PropertyType, reachable);
        }
    }
}
