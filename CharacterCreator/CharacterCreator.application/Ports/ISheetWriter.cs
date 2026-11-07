using CharacterCreator.application.Sheets;

namespace CharacterCreator.application.Ports;

/// <summary>Lays a character sheet out as a PDF (DESIGN.md §5, Print), in Guild Hall's print look.</summary>
public interface ISheetWriter
{
    /// <summary>Writes the sheet.</summary>
    /// <param name="sheet">The sheet, already worded.</param>
    /// <returns>The PDF's bytes.</returns>
    ReadOnlyMemory<byte> Write(CharacterSheet sheet);
}
