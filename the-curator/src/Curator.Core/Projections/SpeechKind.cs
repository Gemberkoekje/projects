namespace Curator.Core.Projections;

/// <summary>Who says a line in the visit's transcript, and what kind of line it is.</summary>
public enum SpeechKind
{
    None = 0,

    /// <summary>The patron passes on gossip as they arrive.</summary>
    Gossip,

    /// <summary>The patron says how an earlier loan went.</summary>
    Return,

    /// <summary>The patron's greeting.</summary>
    Greeting,

    /// <summary>The patron's request.</summary>
    Request,

    /// <summary>The curator says the requested book is out.</summary>
    BookOut,

    /// <summary>The curator asks a question.</summary>
    Asked,

    /// <summary>The patron answers it.</summary>
    Answer,

    /// <summary>The curator offers another book; the text is its title.</summary>
    Offered,

    /// <summary>The patron turns the offer down.</summary>
    Refused,

    /// <summary>The patron's exit line.</summary>
    Exit,
}
