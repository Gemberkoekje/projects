using System.Threading;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using TheCuratool.Application;
using TheCuratool.Application.Abstractions.Repositories;
using TheCuratool.Domain;

namespace TheCuratool.Web.Api;

public static class CuratoolApi
{
    public static IEndpointRouteBuilder MapCuratoolApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api").WithTags("Curatool API");

        group.MapPost("/scripts", UploadScriptAsync)
            .WithName("UploadScript")
            .WithSummary("Upload a script")
            .WithDescription("Parses and persists a Blood on the Clocktower script JSON. Returns the stored script metadata including any parse warnings.");

        group.MapPost("/sessions", CreateSessionAsync)
            .WithName("CreateSession")
            .WithSummary("Create a draft session")
            .WithDescription("Creates a new Curator draft session for a previously uploaded script. Players are randomly assigned a draft order.");

        group.MapGet("/sessions/{id:guid}", GetSessionAsync)
            .WithName("GetSession")
            .WithSummary("Get a draft session")
            .WithDescription("Returns the full state of a draft session including all player slots, current makeup summary, and session status.");

        group.MapGet("/sessions/{id:guid}/suggestions", GetSuggestionsAsync)
            .WithName("GetSuggestions")
            .WithSummary("Get character suggestions for the next player")
            .WithDescription("Returns up to 3 suggested character IDs for the next unchosen player in draft order, respecting setup rules and availability constraints.");

        group.MapGet("/sessions/{id:guid}/remaining", GetRemainingAsync)
            .WithName("GetRemainingCharacters")
            .WithSummary("Get remaining valid characters")
            .WithDescription("Returns all characters that are still valid for selection in the current draft state, filtered by setup feasibility and availability constraints.");

        group.MapPost("/sessions/{id:guid}/curated-offer", CreateCuratedOfferAsync)
            .WithName("CreateCuratedOffer")
            .WithSummary("Create a curated offer for a player")
            .WithDescription("Locks a set of up to 3 character IDs as the curated offer for the specified player slot. The offer is idempotent — re-posting with the same IDs is safe.");

        group.MapPost("/sessions/{id:guid}/choices", RecordChoiceAsync)
            .WithName("RecordChoice")
            .WithSummary("Record a player's character choice")
            .WithDescription("Records the character chosen by a player from a previously created curated offer. Optional hidden flags (Drunk, Lunatic) may be specified by the Storyteller.");

