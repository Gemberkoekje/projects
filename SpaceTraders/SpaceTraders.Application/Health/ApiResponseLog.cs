namespace SpaceTraders.Application.Health;

/// <summary>
/// The 401 and 429 answers from the SpaceTraders API in the last <see cref="Window"/>, for the API
/// health rules. The API client's innermost handler records every one, retries included.
/// </summary>
/// <remarks>Thread-safe: requests go out from several threads at once.</remarks>
public sealed class ApiResponseLog
{
    /// <summary>How long an answer is remembered: the API rules look at the last hour.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(1);

    /// <summary>At most this many answers are remembered; far more than any limit.</summary>
    private const int MaxResponses = 10_000;

    private readonly Queue<ApiProblemResponse> _responses = new();
    private readonly Lock _lock = new();

    /// <summary>Records a 401: the server didn't accept the token.</summary>
    /// <param name="endpoint">The route template of the call.</param>
    /// <param name="at">When the answer came.</param>
    public void RecordUnauthorized(string endpoint, DateTimeOffset at) => Record(new ApiProblemResponse(401, endpoint, string.Empty, at));

    /// <summary>Records a 429.</summary>
    /// <param name="endpoint">The route template of the call.</param>
    /// <param name="source"><c>rate_limiter</c> or <c>infrastructure</c>, as <c>spacetraders_api_throttled_total</c> counts it.</param>
    /// <param name="at">When the answer came.</param>
    public void RecordThrottled(string endpoint, string source, DateTimeOffset at) => Record(new ApiProblemResponse(429, endpoint, source, at));

    /// <summary>The answers from <paramref name="since"/> on, oldest first.</summary>
    /// <param name="since">The start of the window.</param>
    /// <returns>The answers.</returns>
    public IReadOnlyList<ApiProblemResponse> Since(DateTimeOffset since)
    {
        lock (_lock)
        {
            return [.. _responses.Where(response => response.At >= since)];
        }
    }

    private void Record(ApiProblemResponse response)
    {
        lock (_lock)
        {
            _responses.Enqueue(response);
            while (_responses.Count > MaxResponses
                || (_responses.Count > 0 && _responses.Peek().At < response.At - Window))
            {
                _responses.Dequeue();
            }
        }
    }
}

/// <summary>A 401 or 429 answer from the SpaceTraders API.</summary>
public sealed record ApiProblemResponse
{
    /// <summary>Creates the record of one answer.</summary>
    /// <param name="StatusCode">401 or 429.</param>
    /// <param name="Endpoint">The route template of the call.</param>
    /// <param name="Source">For a 429, <c>rate_limiter</c> or <c>infrastructure</c>; otherwise empty.</param>
    /// <param name="At">When the answer came.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ApiProblemResponse(int StatusCode, string Endpoint, string Source, DateTimeOffset At)
    {
        this.StatusCode = StatusCode;
        this.Endpoint = Endpoint;
        this.Source = Source;
        this.At = At;
    }

    /// <summary>401 or 429.</summary>
    public required int StatusCode { get; init; }

    /// <summary>The route template of the call.</summary>
    public required string Endpoint { get; init; }

    /// <summary>For a 429, <c>rate_limiter</c> or <c>infrastructure</c>; otherwise empty.</summary>
    public required string Source { get; init; }

    /// <summary>When the answer came.</summary>
    public required DateTimeOffset At { get; init; }
}
