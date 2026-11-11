namespace DatumPrikker.WebApp.Pages.Shared;

/// <summary>What the "Dit wordt het" card shows (D22): the picked date, and for whom.</summary>
public sealed record PickCard
{
    /// <summary>The poll, with its pick.</summary>
    public Poll Poll { get; init; } = new Poll();

    /// <summary>What this respondent said to the picked date ("Jij zei: ja"); none for anybody else.</summary>
    public Availability YourChoice { get; init; }

    /// <summary>True for the owner on the results page: the undo button and the hint to pick another date.</summary>
    public bool ForOwner { get; init; }
}
