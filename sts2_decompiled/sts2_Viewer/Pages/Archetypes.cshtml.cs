using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sts2Viewer.Data;

namespace Sts2Viewer.Pages;

public sealed class ArchetypesModel : PageModel
{
    private readonly PostgresReadService _service;

    public bool HasConnectionString { get; private set; }

    public IReadOnlyList<CharacterRow> AvailableCharacters { get; private set; }

    public IReadOnlyList<ArchetypeRow> Archetypes { get; private set; }

    public string CharacterId { get; private set; }

    public ArchetypesModel(PostgresReadService service)
    {
        _service = service;
        AvailableCharacters = new List<CharacterRow>();
        Archetypes = new List<ArchetypeRow>();
        CharacterId = string.Empty;
    }

    public void OnGet(string characterId = "")
    {
        HasConnectionString = _service.HasConnectionString();
        if (!HasConnectionString)
        {
            return;
        }

        CharacterId = (characterId ?? string.Empty).Trim().ToLowerInvariant();
        AvailableCharacters = _service.GetCharacters();
        Archetypes = _service.GetArchetypes(CharacterId);
    }
}
