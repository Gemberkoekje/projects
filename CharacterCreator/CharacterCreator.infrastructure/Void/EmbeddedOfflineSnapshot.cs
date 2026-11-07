using System.Text.Json;
using CharacterCreator.application.Ports;
using CharacterCreator.application.Void;
using CharacterCreator.core.Rules;

namespace CharacterCreator.infrastructure.Void;

/// <summary>
/// The offline snapshot embedded next to a ruleset's content (<c>Content/{ruleset}/void-snapshot.json</c>),
/// in VOID's <c>GET /skills</c> shape. Made by <c>scripts/make-offline-snapshot.py</c> from the public
/// rulebook draft, so it holds book skills only and is marked synthesized (DESIGN.md §4.2).
/// </summary>
/// <remarks>Stateless and thread-safe.</remarks>
public sealed class EmbeddedOfflineSnapshot : IOfflineVoidSnapshot
{
    /// <inheritdoc />
    public async Task<Result<VoidSnapshot>> LoadAsync(string ruleset, CancellationToken cancellationToken)
    {
        var assembly = typeof(EmbeddedOfflineSnapshot).Assembly;
        var wanted = $"Content/{ruleset}/void-snapshot.json";
        var resource = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => string.Equals(name.Replace('\\', '/'), wanted, StringComparison.OrdinalIgnoreCase));
        if (resource is null)
        {
            return Result.WithMessages<VoidSnapshot>(ValidationMessage.Error($"There is no offline snapshot for ruleset '{ruleset}'."));
        }

        try
        {
            await using var stream = assembly.GetManifestResourceStream(resource);
            var envelope = await VoidSkillsJson.ParseAsync(stream, cancellationToken);
            return Result.For(new VoidSnapshot
            {
                Source = string.IsNullOrWhiteSpace(envelope.Source) ? "offline snapshot" : envelope.Source,
                TakenAt = envelope.TakenAt ?? DateTimeOffset.UnixEpoch,
                Synthesized = envelope.Synthesized,
                Skills = envelope.Skills,
                Creation = envelope.Creation ?? CreationRules.Vortex,
                CreationFromVoid = envelope.Creation is not null,
            });
        }
        catch (JsonException exception)
        {
            return Result.WithMessages<VoidSnapshot>(ValidationMessage.Error($"The offline snapshot for '{ruleset}' does not parse: {exception.Message}"));
        }
    }
}
