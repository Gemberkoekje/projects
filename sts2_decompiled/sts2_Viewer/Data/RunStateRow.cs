using System;
using System.Collections.Generic;

namespace Sts2Viewer.Data;

public sealed class RunStateRow
{
    public int Id { get; set; }

    public string Name { get; set; }

    public string CharacterId { get; set; }

    public int ArchetypeId { get; set; }

    public string ArchetypeName { get; set; }

    public IReadOnlyList<string> CardIds { get; set; }

    public IReadOnlyList<string> RelicIds { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public RunStateRow()
    {
        Name = string.Empty;
        CharacterId = string.Empty;
        ArchetypeName = string.Empty;
        CardIds = new List<string>();
        RelicIds = new List<string>();
        CreatedAt = DateTimeOffset.MinValue;
    }
}
