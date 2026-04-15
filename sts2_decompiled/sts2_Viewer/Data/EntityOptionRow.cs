using System.Collections.Generic;

namespace Sts2Viewer.Data;

public sealed class EntityOptionRow
{
    public string EntityType { get; set; }

    public string EntityId { get; set; }

    public string CharacterId { get; set; }

    public string Title { get; set; }

    public IReadOnlyList<string> Tags { get; set; }

    public EntityOptionRow()
    {
        EntityType = string.Empty;
        EntityId = string.Empty;
        CharacterId = string.Empty;
        Title = string.Empty;
        Tags = new List<string>();
    }
}
