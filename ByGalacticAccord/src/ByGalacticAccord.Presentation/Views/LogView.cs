using System.Collections.Generic;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Engine.Simulation;
using ByGalacticAccord.Presentation.Session;
using ByGalacticAccord.Presentation.Text;

namespace ByGalacticAccord.Presentation.Views;

/// <summary>One line of the log: when, what, and whether it is about the player.</summary>
public sealed record LogLine(string When, string Text, bool Yours, ContractId? Contract);

public sealed record LogViewModel(string Title, string Empty, IReadOnlyList<LogLine> Lines);

/// <summary>
/// The captain's log (design §11.4): the journal's entries about contracts the player can see, newest
/// first, each worded by its template. The faucet adjustments are the economy's own bookkeeping and never
/// shown; others' routine (lots nobody bought, withdrawn posts, storage) is left out.
/// </summary>
public static class LogView
{
    public const int DefaultLines = 40;

    public static LogViewModel Build(GameSession session, int max = DefaultLines)
    {
        SimulationContext context = session.Context;
        IReadOnlyList<JournalEntry> entries = context.Journal.Entries;
        var lines = new List<LogLine>();
        for (int i = entries.Count - 1; i >= 0 && lines.Count < max; i--)
        {
            JournalEntry entry = entries[i];
            if (JournalText.ContractOf(entry) is not { } id)
            {
                continue;
            }

            Contract contract = context.Contract(id);
            bool yours = JournalText.Subject(entry) == session.Player.Id || JournalText.Addressee(entry) == session.Player.Id || contract.Involves(session.Player.Id);
            if (!yours && (Routine(entry, contract) || !Visibility.CanSee(context, session.Player, contract)))
            {
                continue;
            }

            lines.Add(new LogLine(Format.Date(entry.Tick), JournalText.Describe(context, entry), yours, id));
        }

        return new LogViewModel(Templates.Get("Log.Title"), Templates.Get("Log.Empty"), lines);
    }

    // Other people's routine: lots nobody bought, posts taken down, stores opened and emptied. True, and in the
    // journal, but not news; the player's own always shows.
    private static bool Routine(JournalEntry entry, Contract contract) => entry switch
    {
        ContractExpiredEntry => contract is SaleContract { Side: SaleSide.ForSale },
        ContractWithdrawnEntry or StorageOpenedEntry or StorageClosedEntry => true,
        _ => false,
    };
}
