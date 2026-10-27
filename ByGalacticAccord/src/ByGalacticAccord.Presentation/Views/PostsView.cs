using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;
using ByGalacticAccord.Presentation.Session;
using ByGalacticAccord.Presentation.Text;

namespace ByGalacticAccord.Presentation.Views;

/// <summary>A line about one of the player's posts or haggles: what it is, where it stands, and a flag when it waits on the player.</summary>
public sealed record PostLine(ContractId Contract, string Head, string Detail, string? Flag, Tone Tone);

/// <summary>
/// "My posts" (design §11.3: "your own posts appear here with their responses") and the player's haggles over others' posts
/// (§11.3, "Negotiations"): each line selects its contract, where the threads are.
/// </summary>
public sealed record PostsViewModel(
    string Title,
    string NewPostLabel,
    string PostsTitle,
    IReadOnlyList<PostLine> Posts,
    string PostsEmpty,
    string HagglesTitle,
    IReadOnlyList<PostLine> Haggles,
    string HagglesEmpty);

public static class PostsView
{
    /// <summary>How many ended haggles the list keeps under the open ones.</summary>
    public const int EndedShown = 3;

    public static PostsViewModel Build(GameSession session)
    {
        SimulationContext context = session.Context;
        ActorId player = session.Player.Id;
        var posts = new List<PostLine>();
        foreach (Contract post in session.PlayerOpenPosts)
        {
            IReadOnlyList<Negotiation> threads = context.NegotiationsOn(post.Id);
            int waiting = threads.Count(t => t.AwaitingFrom == player);
            string detail = Templates.Fill(
                "Posts.Detail", ("kind", Templates.KindLine(post)), ("price", Format.Money(post.OfferedPrice)), ("deadline", Format.Date(post.DeadlineTick)));
            string answers = threads.Count == 0
                ? Templates.Get("Posts.Answers.None")
                : Templates.Fill("Posts.Answers", ("count", threads.Count.ToString(CultureInfo.InvariantCulture)));
            string? flag = waiting > 0 ? Templates.Fill("Posts.Waiting", ("count", waiting.ToString(CultureInfo.InvariantCulture))) : null;
            posts.Add(new PostLine(post.Id, ContractView.Head(context, post), detail + " · " + answers, flag, waiting > 0 ? Tone.Warn : Tone.Muted));
        }

        var haggles = new List<PostLine>();
        int ended = 0;
        foreach (Negotiation thread in session.PlayerNegotiations)
        {
            if (thread.Poster == player || (!thread.IsOpen && ended++ >= EndedShown))
            {
                continue; // answers to the player's own posts sit under its posts
            }

            Contract contract = context.Contract(thread.Contract);
            ThreadViewModel view = NegotiationView.Thread(session, thread);
            string detail = Templates.Fill("Haggles.Detail", ("party", context.Actor(thread.Poster).Name), ("offer", Templates.Offer(thread.Latest.Offer)));
            haggles.Add(new PostLine(
                contract.Id, ContractView.Head(context, contract), detail + " · " + view.Status, view.YourTurn ? Templates.Get("Haggles.YourTurn") : null,
                view.YourTurn ? Tone.Warn : view.StatusTone));
        }

        return new PostsViewModel(
            Templates.Get("Posts.Title"), Templates.Get("Posts.New"),
            Templates.Get("Posts.Yours"), posts, Templates.Get("Posts.Empty"),
            Templates.Get("Haggles.Title"), haggles, Templates.Get("Haggles.Empty"));
    }
}
