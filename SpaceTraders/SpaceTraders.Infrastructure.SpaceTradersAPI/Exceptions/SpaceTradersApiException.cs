using System.Net;
using System.Text.Json;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Exceptions;

public sealed class SpaceTradersApiException : Exception
{
    public SpaceTradersApiException(
        string message,
        HttpStatusCode statusCode,
        string endpoint,
        string? responseBody,
        int? errorCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Endpoint = endpoint;
        ResponseBody = responseBody;
        ErrorCode = errorCode;
    }

    public HttpStatusCode StatusCode { get; }

    public string Endpoint { get; }

    public string? ResponseBody { get; }

    public int? ErrorCode { get; }

    /// <summary>
    /// True when the call failed because the server was reset since the agent token was issued
    /// (401, "Token reset_date does not match the server"). Every call with that token fails now.
    /// </summary>
    public bool IsServerReset =>
        StatusCode == HttpStatusCode.Unauthorized
        && Message.Contains("Token reset_date does not match the server", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when the call failed because the server is being reset right now (503, "The universe is
    /// being reset"). It answers so for some minutes before the new universe opens: on 2026-11-15
    /// from 13:00:22Z for six minutes or so, before the first 401 of <see cref="IsServerReset"/> (B93).
    /// </summary>
    public bool IsResetInProgress =>
        StatusCode == HttpStatusCode.ServiceUnavailable
        && Message.Contains("The universe is being reset", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether <paramref name="exception"/>, or one it holds, is a call that failed because of the
    /// server's reset: <see cref="IsServerReset"/> or <see cref="IsResetInProgress"/> (B93).
    /// </summary>
    /// <param name="exception">The exception, with its inner exceptions.</param>
    /// <returns>True when the reset is what failed the call.</returns>
    public static bool CameFromTheReset(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            SpaceTradersApiException api => api.IsServerReset || api.IsResetInProgress,
            AggregateException aggregate => aggregate.InnerExceptions.Any(CameFromTheReset),
            _ => exception.InnerException is { } inner && CameFromTheReset(inner),
        };
    }

    internal static async Task<SpaceTradersApiException> CreateAsync(
        HttpResponseMessage response,
        string endpoint,
        CancellationToken cancellationToken)
    {
        var responseBody = response.Content is null
            ? null
            : await response.Content.ReadAsStringAsync(cancellationToken);

        var (errorCode, apiMessage) = TryReadApiError(responseBody);

        var message = apiMessage is null
            ? $"SpaceTraders API request to '{endpoint}' failed with status code {(int)response.StatusCode}."
            : $"SpaceTraders API request to '{endpoint}' failed with status code {(int)response.StatusCode}: {apiMessage}";

        return new SpaceTradersApiException(message, response.StatusCode, endpoint, responseBody, errorCode);
    }

    private static (int? ErrorCode, string? ApiMessage) TryReadApiError(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return (null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;

            if (root.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.Object)
            {
                return ReadErrorObject(errorElement);
            }

            return (null, ReadMessage(root));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static (int? ErrorCode, string? ApiMessage) ReadErrorObject(JsonElement errorElement)
    {
        var errorCode = errorElement.TryGetProperty("code", out var codeElement) && codeElement.TryGetInt32(out var parsedCode)
            ? parsedCode
            : (int?)null;

        return (errorCode, ReadMessage(errorElement));
    }

    private static string? ReadMessage(JsonElement element)
        => element.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String
            ? messageElement.GetString()
            : null;
}
