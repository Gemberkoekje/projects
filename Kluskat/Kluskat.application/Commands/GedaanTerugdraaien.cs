namespace Kluskat.application.Commands;

/// <summary>"Oeps, niet gedaan" on the win screen.</summary>
public sealed record GedaanTerugdraaien : ICommand
{
    /// <summary>The klus whose "Gedaan" was a mis-tap.</summary>
    public KlusId KlusId { get; init; }
}
