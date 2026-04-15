using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sts2Viewer.Llm;

public sealed class OpenAiProvider : ILlmProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public OpenAiProvider(HttpClient httpClient, string apiKey)
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
            messages = new object[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = request.Prompt }
            }
        });

        using HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await _httpClient.SendAsync(message, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();

        using JsonDocument document = JsonDocument.Parse(body);
        if (document.RootElement.TryGetProperty("choices", out JsonElement choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
        {
            JsonElement first = choices[0];
            if (first.TryGetProperty("message", out JsonElement messageNode) && messageNode.TryGetProperty("content", out JsonElement content))
            {
                return new LlmResult { Content = content.GetString() ?? string.Empty };
            }
        }

        return new LlmResult { Content = string.Empty };
    }
}
