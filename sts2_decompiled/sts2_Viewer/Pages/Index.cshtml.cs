using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sts2Viewer.Data;

namespace Sts2Viewer.Pages;

public sealed class IndexModel : PageModel
{
    private const int DefaultEntityLimit = 3000;
    private const int MaxEntityLimit = 10000;

    private readonly PostgresReadService _service;

    [BindProperty(SupportsGet = true)]
    public string Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string EntityType { get; set; }

    [BindProperty(SupportsGet = true)]
    public string CharacterId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Tag { get; set; }

    [BindProperty(SupportsGet = true)]
    public string SortBy { get; set; }

    [BindProperty(SupportsGet = true)]
    public string SortDirection { get; set; }

    [BindProperty(SupportsGet = true)]
    public int MinSynergy { get; set; }

    [BindProperty(SupportsGet = true)]
    public int MinFlexibility { get; set; }

    [BindProperty(SupportsGet = true)]
    public int MaxAntiSynergy { get; set; }

    [BindProperty(SupportsGet = true)]
    public int EntityLimit { get; set; }

    public bool HasConnectionString { get; private set; }

    public IReadOnlyList<string> AvailableTags { get; private set; }

    public IReadOnlyList<CharacterRow> AvailableCharacters { get; private set; }

    public IReadOnlyList<EntityWithArchetypeRow> Entities { get; private set; }

    public IReadOnlyList<SynergyClusterRow> SynergyClusters { get; private set; }

    public PipelineDiagnosticsRow PipelineDiagnostics { get; private set; }

    public IndexModel(PostgresReadService service)
    {
        _service = service;
        Search = string.Empty;
        EntityType = string.Empty;
        CharacterId = string.Empty;
        Tag = string.Empty;
        SortBy = "synergy";
        SortDirection = "desc";
        MinSynergy = 1;
        MinFlexibility = 1;
        MaxAntiSynergy = 10;
        EntityLimit = DefaultEntityLimit;
        AvailableTags = new List<string>();
        AvailableCharacters = new List<CharacterRow>();
        Entities = new List<EntityWithArchetypeRow>();
        SynergyClusters = new List<SynergyClusterRow>();
        PipelineDiagnostics = new PipelineDiagnosticsRow();
    }

    public void OnGet()
    {
        HasConnectionString = _service.HasConnectionString();
        if (!HasConnectionString)
        {
            return;
        }

        Search = (Search ?? string.Empty).Trim();
        EntityType = (EntityType ?? string.Empty).Trim().ToLowerInvariant();
        CharacterId = (CharacterId ?? string.Empty).Trim().ToLowerInvariant();
        Tag = (Tag ?? string.Empty).Trim();
        SortBy = NormalizeSortBy(SortBy);
        SortDirection = NormalizeSortDirection(SortDirection);

        EntityQueryRequest query = new EntityQueryRequest
        {
            Search = Search,
            EntityType = EntityType,
            CharacterId = CharacterId,
            Tag = Tag,
            SortBy = SortBy,
            SortDirection = SortDirection,
            MinSynergy = MinSynergy,
            MinFlexibility = MinFlexibility,
            MaxAntiSynergy = MaxAntiSynergy
        };

        AvailableTags = _service.GetTags(300);
        AvailableCharacters = _service.GetCharacters();
        int entityLimit = EntityLimit <= 0 ? DefaultEntityLimit : Math.Min(EntityLimit, MaxEntityLimit);
        EntityLimit = entityLimit;
        IReadOnlyList<EntityRow> entityRows = _service.GetEntities(query, entityLimit);
        
        List<EntityWithArchetypeRow> entitiesWithArchetypes = new List<EntityWithArchetypeRow>();
        foreach (var entity in entityRows)
        {
            var archetype = _service.GetHighestSynergyArchetype(entity.EntityType, entity.EntityId);
            entitiesWithArchetypes.Add(new EntityWithArchetypeRow
            {
                Entity = entity,
                HighestSynergyArchetype = archetype
            });
        }
        Entities = entitiesWithArchetypes;
        
        SynergyClusters = _service.GetSynergyClusters(100);
        PipelineDiagnostics = _service.GetPipelineDiagnostics();
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

    public string SortIndicator(string sortBy)
    {
        if (!string.Equals(SortBy, sortBy, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return string.Equals(SortDirection, "asc", StringComparison.OrdinalIgnoreCase) ? "↑" : "↓";
    }

    public string NextSortDirection(string sortBy)
    {
        if (string.Equals(SortBy, sortBy, StringComparison.OrdinalIgnoreCase)
            && string.Equals(SortDirection, "desc", StringComparison.OrdinalIgnoreCase))
        {
            return "asc";
        }

        return "desc";
    }

    private static string NormalizeSortBy(string sortBy)
    {
        string normalized = (sortBy ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "flexibility" => "flexibility",
            "anti" => "anti",
            "name" => "name",
            _ => "synergy"
        };
    }

    private static string NormalizeSortDirection(string sortDirection)
    {
        return string.Equals((sortDirection ?? string.Empty).Trim(), "asc", StringComparison.OrdinalIgnoreCase)
            ? "asc"
            : "desc";
    }
}
