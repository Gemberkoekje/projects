namespace Curator.Presentation.Game;

/// <summary>The presentation's own state, carried over when the views are rebuilt for a new text size.</summary>
/// <param name="LookingUp">Where the camera looks.</param>
/// <param name="Document">The document open on the desk.</param>
/// <param name="OpenBookId">The open book, or empty.</param>
/// <param name="NotebookPatronId">The notebook page showing, or empty.</param>
/// <param name="Shelved">Returned books already shelved, by loan.</param>
/// <param name="TrayDay">The day the tray belongs to.</param>
/// <param name="LettersReadDay">The last day whose letters were opened.</param>
/// <param name="NotebookIngredients">Whether the notebook is turned to the ingredient log.</param>
public sealed record DeskState(
    bool LookingUp,
    DeskDocument Document,
    string OpenBookId,
    string NotebookPatronId,
    IReadOnlyList<string> Shelved,
    int TrayDay,
    int LettersReadDay,
    bool NotebookIngredients);
