using Curator.Core.Commands;
using Curator.Core.Events;
using Curator.Core.Game;

namespace Curator.Core.Tests;

/// <summary>Shorthand for driving a session in tests.</summary>
internal static class Play
{
    public static CommandResult Do(this GameSession session, Command command)
    {
        var result = session.Handle(command);
        Assert.True(result.Accepted, $"{command} was refused: {result.Rejection}");
        return result;
    }

    public static void Refused(this GameSession session, Command command, RejectionReason reason)
    {
        var before = session.Events.Count;
        var result = session.Handle(command);
        Assert.Equal(reason, result.Rejection);
        Assert.Equal(before, session.Events.Count);
    }

    public static T Last<T>(this GameSession session)
        where T : GameEvent => session.Events.OfType<T>().Last();

    public static IEnumerable<T> All<T>(this GameSession session)
        where T : GameEvent => session.Events.OfType<T>();

    /// <summary>Rings the bell and asserts which visit came.</summary>
    public static void Ring(this GameSession session, string expectedVisitId)
    {
        session.Do(new RingBell());
        Assert.True(session.State.InVisit, $"expected {expectedVisitId}, but nobody came");
        Assert.Equal(expectedVisitId, session.State.Visit.VisitId);
    }

    /// <summary>Settles the visit at the counter without investigating: lend if possible, else decline, else offer.</summary>
    public static void Settle(this GameSession session)
    {
        var visit = session.Views.Visit();
        if (visit.CanLendRequested)
        {
            session.Do(new LendRequested());
        }
        else if (visit.CanDecline)
        {
            session.Do(new Decline());
        }
        else
        {
            var book = session.Views.Catalogue().Cards.First(c => c.Status == BookStatus.OnShelf && c.BookId != visit.RequestedBookId);
            session.Do(new OfferBook(book.BookId));
        }
    }

    /// <summary>Rings and settles visits today until the given visit is at the counter.</summary>
    public static void FinishDayUntil(this GameSession session, string visitId)
    {
        while (session.Views.Hud().SlotsLeft)
        {
            session.Do(new RingBell());
            if (session.State.InVisit && session.State.Visit.VisitId == visitId)
            {
                return;
            }

            while (session.State.InVisit)
            {
                session.Settle();
            }
        }

        Assert.Fail($"{visitId} never came today.");
    }

    /// <summary>Plays the rest of today with <paramref name="playVisit"/> and closes.</summary>
    public static void FinishDay(this GameSession session, Action<GameSession> playVisit)
    {
        while (session.State.InVisit)
        {
            playVisit(session);
        }

        while (session.Views.Hud().SlotsLeft)
        {
            session.Do(new RingBell());
            while (session.State.InVisit)
            {
                playVisit(session);
            }
        }

        session.Do(new CloseForTheDay());
    }

    /// <summary>A visit policy: the given plays for named visits, <see cref="Settle"/> for the rest.</summary>
    public static Action<GameSession> Policy(params (string VisitId, Action<GameSession> Play)[] plays)
    {
        var byVisit = plays.ToDictionary(p => p.VisitId, p => p.Play, StringComparer.Ordinal);
        return session =>
        {
            if (byVisit.TryGetValue(session.State.Visit.VisitId, out var play))
            {
                play(session);
                if (session.State.InVisit)
                {
                    session.Settle();
                }
            }
            else
            {
                session.Settle();
            }
        };
    }

    /// <summary>Plays days until the given morning, settling every visit plainly.</summary>
    public static void ToDay(this GameSession session, int day) => session.ToDay(day, s => s.Settle());

    public static void ToDay(this GameSession session, int day, Action<GameSession> playVisit)
    {
        while (session.State.Day < day && session.State.Status != DayStatus.WeekOver)
        {
            session.FinishDay(playVisit);
        }
    }
}
