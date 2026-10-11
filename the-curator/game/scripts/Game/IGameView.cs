namespace Curator.Presentation.Game;

/// <summary>A view the controller pushes fresh projections to after every command.</summary>
public interface IGameView
{
    /// <summary>Draws the snapshot.</summary>
    /// <param name="snapshot">What to draw.</param>
    void Render(Snapshot snapshot);
}
