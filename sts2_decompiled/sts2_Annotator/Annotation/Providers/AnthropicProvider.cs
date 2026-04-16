using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sts2Extractor.Annotation.Providers;

internal sealed class AnthropicProvider : IBatchCapableLlmProvider
{
    private const string Endpoint = "https://api.anthropic.com/v1/messages";
    private const string BatchEndpoint = "https://api.anthropic.com/v1/messages/batches";
    private const string BatchBetaHeader = "message-batches-2024-09-24";
    private const int MaxAttempts = 8;
    private const int BatchPollIntervalSeconds = 10;
    private const int BatchMaxPollMinutes = 120;

    private readonly string _apiKey;
    private readonly LlmRateLimiter _rateLimiter;

    public AnthropicProvider(string apiKey, LlmRateLimitSettings rateLimitSettings)
    {
        _apiKey = apiKey;
        _rateLimiter = new LlmRateLimiter(rateLimitSettings);
    }

    public async Task<AnnotationResult> AnnotateAsync(AnnotationRequest request, CancellationToken cancellationToken)
    {
        using HttpClient client = BuildClient();

        int estimatedInputTokens = EstimateInputTokens(request);
        int estimatedOutputTokens = Math.Max(request.MaxTokens, 1);

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
            TimeSpan throttledFor = await _rateLimiter.ReserveAsync(estimatedInputTokens, estimatedOutputTokens, cancellationToken);
            if (throttledFor > TimeSpan.Zero)
            {
                Console.WriteLine($"Throttled {Math.Ceiling(throttledFor.TotalSeconds)}s to stay within configured limits (Anthropic).");
            }

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

    private static int EstimateInputTokens(AnnotationRequest request)
    {
        return EstimateTokenCount(request.SystemPrompt) + EstimateTokenCount(request.Prompt);
    }

    private static int EstimateTokenCount(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 1;
        }

        int chars = value.Length;
        int tokens = (int)Math.Ceiling(chars / 4d);
        return Math.Max(tokens, 1);
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

        // Use exponential backoff with longer initial delays to handle overload gracefully
        // attempt 1 = 2s, 2 = 4s, 3 = 8s, 4 = 16s, 5 = 32s, 6 = 60s, 7 = 120s, 8 = 120s
        int backoff = (int)Math.Min(120, Math.Pow(2, attempt));
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

    public async Task<IReadOnlyDictionary<string, BatchAnnotationResult>> BatchAnnotateAsync(
        IReadOnlyList<BatchAnnotationItem> items,
        CancellationToken cancellationToken)
    {
        using HttpClient client = BuildClient(includeBetaHeader: true);

        // Build the batch submission body
        List<object> requests = new List<object>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            BatchAnnotationItem item = items[i];
            requests.Add(new
            {
                custom_id = item.CustomId,
                @params = new
                {
                    model = item.Request.Model,
                    max_tokens = item.Request.MaxTokens,
                    system = item.Request.SystemPrompt,
                    messages = new[]
                    {
                        new { role = "user", content = item.Request.Prompt }
                    }
                }
            });
        }

        string submitBody = JsonSerializer.Serialize(new { requests });
        using StringContent submitContent = new StringContent(submitBody, Encoding.UTF8, "application/json");
        using HttpResponseMessage submitResponse = await client.PostAsync(BatchEndpoint, submitContent, cancellationToken);
        string submitText = await submitResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!submitResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Anthropic batch submit failed ({(int)submitResponse.StatusCode}): {submitText}");
        }

        string batchId;
        using (JsonDocument submitDoc = JsonDocument.Parse(submitText))
        {
            batchId = submitDoc.RootElement.GetProperty("id").GetString() ?? string.Empty;
        }

        Console.WriteLine($"  Anthropic batch submitted: {batchId} ({items.Count} requests). Polling...");

        // Poll until processing_status == "ended"
        int maxPolls = (BatchMaxPollMinutes * 60) / BatchPollIntervalSeconds;
        for (int poll = 0; poll < maxPolls; poll++)
        {
            await Task.Delay(TimeSpan.FromSeconds(BatchPollIntervalSeconds), cancellationToken);

            using HttpResponseMessage pollResponse = await client.GetAsync($"{BatchEndpoint}/{batchId}", cancellationToken);
            string pollText = await pollResponse.Content.ReadAsStringAsync(cancellationToken);
            if (!pollResponse.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Anthropic batch poll failed ({(int)pollResponse.StatusCode}): {pollText}");
            }

            using JsonDocument pollDoc = JsonDocument.Parse(pollText);
            string status = pollDoc.RootElement.GetProperty("processing_status").GetString() ?? string.Empty;

            if (string.Equals(status, "ended", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"  Batch {batchId} completed.");
                break;
            }

            if (poll % 6 == 0)
            {
                Console.WriteLine($"  Batch {batchId} status={status} (elapsed ~{(poll + 1) * BatchPollIntervalSeconds}s)...");
            }

            if (poll == maxPolls - 1)
            {
                throw new InvalidOperationException($"Anthropic batch {batchId} did not complete within {BatchMaxPollMinutes} minutes.");
            }
        }

        // Retrieve results (JSONL stream)
        using HttpResponseMessage resultsResponse = await client.GetAsync($"{BatchEndpoint}/{batchId}/results", cancellationToken);
        string resultsText = await resultsResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!resultsResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Anthropic batch results failed ({(int)resultsResponse.StatusCode}): {resultsText}");
        }

        Dictionary<string, BatchAnnotationResult> results = new Dictionary<string, BatchAnnotationResult>(StringComparer.Ordinal);
        using StringReader reader = new StringReader(resultsText);
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using JsonDocument lineDoc = JsonDocument.Parse(line);
            string customId = lineDoc.RootElement.GetProperty("custom_id").GetString() ?? string.Empty;
            JsonElement resultElement = lineDoc.RootElement.GetProperty("result");
            string resultType = resultElement.GetProperty("type").GetString() ?? string.Empty;

            BatchAnnotationResult batchResult = new BatchAnnotationResult { CustomId = customId };
            if (string.Equals(resultType, "succeeded", StringComparison.OrdinalIgnoreCase))
            {
                JsonElement messageElement = resultElement.GetProperty("message");
                string text = ExtractText(messageElement);
                batchResult.Result = new AnnotationResult
                {
                    Provider = "anthropic",
                    Model = messageElement.TryGetProperty("model", out JsonElement modelEl)
                        ? modelEl.GetString() ?? string.Empty
                        : string.Empty,
                    Content = text
                };
            }
            else
            {
                string errorDetail = resultElement.TryGetProperty("error", out JsonElement errEl)
                    ? errEl.ToString()
                    : resultType;
                batchResult.ErrorMessage = errorDetail;
                Console.WriteLine($"  Batch item {customId} failed: {errorDetail}");
            }

            results[customId] = batchResult;
        }

        return results;
    }

    private HttpClient BuildClient(bool includeBetaHeader = false)
    {
        HttpClient client = new HttpClient();
        client.DefaultRequestHeaders.Add("x-api-key", _apiKey);
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (includeBetaHeader)
        {
            client.DefaultRequestHeaders.Add("anthropic-beta", BatchBetaHeader);
        }

        return client;
    }
}
