using System.Collections.Generic;

namespace Sts2Viewer.Data;

public sealed class ArchetypeRow
{
    public int Id { get; set; }

    public string CharacterId { get; set; }

    public string Name { get; set; }

    public string Description { get; set; }

    public IReadOnlyList<string> KeyEffectTags { get; set; }

    public ArchetypeRow()
    {
        CharacterId = string.Empty;
        Name = string.Empty;
        Description = string.Empty;
        KeyEffectTags = new List<string>();
    }
}
