namespace ByGalacticAccord.Presentation.Views;

/// <summary>
/// What a value's colour encodes (design §11.1: colour only ever encodes data). The client maps each tone
/// to a colour token; nothing is coloured for decoration.
/// </summary>
public enum Tone
{
    Plain,
    Muted,
    Gain,
    Loss,
    Warn,
    Yours,
}
