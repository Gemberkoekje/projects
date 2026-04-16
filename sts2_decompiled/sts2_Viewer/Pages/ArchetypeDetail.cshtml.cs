using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sts2Viewer.Data;

namespace Sts2Viewer.Pages;

public sealed class ArchetypeDetailModel : PageModel
{
    private const int DefaultAffinityThreshold = 7;
    private const int DefaultEntityLimit = 1000;

    private readonly PostgresReadService _service;

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public int MinAffinityScore { get; set; }

    public bool HasConnectionString { get; private set; }

    public ArchetypeRow Archetype { get; private set; }

    public IReadOnlyList<EntityRow> SynergyEntities { get; private set; }

    public ArchetypeDetailModel(PostgresReadService service)
    {
        _service = service;
        MinAffinityScore = DefaultAffinityThreshold;
        Archetype = new ArchetypeRow();
        SynergyEntities = new List<EntityRow>();
    }

    public void OnGet()
    {
        HasConnectionString = _service.HasConnectionString();
        if (!HasConnectionString)
        {
            return;
        }

        if (Id <= 0)
        {
            return;
        }

        if (MinAffinityScore <= 0)
        {
            MinAffinityScore = DefaultAffinityThreshold;
        }

        var allArchetypes = _service.GetArchetypes(string.Empty);
        foreach (var archetype in allArchetypes)
        {
            if (archetype.Id == Id)
            {
                Archetype = archetype;
                break;
            }
        }

        if (Archetype.Id == 0)
        {
            return;
        }

        SynergyEntities = _service.GetEntitiesSynergizingWithArchetype(Id, MinAffinityScore, DefaultEntityLimit);
    }

    public string TypeBadgeClass(string entityType)
    {
        return entityType switch
        {
            "card" => "type-badge type-card",
            "relic" => "type-badge type-relic",
            "potion" => "type-badge type-potion",
            "event_option" => "type-badge type-event",
            _ => "type-badge"
        };
    }

    public string RatingFillClass(int rating, bool isAnti)
    {
        if (isAnti)
        {
            return rating switch
            {
                <= 3 => "rating-fill rating-anti-low",
                <= 6 => "rating-fill rating-medium",
                _ => "rating-fill rating-anti-high"
            };
        }

        return rating switch
        {
            <= 3 => "rating-fill rating-low",
            <= 6 => "rating-fill rating-medium",
            _ => "rating-fill rating-high"
        };
    }
}
