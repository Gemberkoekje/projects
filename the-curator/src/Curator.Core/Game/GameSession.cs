using Curator.Core.Commands;
using Curator.Core.Content;
using Curator.Core.Events;
using Curator.Core.Projections;

namespace Curator.Core.Game;

/// <summary>
/// The game: content, the event log, the state folded from it, and the curator's notes.
/// Godot sends commands here and renders <see cref="Views"/> (BUILD_BRIEF §4.1).
/// </summary>
public sealed class GameSession
{
    private readonly List<GameEvent> events = [];

    /// <summary>A session with an empty log; send <see cref="NewGame"/> to begin.</summary>
    /// <param name="content">The content.</param>
    /// <param name="debugEnabled">Whether Debug… commands are allowed.</param>
    public GameSession(ContentSet content, bool debugEnabled = false)
    {
        ArgumentNullException.ThrowIfNull(content);
        Content = content;
        DebugEnabled = debugEnabled;
        State = new GameState();
        Notes = new Notes();
        Views = new Projector(this);
    }

    public ContentSet Content { get; }

    /// <summary>The state folded from <see cref="Events"/>.</summary>
    public GameState State { get; private set; }

    public IReadOnlyList<GameEvent> Events => events;

    /// <summary>The curator's free text, saved beside the log.</summary>
    public Notes Notes { get; }

    public bool DebugEnabled { get; }

    /// <summary>Whether this session was restored from a save made with different content.</summary>
    public bool ContentChanged { get; private set; }

    /// <summary>Every view Godot renders.</summary>
    public Projector Views { get; }

    /// <summary>Restores a saved game by replaying its log.</summary>
    /// <param name="content">The content.</param>
    /// <param name="save">The save.</param>
    /// <param name="debugEnabled">Whether Debug… commands are allowed.</param>
    /// <returns>The session.</returns>
    /// <exception cref="SaveIncompatibleException">The save was written by another schema, or no longer replays.</exception>
    public static GameSession Restore(ContentSet content, SaveData save, bool debugEnabled = false)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(save);
        if (save.SchemaVersion != SaveData.CurrentSchemaVersion)
        {
            throw new SaveIncompatibleException($"The save uses schema {save.SchemaVersion}; this build reads {SaveData.CurrentSchemaVersion}.");
        }

        var session = new GameSession(content, debugEnabled);
        session.events.AddRange(save.Events);
        try
        {
            session.State = GameState.Replay(session.events);
            _ = session.Views.Hud();
            _ = session.Views.Visit();
            _ = session.Views.Morning();
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            throw new SaveIncompatibleException("The save no longer matches the game's content.", ex);
        }

        session.ContentChanged = save.ContentVersion != content.Version;
        foreach (var (patronId, text) in save.FreeNotes)
        {
            session.Notes.SetFreeNotes(patronId, text);
        }

        foreach (var (loanId, text) in save.LedgerNotes)
        {
            session.Notes.SetLedgerNote(loanId, text);
        }

        return session;
    }

    /// <summary>Whether a command would be accepted now. Changes nothing; the views use it for their buttons.</summary>
    /// <param name="command">The command.</param>
    /// <returns>Why it would be refused, or None.</returns>
    public RejectionReason Check(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return new CommandProcessor(Content, State, DebugEnabled).Check(command);
    }

    /// <summary>Validates a command and, if it's allowed, appends its events.</summary>
    /// <param name="command">The command.</param>
    /// <returns>The events, or why the command was refused.</returns>
    public CommandResult Handle(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var processor = new CommandProcessor(Content, State, DebugEnabled);
        RejectionReason rejection;
        try
        {
            rejection = processor.Run(command);
        }
        catch
        {
            State = GameState.Replay(events);
            throw;
        }

        if (rejection != RejectionReason.None)
        {
            if (processor.Emitted.Count > 0)
            {
                State = GameState.Replay(events);
                throw new InvalidOperationException($"{command} was refused ({rejection}) after emitting events.");
            }

            return CommandResult.Refused(rejection);
        }

        events.AddRange(processor.Emitted);
        return CommandResult.Done(processor.Emitted);
    }

    /// <summary>The save for this session as it stands.</summary>
    /// <returns>The save.</returns>
    public SaveData ToSave() => new()
    {
        SchemaVersion = SaveData.CurrentSchemaVersion,
        Seed = State.Seed,
        ContentVersion = State.ContentVersion,
        Events = events.ToList(),
        FreeNotes = new Dictionary<string, string>(Notes.Free, StringComparer.Ordinal),
        LedgerNotes = new Dictionary<string, string>(Notes.Ledger, StringComparer.Ordinal),
    };
}
