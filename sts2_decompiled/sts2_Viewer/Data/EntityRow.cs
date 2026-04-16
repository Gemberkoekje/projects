using System;
using System.Collections.Generic;

namespace Sts2Viewer.Data;

public sealed class EntityRow
{
    public string EntityType { get; set; }

    public string EntityId { get; set; }

    public string CharacterId { get; set; }

    public string Title { get; set; }

    public string Description { get; set; }

    public IReadOnlyList<string> Tags { get; set; }

    public int EnergyCost { get; set; }

    public IReadOnlyList<string> ResourcesGenerated { get; set; }

    public IReadOnlyList<string> ResourcesConsumed { get; set; }

    public int AffinityScore { get; set; }

    public int SynergyRating { get; set; }

    public int FlexibilityRating { get; set; }

    public int AntiSynergyRating { get; set; }

    public EntityRow()
    {
        EntityType = string.Empty;
        EntityId = string.Empty;
        CharacterId = string.Empty;
        Title = string.Empty;
        Description = string.Empty;
        Tags = new List<string>();
        EnergyCost = int.MinValue;
        ResourcesGenerated = Array.Empty<string>();
        ResourcesConsumed = Array.Empty<string>();
    }
}