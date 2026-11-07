using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using CharacterCreator.core.Content;
using CharacterCreator.core.Rules;
using CharacterCreator.core.Rulesets;
using Qowaiv.Validation.Abstractions;

namespace CharacterCreator.WebApp.Client.Services;

/// <summary>The published ruleset, ready for the engine and the screen.</summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record LoadedRuleset
{
    /// <summary>The ruleset as published.</summary>
    required public PublishedRuleset Ruleset { get; init; }

    /// <summary>Its rules, for the engine.</summary>
    required public RuleBook Rules { get; init; }

    /// <summary>Its content, for the screen.</summary>
    required public ContentBook Content { get; init; }

    /// <summary>What the picker offers, in the book's order.</summary>
    required public IReadOnlyList<core.Skills.SkillKey> Offered { get; init; }
}

/// <summary>
/// Loads the current ruleset from <c>/api/rulesets/current</c> once per visit. The browser keeps the
/// answer and asks again with its ETag, so a later visit costs a 304.
/// </summary>
/// <remarks>Thread-safe; one per browser tab.</remarks>
/// <param name="http">An HTTP client on the app's own address.</param>
public sealed class RulesetClient(HttpClient http)
{
    private readonly HttpClient http = http ?? throw new ArgumentNullException(nameof(http));
    private readonly SemaphoreSlim gate = new (1, 1);
    private Result<LoadedRuleset> loaded = Result.WithMessages<LoadedRuleset>(ValidationMessage.Error("Not loaded yet."));

    /// <summary>The current ruleset; loaded on the first call, and again after a failure.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The ruleset, or why it could not be loaded.</returns>
    public async Task<Result<LoadedRuleset>> LoadAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!loaded.IsValid)
            {
                loaded = await FetchAsync(cancellationToken);
            }

            return loaded;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Makes a published ruleset ready for the screen.</summary>
    /// <param name="ruleset">The ruleset.</param>
    /// <returns>It, or why its rules or content cannot be right.</returns>
    public static Result<LoadedRuleset> Prepare(PublishedRuleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(ruleset);

        var rules = ruleset.ToRuleBook();
        var content = ruleset.ToContentBook();
        if (!rules.IsValid || !content.IsValid)
        {
            return Result.WithMessages<LoadedRuleset>([.. rules.Messages, .. content.Messages]);
        }

        return Result.For(new LoadedRuleset { Ruleset = ruleset, Rules = rules.Value, Content = content.Value, Offered = ruleset.Offered() });
    }

    private async Task<Result<LoadedRuleset>> FetchAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(new Uri("api/rulesets/current", UriKind.Relative), cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return Result.WithMessages<LoadedRuleset>(ValidationMessage.Error("No ruleset has been published yet."));
            }

            response.EnsureSuccessStatusCode();
            var ruleset = await response.Content.ReadFromJsonAsync(RulesetJsonContext.Default.PublishedRuleset, cancellationToken);
            return ruleset is null
                ? Result.WithMessages<LoadedRuleset>(ValidationMessage.Error("The ruleset came back empty."))
                : Prepare(ruleset);
        }
        catch (HttpRequestException exception)
        {
            return Result.WithMessages<LoadedRuleset>(ValidationMessage.Error($"The rules could not be loaded: {exception.Message}"));
        }
        catch (JsonException exception)
        {
            return Result.WithMessages<LoadedRuleset>(ValidationMessage.Error($"The rules did not read: {exception.Message}"));
        }
    }
}
