using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sts2Viewer.Data;

namespace Sts2Viewer.Pages;

public sealed class ArchetypesModel : PageModel
{
    private readonly PostgresReadService _service;

    public bool HasConnectionString { get; private set; }

    public IReadOnlyList<CharacterRow> AvailableCharacters { get; private set; }

    public IReadOnlyList<ArchetypeRow> Archetypes { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string CharacterId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string SortBy { get; set; }

    [BindProperty(SupportsGet = true)]
    public string SortDirection { get; set; }

    public ArchetypesModel(PostgresReadService service)
    {
        _service = service;
        AvailableCharacters = new List<CharacterRow>();
        Archetypes = new List<ArchetypeRow>();
        CharacterId = string.Empty;
        SortBy = "title";
        SortDirection = "asc";
    }

    public void OnGet(string characterId = "")
    {
        HasConnectionString = _service.HasConnectionString();
        if (!HasConnectionString)
        {
            return;
        }

        CharacterId = (characterId ?? string.Empty).Trim().ToLowerInvariant();
        SortBy = NormalizeSortBy(SortBy);
        SortDirection = NormalizeSortDirection(SortDirection);

        AvailableCharacters = _service.GetCharacters();
        Archetypes = ApplySort(_service.GetArchetypes(CharacterId), SortBy, SortDirection);
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

    private static IReadOnlyList<ArchetypeRow> ApplySort(IReadOnlyList<ArchetypeRow> rows, string sortBy, string sortDirection)
    {
        List<ArchetypeRow> sorted = new List<ArchetypeRow>(rows);

        sorted.Sort((left, right) =>
        {
            int titleComparison = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
            if (string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase))
            {
                titleComparison = -titleComparison;
            }

            if (titleComparison != 0)
            {
                return titleComparison;
            }

            return left.Id.CompareTo(right.Id);
        });

        return sorted;
    }

    private static string NormalizeSortBy(string sortBy)
    {
        string normalized = (sortBy ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
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
