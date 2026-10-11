using Curator.Core.Projections;

namespace Curator.Presentation.Game;

/// <summary>
/// Everything the views draw after a command: fresh projections from Core plus the presentation's own
/// state (which document is open, which returned books were shelved, where the camera looks).
/// </summary>
/// <param name="Hud">The HUD.</param>
/// <param name="Visit">The visit at the counter.</param>
/// <param name="Morning">Today's paper, letters and returns.</param>
/// <param name="Catalogue">The catalogue drawer.</param>
/// <param name="Card">The waiting patron's library card, or null when the counter is empty.</param>
/// <param name="Book">The open book, or null when none is open.</param>
/// <param name="NotebookIndex">The notebook's pages.</param>
/// <param name="Notebook">The notebook page showing, or null before anyone has been met.</param>
/// <param name="Document">The document open on the desk.</param>
/// <param name="Shelved">Returned books already put back on the shelf today, by loan.</param>
/// <param name="LookingUp">Whether the camera is on the up view.</param>
/// <param name="SummaryShowing">Whether a day or week summary covers the screen (the HUD already shows tomorrow).</param>
/// <param name="LettersRead">Whether today's letters have been opened.</param>
/// <remarks>The nullable views mean "none": no patron, no open book, no notebook page yet.</remarks>
public sealed record Snapshot(
    HudView Hud,
    VisitView Visit,
    MorningView Morning,
    CatalogueView Catalogue,
    LibraryCardView? Card,
    BookView? Book,
    IReadOnlyList<NotebookEntry> NotebookIndex,
    NotebookView? Notebook,
    DeskDocument Document,
    IReadOnlySet<string> Shelved,
    bool LookingUp,
    bool SummaryShowing,
    bool LettersRead);
