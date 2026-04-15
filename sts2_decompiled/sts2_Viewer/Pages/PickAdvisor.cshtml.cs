using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Sts2Viewer.Data;
using Sts2Viewer.Llm;

namespace Sts2Viewer.Pages;

public sealed class PickAdvisorModel : PageModel
{
    private readonly PostgresReadService _readService;
    private readonly PostgresWriteService _writeService;
    private readonly PickExplanationService _explanationService;

    [BindProperty(SupportsGet = true)]
    public string CharacterId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int ArchetypeId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string DeckCsv { get; set; }

    [BindProperty(SupportsGet = true)]
    public string RelicsCsv { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Option1Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Option2Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Option3Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool IncludeColorless { get; set; }

    [BindProperty]
    public string RunStateName { get; set; }

    [BindProperty(SupportsGet = true)]
    public int SelectedRunStateId { get; set; }

    public bool HasConnectionString { get; private set; }

    public IReadOnlyList<CharacterRow> Characters { get; private set; }

    public IReadOnlyList<ArchetypeRow> Archetypes { get; private set; }

    public IReadOnlyList<EntityOptionRow> CardOptions { get; private set; }

    public IReadOnlyList<EntityOptionRow> RelicOptions { get; private set; }

    public IReadOnlyList<RunStateRow> RunStates { get; private set; }

    public IReadOnlyList<PickScoreRow> Scores { get; private set; }

    public IReadOnlyList<PickAdviceRow> RecentAdvice { get; private set; }

    public string LlmNarrative { get; private set; }

    public PickAdvisorModel(PostgresReadService readService, PostgresWriteService writeService, PickExplanationService explanationService)
    {
        _readService = readService;
        _writeService = writeService;
        _explanationService = explanationService;

        CharacterId = string.Empty;
        DeckCsv = string.Empty;
        RelicsCsv = string.Empty;
        Option1Id = string.Empty;
        Option2Id = string.Empty;
        Option3Id = string.Empty;
        IncludeColorless = true;
        RunStateName = string.Empty;
        Characters = new List<CharacterRow>();
        Archetypes = new List<ArchetypeRow>();
        CardOptions = new List<EntityOptionRow>();
        RelicOptions = new List<EntityOptionRow>();
        RunStates = new List<RunStateRow>();
        Scores = new List<PickScoreRow>();
        RecentAdvice = new List<PickAdviceRow>();
        LlmNarrative = string.Empty;
    }

    public void OnGet()
    {
        Initialize();
        TryScore();
    }

    public void OnPostAdvise()
    {
        Initialize();
        TryScore();
    }

    public JsonResult OnPostAdviseJson()
    {
        Initialize();
        TryScore();

        return new JsonResult(new
        {
            ok = true,
            scores = Scores.Select(s => new
            {
                rank = s.Rank,
                entityType = s.EntityType,
                entityId = s.EntityId,
                compositeScore = s.CompositeScore,
                deckFitScore = s.EdgeOverlapScore,
                archetypeAffinityScore = s.ArchetypeFitScore,
                antiSynergyPenalty = s.AntiSynergyPenalty,
                flexibilityScore = s.FlexibilityScore,
                synergyDrivers = s.SynergyDrivers
            }).ToArray(),
            recentAdvice = RecentAdvice.Select(r => new
            {
                createdAt = r.CreatedAt,
                optionEntityType = r.OptionEntityType,
                optionEntityId = r.OptionEntityId,
                rank = r.Rank,
                compositeScore = r.CompositeScore,
                archetypeFitScore = r.ArchetypeFitScore,
                edgeOverlapScore = r.EdgeOverlapScore,
                antiSynergyPenalty = r.AntiSynergyPenalty
            }).ToArray()
        });
    }

    public async Task OnPostExplainAsync()
    {
        Initialize();
        TryScore();

        if (Scores.Count > 0)
        {
            LlmNarrative = await _explanationService.ExplainAsync(Scores, CancellationToken.None);
        }
    }

    public IActionResult OnPostSaveRunState()
    {
        Initialize();

        if (string.IsNullOrWhiteSpace(RunStateName))
        {
            ModelState.AddModelError(string.Empty, "Run state name is required.");
            TryScore();
            return Page();
        }

        string[] deckIds = ParseIds(DeckCsv);
        string[] relicIds = ParseIds(RelicsCsv);
        _writeService.SaveRunState(RunStateName.Trim(), CharacterId.Trim(), ArchetypeId, deckIds, relicIds);

        return RedirectToPage(BuildRouteValues(deckIds, relicIds));
    }

    public IActionResult OnPostLoadRunState()
    {
        Initialize();

        if (SelectedRunStateId <= 0)
        {
            return Page();
        }

        RunStateRow runState = RunStates.FirstOrDefault(r => r.Id == SelectedRunStateId) ?? new RunStateRow();
        if (runState.Id <= 0)
        {
            return Page();
        }

        CharacterId = runState.CharacterId;
        ArchetypeId = runState.ArchetypeId;
        DeckCsv = string.Join(',', runState.CardIds);
        RelicsCsv = string.Join(',', runState.RelicIds);

        return RedirectToPage(BuildRouteValues(runState.CardIds.ToArray(), runState.RelicIds.ToArray()));
    }

    public IActionResult OnPostDeleteRunState()
    {
        Initialize();

        if (SelectedRunStateId > 0)
        {
            _writeService.DeleteRunState(SelectedRunStateId);
        }

        string[] deckIds = ParseIds(DeckCsv);
        string[] relicIds = ParseIds(RelicsCsv);
        return RedirectToPage(BuildRouteValues(deckIds, relicIds));
    }

    public IReadOnlyList<SelectListItem> CharacterItems()
    {
        return Characters
            .Select(c => new SelectListItem(c.Title + " (" + c.Id + ")", c.Id, string.Equals(c.Id, CharacterId, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    public IReadOnlyList<SelectListItem> ArchetypeItems()
    {
        return Archetypes
            .Select(a => new SelectListItem(a.Name, a.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), a.Id == ArchetypeId))
            .ToList();
    }

    private void Initialize()
    {
        HasConnectionString = _readService.HasConnectionString() && _writeService.HasConnectionString();
        if (!HasConnectionString)
        {
            return;
        }

        Characters = _readService.GetCharacters();
        if (string.IsNullOrWhiteSpace(CharacterId) && Characters.Count > 0)
        {
            CharacterId = Characters[0].Id;
        }

        Archetypes = _readService.GetArchetypes(CharacterId.Trim());
        CardOptions = _readService.GetCardOptions(CharacterId.Trim(), IncludeColorless);
        RelicOptions = _readService.GetRelicOptions(CharacterId.Trim());
        RunStates = _writeService.GetRunStates();

        if (ArchetypeId == 0 && Archetypes.Count > 0)
        {
            ArchetypeId = Archetypes[0].Id;
        }
    }

    private void TryScore()
    {
        if (!HasConnectionString)
        {
            return;
        }

        string[] deckIds = ParseIds(DeckCsv);
        string[] relicIds = ParseIds(RelicsCsv);

        List<string> options = new List<string>();
        AddOption(options, Option1Id);
        AddOption(options, Option2Id);
        AddOption(options, Option3Id);

        if (options.Count == 0)
        {
            Scores = new List<PickScoreRow>();
            RecentAdvice = new List<PickAdviceRow>();
            return;
        }

        Scores = _readService.ScorePickOptions(ArchetypeId, deckIds, relicIds, options.ToArray());
        _writeService.SavePickAdvice(SelectedRunStateId, Scores);
        RecentAdvice = _writeService.GetRecentPickAdvice(options.ToArray(), 20);
    }

    private object BuildRouteValues(string[] deckIds, string[] relicIds)
    {
        return new
        {
            CharacterId,
            ArchetypeId,
            DeckCsv = string.Join(',', deckIds),
            RelicsCsv = string.Join(',', relicIds),
            Option1Id,
            Option2Id,
            Option3Id,
            IncludeColorless,
            SelectedRunStateId
        };
    }

    private static void AddOption(List<string> options, string value)
    {
        string id = value.Trim();
        if (id.Length == 0)
        {
            return;
        }

        if (!options.Contains(id, StringComparer.OrdinalIgnoreCase))
        {
            options.Add(id);
        }
    }

    private static string[] ParseIds(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return Array.Empty<string>();
        }

        return csv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
