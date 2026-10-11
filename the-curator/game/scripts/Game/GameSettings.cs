using Curator.Presentation.Ui;
using Godot;

namespace Curator.Presentation.Game;

/// <summary>The settings (BUILD_BRIEF §7.12): fullscreen, text size and the reading lamp, kept in user://settings.cfg.</summary>
public sealed class GameSettings
{
    private const string FilePath = "user://settings.cfg";
    private const string Section = "settings";

    /// <summary>The text sizes on offer, as scales of the normal size.</summary>
    public static readonly float[] TextScales = [1f, 1.15f, 1.3f];

    /// <summary>Their names, in the same order.</summary>
    public static readonly string[] TextScaleNames = ["Normal", "Large", "Larger"];

    /// <summary>Whether the window fills the screen.</summary>
    public bool Fullscreen { get; set; }

    /// <summary>Which of <see cref="TextScales"/> is chosen.</summary>
    public int TextSize { get; set; }

    /// <summary>Whether documents keep full brightness in every phase.</summary>
    public bool ReadingLamp { get; set; }

    /// <summary>Reads the settings, or the defaults when there are none.</summary>
    /// <returns>The settings.</returns>
    public static GameSettings Load()
    {
        var config = new ConfigFile();
        var settings = new GameSettings();
        if (config.Load(FilePath) == Error.Ok)
        {
            settings.Fullscreen = (bool)config.GetValue(Section, "fullscreen", false);
            settings.TextSize = Math.Clamp((int)config.GetValue(Section, "text_size", 0), 0, TextScales.Length - 1);
            settings.ReadingLamp = (bool)config.GetValue(Section, "reading_lamp", false);
        }

        return settings;
    }

    /// <summary>Writes the settings.</summary>
    public void Save()
    {
        var config = new ConfigFile();
        config.SetValue(Section, "fullscreen", Fullscreen);
        config.SetValue(Section, "text_size", TextSize);
        config.SetValue(Section, "reading_lamp", ReadingLamp);
        var error = config.Save(FilePath);
        if (error != Error.Ok)
        {
            GD.PushError($"Couldn't save {FilePath}: {error}");
        }
    }

    /// <summary>Applies the window mode and text scale.</summary>
    public void Apply()
    {
        UiTheme.TextScale = TextScales[TextSize];
        var mode = Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed;
        if (DisplayServer.GetName() != "headless" && DisplayServer.WindowGetMode() != mode)
        {
            DisplayServer.WindowSetMode(mode);
        }
    }
}
