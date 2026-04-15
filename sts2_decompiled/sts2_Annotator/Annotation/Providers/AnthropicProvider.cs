using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sts2Extractor.Annotation.Providers;

internal sealed class AnthropicProvider : ILlmProvider
{
    private const string Endpoint = "https://api.anthropic.com/v1/messages";
    private const int MaxAttempts = 8;

    private readonly string _apiKey;

    public AnthropicProvider(string apiKey)
    {
        _apiKey = apiKey;
    }

    public async Task<AnnotationResult> AnnotateAsync(AnnotationRequest request, CancellationToken cancellationToken)
    {
        using HttpClient client = new HttpClient();
        client.DefaultRequestHeaders.Add("x-api-key", _apiKey);
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        string body = JsonSerializer.Serialize(new
        {
            model = request.Model,
            max_tokens = request.MaxTokens,
            system = request.SystemPrompt,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = request.Prompt
                }
            }
        });

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            using StringContent content = new StringContent(body, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await client.PostAsync(Endpoint, content, cancellationToken);
            string responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                using JsonDocument document = JsonDocument.Parse(responseText);
                string extracted = ExtractText(document.RootElement);

                return new AnnotationResult
                {
                    Provider = "anthropic",
                    Model = request.Model,
                    Content = extracted
                };
            }

            bool isRateLimited = IsRateLimited(response.StatusCode, responseText);
            if (isRateLimited && attempt < MaxAttempts)
            {
                int waitSeconds = GetWaitSeconds(response, attempt);
                Console.WriteLine($"({attempt}/{MaxAttempts}) Rate limit from Anthropic; waiting {waitSeconds}s before retry");
                await Task.Delay(TimeSpan.FromSeconds(waitSeconds), cancellationToken);
                continue;
            }

            throw new InvalidOperationException($"Anthropic request failed ({(int)response.StatusCode}): {responseText}");
        }

        throw new InvalidOperationException("Anthropic request failed after retries.");
    }

    private static bool IsRateLimited(HttpStatusCode statusCode, string responseText)
    {
        if (statusCode == HttpStatusCode.TooManyRequests || statusCode == HttpStatusCode.ServiceUnavailable || (int)statusCode == 529)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(responseText)
            && responseText.IndexOf("rate", StringComparison.OrdinalIgnoreCase) >= 0
            && responseText.IndexOf("limit", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return false;
    }

    private static int GetWaitSeconds(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter != null)
        {
            if (response.Headers.RetryAfter.Delta.HasValue)
            {
                int seconds = (int)Math.Ceiling(response.Headers.RetryAfter.Delta.Value.TotalSeconds);
                if (seconds > 0)
                {
                    return seconds;
                }
            }

            if (response.Headers.RetryAfter.Date.HasValue)
            {
                TimeSpan delay = response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow;
                int seconds = (int)Math.Ceiling(delay.TotalSeconds);
                if (seconds > 0)
                {
                    return seconds;
                }
            }
        }

        int backoff = (int)Math.Min(60, Math.Pow(2, attempt));
        return Math.Max(backoff, 1);
    }

    private static string ExtractText(JsonElement root)
    {
        if (!root.TryGetProperty("content", out JsonElement contentNode) || contentNode.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        foreach (JsonElement node in contentNode.EnumerateArray())
        {
            if (!node.TryGetProperty("type", out JsonElement typeNode))
            {
                continue;
            }

            if (!string.Equals(typeNode.GetString(), "text", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (node.TryGetProperty("text", out JsonElement textNode))
            {
                return textNode.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }
}
