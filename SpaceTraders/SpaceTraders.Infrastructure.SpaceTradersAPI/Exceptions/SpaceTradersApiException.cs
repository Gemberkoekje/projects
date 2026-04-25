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

        int? errorCode = null;
        string? apiMessage = null;

        if (!string.IsNullOrWhiteSpace(responseBody))
        {
            try
            {
                using var document = JsonDocument.Parse(responseBody);
                var root = document.RootElement;

                if (root.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.Object)
                {
                    if (errorElement.TryGetProperty("code", out var codeElement) && codeElement.TryGetInt32(out var parsedCode))
                    {
                        errorCode = parsedCode;
                    }

                    if (errorElement.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String)
                    {
                        apiMessage = messageElement.GetString();
                    }
                }
                else if (root.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String)
                {
                    apiMessage = messageElement.GetString();
                }
            }
            catch (JsonException)
            {
            }
        }

        var message = apiMessage is null
            ? $"SpaceTraders API request to '{endpoint}' failed with status code {(int)response.StatusCode}."
            : $"SpaceTraders API request to '{endpoint}' failed with status code {(int)response.StatusCode}: {apiMessage}";

        return new SpaceTradersApiException(message, response.StatusCode, endpoint, responseBody, errorCode);
    }
}
