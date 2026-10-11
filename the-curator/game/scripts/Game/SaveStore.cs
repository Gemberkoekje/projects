using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Curator.Core.Content;
using Curator.Core.Game;
using Godot;

namespace Curator.Presentation.Game;

/// <summary>
/// The autosave (BUILD_BRIEF §7.12): user://save.json, written each evening. It holds the schema
/// version, seed, content version, the events and the curator's notes, so Continue resumes the next
/// morning and quitting mid-day loses that day.
/// </summary>
public static class SaveStore
{
    /// <summary>Where the save lives; the click test points it at a file of its own.</summary>
    public static string SavePath { get; set; } = "user://save.json";

    private static string TempPath => SavePath + ".tmp";

    /// <summary>Whether a save exists.</summary>
    public static bool Exists => Godot.FileAccess.FileExists(SavePath);

    /// <summary>Deletes the save.</summary>
    public static void Delete()
    {
        if (Exists)
        {
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(SavePath));
        }
    }

    /// <summary>Writes the save, replacing the old one only once the new one is complete.</summary>
    /// <param name="session">The game.</param>
    public static void Save(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        using (var file = Godot.FileAccess.Open(TempPath, Godot.FileAccess.ModeFlags.Write))
        {
            if (file is null)
            {
                GD.PushError($"Couldn't write {TempPath}: {Godot.FileAccess.GetOpenError()}");
                return;
            }

            file.StoreString(SaveSerializer.Serialize(session.ToSave()));
        }

        using var directory = DirAccess.Open("user://");
        var error = directory.Rename(TempPath, SavePath);
        if (error != Error.Ok)
        {
            GD.PushError($"Couldn't replace {SavePath}: {error}");
        }
    }

    /// <summary>Restores the saved game.</summary>
    /// <param name="content">The content.</param>
    /// <param name="debugEnabled">Whether Debug… commands are allowed.</param>
    /// <param name="session">The game, when it could be restored.</param>
    /// <param name="problem">Why it couldn't be, or a note that the content changed since; else empty.</param>
    /// <returns>True when the game was restored.</returns>
    public static bool TryRestore(ContentSet content, bool debugEnabled, [NotNullWhen(true)] out GameSession? session, out string problem)
    {
        session = null;
        problem = "";
        if (!Exists)
        {
            problem = "No saved game yet.";
            return false;
        }

        try
        {
            var save = SaveSerializer.Deserialize(Godot.FileAccess.GetFileAsString(SavePath));
            session = GameSession.Restore(content, save, debugEnabled);
            problem = session.ContentChanged ? "The content has changed since this game was saved; it still loads." : "";
            return true;
        }
        catch (Exception ex) when (ex is JsonException or SaveIncompatibleException or NotSupportedException)
        {
            problem = $"The saved game can't be continued: {ex.Message}";
            return false;
        }
    }
}
