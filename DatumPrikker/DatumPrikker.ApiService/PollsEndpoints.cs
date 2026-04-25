using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DatumPrikker.ApiService.Data;
using DatumPrikker.ApiService.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace DatumPrikker.ApiService;

internal static class PollsEndpoints
{
    private const string OwnerIdentityHeader = "X-Owner-IdentityId";
    private const string RespondentIdentityHeader = "X-Respondent-IdentityId";
    private const string RespondentNameHeader = "X-Respondent-Name";
    private const string PollDetailsCacheTag = "poll-details";

    public static IEndpointRouteBuilder MapPollsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/polls");

        group.MapPost("/", CreatePollAsync);
        group.MapGet("/", ListPollsAsync);
        group.MapGet("/{shareToken}", GetPollAsync)
            .CacheOutput(policy => policy.Expire(TimeSpan.FromSeconds(30)).Tag(PollDetailsCacheTag));
        group.MapGet("/{shareToken}/results", GetResultsAsync);
        group.MapPost("/{shareToken}/respond", RespondAsync)
            .RequireRateLimiting("anonymous-respond");

        return app;
    }

    private static async Task<IResult> CreatePollAsync(
        HttpContext context,
        DatumPrikkerDbContext dbContext,
        CreatePollRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetHeader(context, OwnerIdentityHeader, out var ownerIdentityId))
        {
            return Results.Unauthorized();
        }

        if (request.Options.Count == 0)
        {
            return Results.BadRequest("At least one poll option is required.");
        }

        var utcNow = DateTimeOffset.UtcNow;

        var poll = new Poll
        {
            Id = Guid.NewGuid(),
            Title = request.Title.Trim(),
            Description = request.Description.Trim(),
            OwnerIdentityId = ownerIdentityId,
            ShareToken = await GenerateUniqueShareTokenAsync(dbContext, cancellationToken),
            ClosesAtUtc = request.ClosesAtUtc,
            CreatedAtUtc = utcNow,
            UpdatedAtUtc = utcNow,
            Options = request.Options.Select(option => new PollOption
            {
                Id = Guid.NewGuid(),
                Date = option.Date,
                StartTime = option.StartTime,
                EndTime = option.EndTime,
                CreatedAtUtc = utcNow,
                UpdatedAtUtc = utcNow,
            }).ToList(),
        };

        dbContext.Polls.Add(poll);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/polls/{poll.ShareToken}", new CreatePollResponse(poll.Id, poll.ShareToken));
    }

    private static async Task<IResult> ListPollsAsync(
        HttpContext context,
        DatumPrikkerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!TryGetHeader(context, OwnerIdentityHeader, out var ownerIdentityId))
        {
            return Results.Unauthorized();
        }

        var utcNow = DateTimeOffset.UtcNow;

        var polls = await dbContext.Polls
            .AsNoTracking()
            .Where(poll => poll.OwnerIdentityId == ownerIdentityId)
            .Select(poll => new PollSummaryResponse(
                poll.Id,
                poll.ShareToken,
                poll.Title,
                poll.ClosesAtUtc,
                poll.ClosedAtUtc,
                poll.CreatedAtUtc,
                poll.Options.Count,
                poll.Options.SelectMany(option => option.Responses).Count(),
                poll.ClosedAtUtc == null && (poll.ClosesAtUtc == null || poll.ClosesAtUtc > utcNow)))
            .OrderByDescending(poll => poll.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return Results.Ok(polls);
    }

    private static async Task<IResult> GetPollAsync(
        string shareToken,
        DatumPrikkerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var utcNow = DateTimeOffset.UtcNow;

        var poll = await dbContext.Polls
            .AsNoTracking()
            .Where(x => x.ShareToken == shareToken)
            .Select(x => new PollDetailsResponse(
                x.Id,
                x.ShareToken,
                x.Title,
                x.Description,
                x.ClosesAtUtc,
                x.ClosedAtUtc,
                x.ClosedAtUtc == null && (x.ClosesAtUtc == null || x.ClosesAtUtc > utcNow),
                x.Options
                    .OrderBy(option => option.Date)
                    .ThenBy(option => option.StartTime)
                    .Select(option => new PollOptionResponse(option.Id, option.Date, option.StartTime, option.EndTime))
                    .ToList()))
            .SingleOrDefaultAsync(cancellationToken);

        return poll is null ? Results.NotFound() : Results.Ok(poll);
    }

    private static async Task<IResult> GetResultsAsync(
        HttpContext context,
        string shareToken,
        DatumPrikkerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!TryGetHeader(context, OwnerIdentityHeader, out var ownerIdentityId))
        {
            return Results.Unauthorized();
        }

        var poll = await dbContext.Polls
            .AsNoTracking()
            .Include(x => x.Options)
            .ThenInclude(x => x.Responses)
            .SingleOrDefaultAsync(x => x.ShareToken == shareToken, cancellationToken);

        if (poll is null)
        {
            return Results.NotFound();
        }

        if (!string.Equals(poll.OwnerIdentityId, ownerIdentityId, StringComparison.Ordinal))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var rankedResults = poll.Options
            .Select(option => new PollResultOptionResponse(
                option.Id,
                option.Date,
                option.StartTime,
                option.EndTime,
                option.Responses.Count(response => response.Availability == Availability.Yes),
                option.Responses.Count(response => response.Availability == Availability.Maybe),
                option.Responses.Count(response => response.Availability == Availability.No),
                option.Responses
                    .OrderBy(response => response.RespondentName)
                    .Select(response => new PollResultRespondentResponse(response.RespondentName, response.Availability, response.Reason))
                    .ToList()))
            .OrderByDescending(option => option.YesCount)
            .ThenByDescending(option => option.MaybeCount)
            .ThenBy(option => option.Date)
            .ThenBy(option => option.StartTime)
            .ToList();

        var response = new PollResultsResponse(poll.Id, poll.ShareToken, poll.Title, rankedResults);

        return Results.Ok(response);
    }

    private static async Task<IResult> RespondAsync(
        HttpContext context,
        string shareToken,
        DatumPrikkerDbContext dbContext,
        IOutputCacheStore outputCacheStore,
        SubmitResponsesRequest request,
        CancellationToken cancellationToken)
    {
        var poll = await dbContext.Polls
            .Include(x => x.Options)
            .ThenInclude(x => x.Responses)
            .SingleOrDefaultAsync(x => x.ShareToken == shareToken, cancellationToken);

        if (poll is null)
        {
            return Results.NotFound();
        }

        var utcNow = DateTimeOffset.UtcNow;

        if (poll.ClosedAtUtc is not null || (poll.ClosesAtUtc is not null && poll.ClosesAtUtc <= utcNow))
        {
            return Results.Conflict("Poll is closed.");
        }

        if (request.Responses.Count == 0)
        {
            return Results.BadRequest("At least one response is required.");
        }

        var respondentKey = string.Empty;
        var respondentName = string.Empty;

        if (TryGetHeader(context, RespondentIdentityHeader, out var identityId))
        {
            respondentKey = identityId;
            respondentName = TryGetHeader(context, RespondentNameHeader, out var claimName)
                ? claimName
                : identityId;
        }
        else
        {
            respondentName = request.RespondentName.Trim();
            if (respondentName.Length == 0)
            {
                return Results.BadRequest("RespondentName is required for anonymous users.");
            }

            respondentKey = $"anon:{NormalizeName(respondentName)}";
        }

        var optionsById = poll.Options.ToDictionary(option => option.Id);

        foreach (var input in request.Responses)
        {
            if (!optionsById.TryGetValue(input.PollOptionId, out var option))
            {
                return Results.BadRequest($"Poll option '{input.PollOptionId}' does not belong to this poll.");
            }

            var existing = option.Responses.SingleOrDefault(response => response.RespondentKey == respondentKey);

            if (existing is null)
            {
                option.Responses.Add(new PollResponse
                {
                    Id = Guid.NewGuid(),
                    PollOptionId = option.Id,
                    RespondentKey = respondentKey,
                    RespondentName = respondentName,
                    Availability = input.Availability,
                    Reason = input.Reason.Trim(),
                    CreatedAtUtc = utcNow,
                    UpdatedAtUtc = utcNow,
                });
                continue;
            }

            existing.RespondentName = respondentName;
            existing.Availability = input.Availability;
            existing.Reason = input.Reason.Trim();
            existing.UpdatedAtUtc = utcNow;
        }

        poll.UpdatedAtUtc = utcNow;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            var submittedByOption = request.Responses.ToDictionary(x => x.PollOptionId);
            var persistedResponses = await dbContext.Responses
                .AsNoTracking()
                .Where(x => x.RespondentKey == respondentKey && submittedByOption.Keys.Contains(x.PollOptionId))
                .ToListAsync(cancellationToken);

            var hasAllResponses = persistedResponses.Count == submittedByOption.Count
                && persistedResponses.All(persisted =>
                    submittedByOption.TryGetValue(persisted.PollOptionId, out var submitted)
                    && persisted.Availability == submitted.Availability
                    && string.Equals(persisted.Reason, submitted.Reason.Trim(), StringComparison.Ordinal)
                    && string.Equals(persisted.RespondentName, respondentName, StringComparison.Ordinal));

            if (!hasAllResponses)
            {
                throw;
            }
        }

        await TryEvictPollDetailsCacheAsync(outputCacheStore, cancellationToken);

        return Results.NoContent();
    }

    private static async Task TryEvictPollDetailsCacheAsync(IOutputCacheStore outputCacheStore, CancellationToken cancellationToken)
    {
        try
        {
            await outputCacheStore.EvictByTagAsync(PollDetailsCacheTag, cancellationToken);
        }
        catch (Exception)
        {
        }
    }

    private static bool TryGetHeader(HttpContext context, string headerName, out string value)
    {
        if (context.Request.Headers.TryGetValue(headerName, out var headerValues))
        {
            var headerValue = headerValues.ToString().Trim();
            if (headerValue.Length > 0)
            {
                value = headerValue;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static string NormalizeName(string name)
    {
        var normalized = new StringBuilder(name.Length);
        var previousWasWhitespace = false;

        foreach (var character in name.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasWhitespace)
                {
                    normalized.Append(' ');
                }

                previousWasWhitespace = true;
                continue;
            }

            normalized.Append(char.ToLowerInvariant(character));
            previousWasWhitespace = false;
        }

        return normalized.ToString();
    }

    private static async Task<string> GenerateUniqueShareTokenAsync(DatumPrikkerDbContext dbContext, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var token = CreateShareToken();
            var exists = await dbContext.Polls.AnyAsync(poll => poll.ShareToken == token, cancellationToken);
            if (!exists)
            {
                return token;
            }
        }

        throw new InvalidOperationException("Failed to generate a unique share token.");
    }

    private static string CreateShareToken()
    {
        const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        const int tokenLength = 8;

        Span<char> buffer = stackalloc char[tokenLength];
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        }

        return new string(buffer);
    }

    internal sealed record CreatePollRequest(
        string Title,
        string Description,
        DateTimeOffset? ClosesAtUtc,
        List<CreatePollOptionRequest> Options);

    internal sealed record CreatePollOptionRequest(DateOnly Date, TimeOnly StartTime, TimeOnly EndTime);

    internal sealed record CreatePollResponse(Guid PollId, string ShareToken);

    internal sealed record PollSummaryResponse(
        Guid PollId,
        string ShareToken,
        string Title,
        DateTimeOffset? ClosesAtUtc,
        DateTimeOffset? ClosedAtUtc,
        DateTimeOffset CreatedAtUtc,
        int OptionsCount,
        int ResponsesCount,
        bool IsOpen);

    internal sealed record PollDetailsResponse(
        Guid PollId,
        string ShareToken,
        string Title,
        string Description,
        DateTimeOffset? ClosesAtUtc,
        DateTimeOffset? ClosedAtUtc,
        bool IsOpen,
        List<PollOptionResponse> Options);

    internal sealed record PollOptionResponse(Guid PollOptionId, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime);

    internal sealed record SubmitResponsesRequest(string RespondentName, List<SubmitResponseInputRequest> Responses);

    internal sealed record SubmitResponseInputRequest(Guid PollOptionId, Availability Availability, string Reason);

    internal sealed record PollResultsResponse(Guid PollId, string ShareToken, string Title, List<PollResultOptionResponse> Options);

    internal sealed record PollResultOptionResponse(
        Guid PollOptionId,
        DateOnly Date,
        TimeOnly StartTime,
        TimeOnly EndTime,
        int YesCount,
        int MaybeCount,
        int NoCount,
        List<PollResultRespondentResponse> Respondents);

    internal sealed record PollResultRespondentResponse(string RespondentName, Availability Availability, string Reason);
}
