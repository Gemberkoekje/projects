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
