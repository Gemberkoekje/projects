using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sts2Viewer.Data;

namespace Sts2Viewer.Pages;

public sealed class EntityDetailModel : PageModel
{
    private readonly PostgresReadService _service;

    [BindProperty(SupportsGet = true, Name = "type")]
    public string EntityType { get; set; }

    [BindProperty(SupportsGet = true, Name = "id")]
    public string EntityId { get; set; }

    public bool HasConnectionString { get; private set; }

    public EntityRow Entity { get; private set; }

    public IReadOnlyList<EntityDetailEdgeRow> SynergyEdges { get; private set; }

    public IReadOnlyList<EntityArchetypeAffinityRow> ArchetypeAffinities { get; private set; }

    public IReadOnlyList<SynergyClusterRow> SynergyClusters { get; private set; }

    public string QuickVerdict { get; private set; }

    public bool IsFound => Entity.EntityId.Length > 0;

    public EntityDetailModel(PostgresReadService service)
    {
        _service = service;
        EntityType = string.Empty;
        EntityId = string.Empty;
        Entity = new EntityRow();
        SynergyEdges = new List<EntityDetailEdgeRow>();
        ArchetypeAffinities = new List<EntityArchetypeAffinityRow>();
        SynergyClusters = new List<SynergyClusterRow>();
        QuickVerdict = string.Empty;
    }

    public void OnGet()
    {
        HasConnectionString = _service.HasConnectionString();
        if (!HasConnectionString)
        {
            return;
        }

        string normalizedType = (EntityType ?? string.Empty).Trim().ToLowerInvariant();
        string normalizedId = (EntityId ?? string.Empty).Trim();

        EntityType = normalizedType;
        EntityId = normalizedId;

        if (normalizedType.Length == 0 || normalizedId.Length == 0)
        {
            return;
        }

        EntityQueryRequest query = new EntityQueryRequest
        {
            EntityType = normalizedType,
            Search = normalizedId,
            MinSynergy = 0,
            MinFlexibility = 0,
            MaxAntiSynergy = 10
        };

        Entity = _service.GetEntities(query, 50)
            .FirstOrDefault(e => string.Equals(e.EntityType, normalizedType, StringComparison.OrdinalIgnoreCase)
                              && string.Equals(e.EntityId, normalizedId, StringComparison.OrdinalIgnoreCase))
            ?? new EntityRow();

        if (!IsFound)
        {
            return;
        }

        List<EntityDetailEdgeRow> edges = new List<EntityDetailEdgeRow>();
        foreach (EntitySynergyEdgeRow edge in _service.GetEntitySynergyEdges(new[] { normalizedId }))
        {
            bool matchesA = string.Equals(edge.EntityAId, normalizedId, StringComparison.OrdinalIgnoreCase)
                         && string.Equals(edge.EntityAType, normalizedType, StringComparison.OrdinalIgnoreCase);
            bool matchesB = string.Equals(edge.EntityBId, normalizedId, StringComparison.OrdinalIgnoreCase)
                         && string.Equals(edge.EntityBType, normalizedType, StringComparison.OrdinalIgnoreCase);

            if (!matchesA && !matchesB)
            {
                continue;
            }

            edges.Add(new EntityDetailEdgeRow
            {
                PartnerType = matchesA ? edge.EntityBType : edge.EntityAType,
                PartnerId = matchesA ? edge.EntityBId : edge.EntityAId,
                Strength = edge.SynergyStrength,
                IsAntiSynergy = edge.IsAntiSynergy,
                SharedTags = edge.SharedTags,
                Explanation = edge.Explanation
            });
        }

        SynergyEdges = edges
            .OrderByDescending(e => e.Strength)
            .ThenBy(e => e.PartnerType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.PartnerId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ArchetypeAffinities = _service.GetEntityArchetypeAffinities(normalizedType, normalizedId);
        SynergyClusters = _service.GetSynergyClustersForEntity(normalizedType, normalizedId);
        QuickVerdict = BuildVerdict(Entity);
    }

    public string TypeBadgeClass(string entityType)
    {
        return entityType switch
        {
            "card" => "badge badge-card",
            "relic" => "badge badge-relic",
            "potion" => "badge badge-potion",
            "event_option" => "badge badge-event",
            _ => "badge"
        };
    }

    private static string BuildVerdict(EntityRow entity)
    {
        if (entity.FlexibilityRating >= 7)
        {
            return "Flexible generalist — fits most decks.";
        }

        if (entity.SynergyRating >= 8 && entity.FlexibilityRating <= 4)
        {
            return "Niche specialist — powerful in the right shell.";
        }

        if (entity.AntiSynergyRating >= 7)
        {
            return "Caution — frequent conflicts with common plans.";
        }

        return "Solid role-player.";
    }
}

public sealed class EntityDetailEdgeRow
{
    public string PartnerType { get; set; }

    public string PartnerId { get; set; }

    public int Strength { get; set; }

    public bool IsAntiSynergy { get; set; }

    public IReadOnlyList<string> SharedTags { get; set; }

    public string Explanation { get; set; }

    public EntityDetailEdgeRow()
    {
        PartnerType = string.Empty;
        PartnerId = string.Empty;
        SharedTags = new List<string>();
        Explanation = string.Empty;
    }
}
