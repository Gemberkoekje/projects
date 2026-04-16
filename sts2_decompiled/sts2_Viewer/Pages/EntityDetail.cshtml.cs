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

    public IReadOnlyList<EntityArchetypeAffinityRow> ArchetypeAffinities { get; private set; }

    public IReadOnlyList<SynergyClusterRow> SynergyClusters { get; private set; }

    public string QuickVerdict { get; private set; }

    public string CharacterDisplayName { get; private set; }

    public bool IsFound => Entity.EntityId.Length > 0;

    public EntityDetailModel(PostgresReadService service)
    {
        _service = service;
        EntityType = string.Empty;
        EntityId = string.Empty;
        Entity = new EntityRow();
        ArchetypeAffinities = new List<EntityArchetypeAffinityRow>();
        SynergyClusters = new List<SynergyClusterRow>();
        QuickVerdict = string.Empty;
        CharacterDisplayName = string.Empty;
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

        CharacterDisplayName = ResolveCharacterDisplayName(Entity.CharacterId);
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

    private string ResolveCharacterDisplayName(string characterId)
    {
        if (string.IsNullOrWhiteSpace(characterId))
        {
            return string.Empty;
        }

        CharacterRow match = _service.GetCharacters()
            .FirstOrDefault(c => string.Equals(c.Id, characterId, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            return characterId;
        }

        return match.Title;
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
