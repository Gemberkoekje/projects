using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using CharacterCreator.application.Ports;
using CharacterCreator.application.Void;
using Qowaiv;

namespace CharacterCreator.infrastructure.Void;

/// <summary>
/// VOID's REST API, called from the server with the signed-in person's ERU token (DESIGN.md §6).
/// The base address is <c>Void:BaseUrl</c>; the demo uses <c>void-test</c>.
/// </summary>
/// <remarks>
/// A typed <see cref="HttpClient"/>: thread-safe as far as <see cref="HttpClient"/> is. No CORS is
/// involved: this is a server calling a server.
/// </remarks>
/// <param name="http">The HTTP client, with VOID's base address when one is configured.</param>
public sealed class VoidHttpClient(HttpClient http) : IVoidClient
{
    private readonly HttpClient http = http ?? throw new ArgumentNullException(nameof(http));

    /// <inheritdoc />
    public bool IsConfigured => http.BaseAddress is not null;

    /// <inheritdoc />
    public string Source => http.BaseAddress?.ToString() ?? string.Empty;

    /// <inheritdoc />
    public async Task<Result<VoidSnapshot>> GetSkillsAsync(string accessToken, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return Result.WithMessages<VoidSnapshot>(ValidationMessage.Error("No VOID is configured (Void:BaseUrl), so nothing can be imported."));
        }

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return Result.WithMessages<VoidSnapshot>(ValidationMessage.Error("There is no ERU sign-in to call VOID with; sign in again."));
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("skills", UriKind.Relative));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return Result.WithMessages<VoidSnapshot>(ValidationMessage.Error($"VOID refused the sign-in ({(int)response.StatusCode}); sign in again, or check that the account has a PLIN."));
            }

            if (!response.IsSuccessStatusCode)
            {
                return Result.WithMessages<VoidSnapshot>(ValidationMessage.Error($"VOID answered {(int)response.StatusCode} {response.ReasonPhrase}."));
            }

            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            var envelope = await VoidSkillsJson.ParseAsync(body, cancellationToken);
            return Result.For(new VoidSnapshot
            {
                Source = Source,
                TakenAt = new DateTimeOffset(Clock.UtcNow(), TimeSpan.Zero),
                Synthesized = false,
                Skills = envelope.Skills,
            });
        }
        catch (HttpRequestException exception)
        {
            return Result.WithMessages<VoidSnapshot>(ValidationMessage.Error($"VOID could not be reached: {exception.Message}"));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.WithMessages<VoidSnapshot>(ValidationMessage.Error("VOID did not answer in time."));
        }
        catch (JsonException exception)
        {
            return Result.WithMessages<VoidSnapshot>(ValidationMessage.Error($"VOID's answer was not a list of skills: {exception.Message}"));
        }
    }
}
