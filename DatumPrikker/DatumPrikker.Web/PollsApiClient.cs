using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace DatumPrikker.Web;

public sealed class PollsApiClient(HttpClient httpClient, IHttpContextAccessor httpContextAccessor)
{
    private const string OwnerIdentityHeader = "X-Owner-IdentityId";
    private const string RespondentIdentityHeader = "X-Respondent-IdentityId";
    private const string RespondentNameHeader = "X-Respondent-Name";

    public async Task<IReadOnlyList<PollSummaryDto>> GetOwnerPollsAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/polls");
        AddOwnerIdentityHeader(request);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<List<PollSummaryDto>>(cancellationToken);
        return result ?? [];
    }

    public async Task<CreatePollResponseDto> CreatePollAsync(CreatePollRequestDto payload, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/polls")
        {
            Content = JsonContent.Create(payload),
        };

        AddOwnerIdentityHeader(request);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CreatePollResponseDto>(cancellationToken);
        if (result is null)
        {
            throw new InvalidOperationException("Create poll response did not include payload.");
        }

        return result;
    }

    public async Task<PollDetailsDto> GetPollAsync(string shareToken, CancellationToken cancellationToken = default)
    {
        var result = await httpClient.GetFromJsonAsync<PollDetailsDto>($"/api/polls/{shareToken}", cancellationToken);
        if (result is null)
        {
            throw new InvalidOperationException("Poll details were not found.");
        }

        return result;
    }

    public async Task SubmitResponsesAsync(string shareToken, SubmitResponsesRequestDto payload, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/polls/{shareToken}/respond")
        {
            Content = JsonContent.Create(payload),
        };

        if (IsAuthenticated())
        {
            AddAuthenticatedRespondentHeaders(request);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            throw new PollClosedException("This poll is closed and no longer accepts responses.");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            throw new RateLimitedException("Too many submissions. Please wait a few minutes before trying again.");
        }

        response.EnsureSuccessStatusCode();
    }

    public async Task<PollResultsDto> GetPollResultsAsync(string shareToken, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/polls/{shareToken}/results");
        AddOwnerIdentityHeader(request);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<PollResultsDto>(cancellationToken);
        if (result is null)
        {
            throw new InvalidOperationException("Poll results were not found.");
        }

        return result;
    }

    public bool IsAuthenticated()
    {
        var user = httpContextAccessor.HttpContext?.User;
        return user != null && user.Identity != null && user.Identity.IsAuthenticated;
    }

    public string GetAuthenticatedDisplayName()
    {
        if (!IsAuthenticated())
        {
            return string.Empty;
        }

        return BuildDisplayName(GetAuthenticatedUser());
    }

    private void AddAuthenticatedRespondentHeaders(HttpRequestMessage request)
    {
        var user = GetAuthenticatedUser();
        request.Headers.Remove(RespondentIdentityHeader);
        request.Headers.Remove(RespondentNameHeader);
        request.Headers.Add(RespondentIdentityHeader, BuildIdentityId(user));
        request.Headers.Add(RespondentNameHeader, BuildDisplayName(user));
    }

    private void AddOwnerIdentityHeader(HttpRequestMessage request)
    {
        var user = GetAuthenticatedUser();
        request.Headers.Remove(OwnerIdentityHeader);
        request.Headers.Add(OwnerIdentityHeader, BuildIdentityId(user));
    }

    private ClaimsPrincipal GetAuthenticatedUser()
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user == null || user.Identity == null || !user.Identity.IsAuthenticated)
        {
            throw new InvalidOperationException("Authenticated user context is required.");
        }

        return user;
    }

    private static string BuildIdentityId(ClaimsPrincipal user)
    {
        var subject = GetClaimValue(user, ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new InvalidOperationException("Authenticated subject claim is missing.");
        }

        var issuer =
            GetClaimValue(user, "http://schemas.microsoft.com/identity/claims/identityprovider")
            ?? GetClaimValue(user, "iss")
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Issuer
            ?? string.Empty;

        var provider = NormalizeProvider(issuer);
        return $"{provider}:{subject}";
    }

    private static string BuildDisplayName(ClaimsPrincipal user)
    {
        var name = GetClaimValue(user, ClaimTypes.Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        var identityName = user.Identity?.Name;
        if (!string.IsNullOrWhiteSpace(identityName))
        {
            return identityName;
        }

        return GetClaimValue(user, ClaimTypes.Email) ?? BuildIdentityId(user);
    }

    private static string NormalizeProvider(string issuer)
    {
        if (issuer.Contains("google", StringComparison.OrdinalIgnoreCase))
        {
            return "google";
        }

        if (issuer.Contains("microsoft", StringComparison.OrdinalIgnoreCase)
            || issuer.Contains("live.com", StringComparison.OrdinalIgnoreCase)
            || issuer.Contains("sts.windows.net", StringComparison.OrdinalIgnoreCase))
        {
            return "microsoft";
        }

        return "external";
    }

    private static string GetClaimValue(ClaimsPrincipal user, string claimType)
    {
        return user.FindFirst(claimType)?.Value ?? string.Empty;
    }
}

public enum PollAvailability
{
    None = 0,
    Yes = 1,
    No = 2,
    Maybe = 3,
}

public sealed record PollSummaryDto(
    Guid PollId,
    string ShareToken,
    string Title,
    DateTimeOffset? ClosesAtUtc,
    DateTimeOffset? ClosedAtUtc,
    DateTimeOffset CreatedAtUtc,
    int OptionsCount,
    int ResponsesCount,
    bool IsOpen);

public sealed record CreatePollRequestDto(
    string Title,
    string Description,
    DateTimeOffset? ClosesAtUtc,
    List<CreatePollOptionRequestDto> Options);

public sealed record CreatePollOptionRequestDto(DateOnly Date, TimeOnly StartTime, TimeOnly EndTime);

public sealed record CreatePollResponseDto(Guid PollId, string ShareToken);

public sealed record PollDetailsDto(
    Guid PollId,
    string ShareToken,
    string Title,
    string Description,
    DateTimeOffset? ClosesAtUtc,
    DateTimeOffset? ClosedAtUtc,
    bool IsOpen,
    List<PollOptionDto> Options);

public sealed record PollOptionDto(Guid PollOptionId, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime);

public sealed record SubmitResponsesRequestDto(string RespondentName, List<SubmitResponseInputDto> Responses);

public sealed record SubmitResponseInputDto(Guid PollOptionId, PollAvailability Availability, string Reason);

public sealed record PollResultsDto(Guid PollId, string ShareToken, string Title, List<PollResultOptionDto> Options);

public sealed record PollResultOptionDto(
    Guid PollOptionId,
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int YesCount,
    int MaybeCount,
    int NoCount,
    List<PollResultRespondentDto> Respondents);

public sealed record PollResultRespondentDto(string RespondentName, PollAvailability Availability, string Reason);

public sealed class PollClosedException(string message) : Exception(message);

public sealed class RateLimitedException(string message) : Exception(message);
