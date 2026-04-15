using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sts2Viewer.Llm;

public sealed class AnthropicProvider : ILlmProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public AnthropicProvider(HttpClient httpClient, string apiKey)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
    }

    public async Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        string payload = JsonSerializer.Serialize(new
        {
            model = request.Model,
            max_tokens = request.MaxTokens,
            system = request.SystemPrompt,
            messages = new[]
            {
                new { role = "user", content = request.Prompt }
            }
        });

        using HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        message.Headers.Add("x-api-key", _apiKey);
        message.Headers.Add("anthropic-version", "2023-06-01");
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await _httpClient.SendAsync(message, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();

        using JsonDocument document = JsonDocument.Parse(body);
        if (document.RootElement.TryGetProperty("content", out JsonElement contentArray) && contentArray.ValueKind == JsonValueKind.Array)
        {
            for (int i = 0; i < contentArray.GetArrayLength(); i++)
            {
                JsonElement item = contentArray[i];
                if (item.TryGetProperty("type", out JsonElement type) && type.GetString() == "text")
                {
                    if (item.TryGetProperty("text", out JsonElement text))
                    {
                        return new LlmResult { Content = text.GetString() ?? string.Empty };
                    }
                }
            }
        }

        return new LlmResult { Content = string.Empty };
    }
}
