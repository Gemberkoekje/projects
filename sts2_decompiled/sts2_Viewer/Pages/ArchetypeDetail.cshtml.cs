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

    [BindProperty(SupportsGet = true)]
    public string SortBy { get; set; }

    [BindProperty(SupportsGet = true)]
    public string SortDirection { get; set; }

    public bool HasConnectionString { get; private set; }

    public ArchetypeRow Archetype { get; private set; }

    public IReadOnlyList<EntityRow> SynergyEntities { get; private set; }

    public ArchetypeDetailModel(PostgresReadService service)
    {
        _service = service;
        MinAffinityScore = DefaultAffinityThreshold;
        SortBy = "title";
        SortDirection = "asc";
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

        SortBy = NormalizeSortBy(SortBy);
        SortDirection = NormalizeSortDirection(SortDirection);

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

        SynergyEntities = ApplySort(_service.GetEntitiesSynergizingWithArchetype(Id, MinAffinityScore, DefaultEntityLimit), SortBy, SortDirection);
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

    private static IReadOnlyList<EntityRow> ApplySort(IReadOnlyList<EntityRow> rows, string sortBy, string sortDirection)
    {
        List<EntityRow> sorted = new List<EntityRow>(rows);

        sorted.Sort((left, right) =>
        {
            int comparison = sortBy switch
            {
                "affinity" => left.AffinityScore.CompareTo(right.AffinityScore),
                _ => string.Compare(left.Title, right.Title, StringComparison.OrdinalIgnoreCase)
            };

            if (string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase))
            {
                comparison = -comparison;
            }

            if (comparison != 0)
            {
                return comparison;
            }

            int byType = string.Compare(left.EntityType, right.EntityType, StringComparison.OrdinalIgnoreCase);
            if (byType != 0)
            {
                return byType;
            }

            return string.Compare(left.EntityId, right.EntityId, StringComparison.OrdinalIgnoreCase);
        });

        return sorted;
    }

    private static string NormalizeSortBy(string sortBy)
    {
        string normalized = (sortBy ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "affinity" => "affinity",
            "title" => "title",
            "name" => "title",
            _ => "title"
        };
    }

    private static string NormalizeSortDirection(string sortDirection)
    {
        return string.Equals((sortDirection ?? string.Empty).Trim(), "desc", StringComparison.OrdinalIgnoreCase)
            ? "desc"
            : "asc";
    }
}
