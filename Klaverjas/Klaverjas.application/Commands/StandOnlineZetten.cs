namespace Klaverjas.application.Commands;

/// <summary>"Zet de stand online" (D2): the standings as they are now go to the players' page.</summary>
public sealed record StandOnlineZetten : ICommand
{
    /// <summary>Dad's note above the standings; may be empty.</summary>
    public string Bericht { get; init; } = string.Empty;
}