        return endpoints;
    }

    private static async Task<Results<Created<ScriptApiResponse>, ValidationProblem>> UploadScriptAsync(
        [FromBody] ScriptUploadRequest request,
        ScriptParser scriptParser,
        CharacterDatabase characterDatabase,
        IScriptRepository scriptRepository,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RawJson))
        {
            return TypedResults.ValidationProblem(CreateValidationErrors(nameof(request.RawJson), "RawJson is required."));
        }

        var parseResult = scriptParser.Parse(request.RawJson, characterDatabase);
        if (!parseResult.IsSuccess)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.RawJson)] = parseResult.Errors.ToArray(),
            });
        }

        var storedScript = await scriptRepository.AddAsync(
            parseResult.Script.Name,
            parseResult.Script.Author,
            request.RawJson,
            cancellationToken);

        var response = new ScriptApiResponse(
            storedScript.Id,
            storedScript.Script.Name,
            storedScript.Script.Author,
            storedScript.Script.Characters.Count,
            parseResult.Info,
            parseResult.Warnings);

        return TypedResults.Created($"/api/scripts/{storedScript.Id}", response);
    }

    private static async Task<Results<Created<SessionApiResponse>, ValidationProblem, NotFound<ProblemDetails>>> CreateSessionAsync(
        [FromBody] CreateSessionRequest request,
        IScriptRepository scriptRepository,
        IGameSessionRepository gameSessionRepository,
        DraftEngine draftEngine,
        CancellationToken cancellationToken)
    {
        if (request.PlayerCount < 5)
        {
            return TypedResults.ValidationProblem(CreateValidationErrors(nameof(request.PlayerCount), "PlayerCount must be at least 5."));
        }

        StoredScript storedScript;
        try
        {
            storedScript = await scriptRepository.GetByIdAsync(request.ScriptId, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return TypedResults.NotFound(CreateNotFoundProblem("Script not found.", $"Script '{request.ScriptId}' was not found."));
        }

        var session = draftEngine.StartSession(
            storedScript.Script,
            request.PlayerCount,
            NormalizeIds(request.ActiveLorics),
            request.UseMarionette,
            request.UseAtheist,
            request.IsLegionGame,
            request.LegionCount);
        await gameSessionRepository.AddAsync(session, storedScript.Id, cancellationToken);
        var persisted = await gameSessionRepository.GetByIdAsync(session.Id, cancellationToken);
        return TypedResults.Created($"/api/sessions/{persisted.Id}", SessionApiMapper.MapSession(persisted, draftEngine.GetMakeupSummary(persisted)));
    }

    private static async Task<Results<Ok<SessionApiResponse>, NotFound<ProblemDetails>>> GetSessionAsync(
        Guid id,
        IGameSessionRepository gameSessionRepository,
        DraftEngine draftEngine,
        CancellationToken cancellationToken)
    {
        var lookup = await TryGetSessionAsync(id, gameSessionRepository, cancellationToken);
        if (!lookup.Found)
        {
            return TypedResults.NotFound(lookup.Problem);
        }

        draftEngine.TrackSession(lookup.Session);
        var summary = draftEngine.GetMakeupSummary(lookup.Session);
        return TypedResults.Ok(SessionApiMapper.MapSession(lookup.Session, summary));
    }

    private static async Task<Results<Ok<IReadOnlyList<CharacterApiResponse>>, NotFound<ProblemDetails>>> GetSuggestionsAsync(
        Guid id,
        IGameSessionRepository gameSessionRepository,
        DraftEngine draftEngine,
        CancellationToken cancellationToken)
    {
        var lookup = await TryGetSessionAsync(id, gameSessionRepository, cancellationToken);
        if (!lookup.Found)
        {
            return TypedResults.NotFound(lookup.Problem);
        }

        draftEngine.TrackSession(lookup.Session);
        IReadOnlyList<CharacterApiResponse> suggestions = draftEngine.SuggestThree(lookup.Session)
            .Select(option =>
            {
                var presentedId = string.IsNullOrWhiteSpace(option.DisguiseCharacterId)
                    ? option.CharacterId
                    : option.DisguiseCharacterId;
                var presented = lookup.Session.Script.Characters.FirstOrDefault(character =>
                        string.Equals(character.Id, presentedId, StringComparison.OrdinalIgnoreCase))
                    ?? new CharacterDefinition(presentedId, presentedId, CharacterType.Unknown, Array.Empty<ISetupRule>(), Array.Empty<IAvailabilityConstraint>(), false, true, false);
                return SessionApiMapper.MapCharacter(presented);
            })
            .ToList();

        return TypedResults.Ok(suggestions);
    }

    private static async Task<Results<Ok<IReadOnlyList<CharacterApiResponse>>, NotFound<ProblemDetails>>> GetRemainingAsync(
        Guid id,
        IGameSessionRepository gameSessionRepository,
        DraftEngine draftEngine,
        CancellationToken cancellationToken)
    {
        var lookup = await TryGetSessionAsync(id, gameSessionRepository, cancellationToken);
        if (!lookup.Found)
        {
            return TypedResults.NotFound(lookup.Problem);
        }

        draftEngine.TrackSession(lookup.Session);
        IReadOnlyList<CharacterApiResponse> remaining = draftEngine.GetRemainingValidCharacters(lookup.Session)
            .Select(SessionApiMapper.MapCharacter)
            .ToList();

        return TypedResults.Ok(remaining);
    }

    private static async Task<Results<Ok<SessionApiResponse>, ValidationProblem, NotFound<ProblemDetails>>> CreateCuratedOfferAsync(
        Guid id,
        [FromBody] CuratedOfferRequest request,
        IGameSessionRepository gameSessionRepository,
        DraftEngine draftEngine,
        CancellationToken cancellationToken)
    {
        if (request.PlayerSlot < 1)
        {
            return TypedResults.ValidationProblem(CreateValidationErrors(nameof(request.PlayerSlot), "PlayerSlot must be greater than zero."));
        }

        var lookup = await TryGetSessionAsync(id, gameSessionRepository, cancellationToken);
        if (!lookup.Found)
        {
            return TypedResults.NotFound(lookup.Problem);
        }

        var offeredOptions = request.OfferedOptions.Count > 0
            ? request.OfferedOptions.Select(MapOfferOption).ToList().AsReadOnly()
            : request.OfferedIds.Select(OfferOption.Normal).ToList().AsReadOnly();

        return await ExecuteMutationAsync(
            lookup.Session,
            gameSessionRepository,
            draftEngine,
            cancellationToken,
            session => draftEngine.CreateCuratedOffer(
                session.Id,
                request.PlayerSlot,
                offeredOptions));
    }

    private static async Task<Results<Ok<SessionApiResponse>, ValidationProblem, NotFound<ProblemDetails>>> RecordChoiceAsync(
        Guid id,
        [FromBody] RecordChoiceRequest request,
        IGameSessionRepository gameSessionRepository,
        DraftEngine draftEngine,
        CancellationToken cancellationToken)
    {
        if (request.PlayerSlot < 1)
        {
            return TypedResults.ValidationProblem(CreateValidationErrors(nameof(request.PlayerSlot), "PlayerSlot must be greater than zero."));
        }

        if (string.IsNullOrWhiteSpace(request.ChosenCharacterId))
        {
            return TypedResults.ValidationProblem(CreateValidationErrors(nameof(request.ChosenCharacterId), "ChosenCharacterId is required."));
        }

        var lookup = await TryGetSessionAsync(id, gameSessionRepository, cancellationToken);
        if (!lookup.Found)
        {
            return TypedResults.NotFound(lookup.Problem);
        }

        if (request.OfferedOptions.Count == 0 && request.OfferedIds.Count == 0)
        {
            return TypedResults.ValidationProblem(CreateValidationErrors(nameof(request.OfferedOptions), "Offered options are required."));
        }

        return await ExecuteMutationAsync(
            lookup.Session,
            gameSessionRepository,
            draftEngine,
            cancellationToken,
            session => request.OfferedOptions.Count > 0
                ? draftEngine.RecordChoice(
                    session.Id,
                    request.PlayerSlot,
                    request.ChosenCharacterId,
                    request.OfferedOptions.Select(MapOfferOption).ToList().AsReadOnly())
                : draftEngine.RecordChoice(
                    session.Id,
                    request.PlayerSlot,
                    request.ChosenCharacterId,
                    request.OfferedIds,
                    new HiddenFlags(request.HiddenFlags.IsDrunk, request.HiddenFlags.IsLunatic)));
    }

    private static async Task<Results<Ok<SessionApiResponse>, ValidationProblem, NotFound<ProblemDetails>>> ExecuteMutationAsync(
        GameSession session,
        IGameSessionRepository gameSessionRepository,
        DraftEngine draftEngine,
        CancellationToken cancellationToken,
        Func<GameSession, GameSession> operation)
    {
        try
        {
            draftEngine.TrackSession(session);
            var updated = operation(session);
            await gameSessionRepository.UpdateAsync(updated, cancellationToken);
            var persisted = await gameSessionRepository.GetByIdAsync(updated.Id, cancellationToken);
            return TypedResults.Ok(SessionApiMapper.MapSession(persisted, draftEngine.GetMakeupSummary(persisted)));
        }
        catch (ArgumentException exception)
        {
            return TypedResults.ValidationProblem(ToErrors(exception));
        }
        catch (InvalidOperationException exception)
        {
            return TypedResults.ValidationProblem(ToErrors(exception));
        }
        catch (KeyNotFoundException exception)
        {
            return TypedResults.ValidationProblem(ToErrors(exception));
        }
    }

    private static async Task<SessionLookupResult> TryGetSessionAsync(
        Guid id,
        IGameSessionRepository gameSessionRepository,
        CancellationToken cancellationToken)
    {
        try
        {
            return new SessionLookupResult(true, await gameSessionRepository.GetByIdAsync(id, cancellationToken), new ProblemDetails());
        }
        catch (InvalidOperationException)
        {
            return new SessionLookupResult(false, EmptySession.Instance, CreateNotFoundProblem("Session not found.", $"Session '{id}' was not found."));
        }
    }

    private static Dictionary<string, string[]> CreateValidationErrors(string key, string message)
    {
        return new Dictionary<string, string[]>
        {
            [key] = new[] { message },
        };
    }

    private static ProblemDetails CreateNotFoundProblem(string title, string detail)
    {
        return new ProblemDetails
        {
            Title = title,
            Detail = detail,
            Status = StatusCodes.Status404NotFound,
        };
    }

    private static Dictionary<string, string[]> ToErrors(Exception exception)
    {
        return CreateValidationErrors("request", exception.Message);
    }

    private static OfferOption MapOfferOption(OfferOptionRequest request)
    {
        return new OfferOption(
            request.CharacterId.Trim(),
            new HiddenFlags(request.HiddenFlags.IsDrunk, request.HiddenFlags.IsLunatic),
            request.DisguiseCharacterId.Trim(),
            request.BorrowedAbilityCharacterId.Trim());
    }

    private static IReadOnlyList<string> NormalizeIds(IReadOnlyList<string> ids)
    {
        return ids
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();
    }

    private sealed record SessionLookupResult(bool Found, GameSession Session, ProblemDetails Problem);

    private sealed class EmptySession
    {
        public static readonly GameSession Instance = new(
            Guid.Empty,
            new Script(string.Empty, string.Empty, Array.Empty<CharacterDefinition>()),
            0,
            Array.Empty<PlayerSlot>(),
            GameStatus.Unknown,
            Array.Empty<string>(),
            false,
            false,
            false,
            0);
    }
}
