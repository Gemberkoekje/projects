namespace TheCuratool.Domain;

/// <summary>
/// An imported Blood on the Clocktower script containing the set of characters available for a game.
/// </summary>
/// <param name="Name">The display name of the script.</param>
/// <param name="Author">The author of the script, if present in the JSON metadata.</param>
/// <param name="Characters">The ordered list of character definitions included in this script.</param>
public sealed record Script(
    string Name,
    string Author,
    IReadOnlyList<CharacterDefinition> Characters)
{
    public IReadOnlyList<CharacterDefinition> EffectiveCharacters => Characters;

    public IReadOnlyList<CharacterDefinition> OutOfScriptCharacters => Characters
        .Where(character => character.IsOutOfScript)
        .ToList()
        .AsReadOnly();
}
