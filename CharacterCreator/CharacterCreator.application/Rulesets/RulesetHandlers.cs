using CharacterCreator.application.Ports;
using CharacterCreator.core.Content;
using CharacterCreator.core.Rulesets;
using Qowaiv;

namespace CharacterCreator.application.Rulesets;

/// <summary>Import from VOID without storing anything: the dry run (DESIGN.md §4.2).</summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record PreviewImport
{
    /// <summary>The signed-in SL's or EC's ERU access token, for VOID.</summary>
    required public string AccessToken { get; init; }

    /// <summary>Who would publish it.</summary>
    required public string PublishedBy { get; init; }
}

/// <summary>Import from VOID and publish the result.</summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record PublishRuleset
{
    /// <summary>The signed-in SL's or EC's ERU access token, for VOID.</summary>
    required public string AccessToken { get; init; }

    /// <summary>Who publishes it.</summary>
    required public string PublishedBy { get; init; }
}

/// <summary>Reads VOID's skills and builds the ruleset publishing would store, with its report.</summary>
/// <param name="content">The content per ruleset.</param>
/// <param name="voids">The configured VOID.</param>
/// <param name="store">The published rulesets, to report changes against the current one.</param>
/// <param name="logger">The log.</param>
public sealed class PreviewImportHandler(IContentSource content, IVoidClient voids, IRulesetStore store, ILogger<PreviewImportHandler> logger)
{
    private readonly IContentSource content = content ?? throw new ArgumentNullException(nameof(content));
    private readonly IVoidClient voids = voids ?? throw new ArgumentNullException(nameof(voids));
    private readonly IRulesetStore store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly ILogger<PreviewImportHandler> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Runs the dry run.</summary>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">Cancels it.</param>
    /// <returns>The ruleset and report, or why VOID or the content could not be read.</returns>
    public Task<Result<ImportOutcome>> HandleAsync(PreviewImport command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return PreviewAsync(command, cancellationToken);
    }

    /// <summary>The newest content ruleset: the one an import builds on.</summary>
    /// <param name="source">The content per ruleset.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The content, or why there is none.</returns>
    internal static async Task<Result<ContentBook>> LatestContentAsync(IContentSource source, CancellationToken cancellationToken)
    {
        var ruleset = source.Rulesets.Order(StringComparer.OrdinalIgnoreCase).LastOrDefault();
        return ruleset is null
            ? Result.WithMessages<ContentBook>(ValidationMessage.Error("There is no content to build a ruleset on."))
            : await source.LoadAsync(ruleset, cancellationToken);
    }

    /// <summary>Errors as validation messages, one each.</summary>
    /// <param name="errors">The errors.</param>
    /// <returns>The messages.</returns>
    internal static List<IValidationMessage> Messages(IEnumerable<string> errors)
        => [.. errors.Select(error => (IValidationMessage)ValidationMessage.Error(error))];

    private async Task<Result<ImportOutcome>> PreviewAsync(PreviewImport command, CancellationToken cancellationToken)
    {
        var book = await LatestContentAsync(content, cancellationToken);
        if (!book.IsValid)
        {
            return Result.WithMessages<ImportOutcome>(book.Messages);
        }

        var snapshot = await voids.GetSkillsAsync(command.AccessToken, cancellationToken);
        if (!snapshot.IsValid)
        {
            logger.LogWarning("Importing from VOID {Source} failed: {Reason}", voids.Source, string.Join("; ", snapshot.Messages.Select(message => message.Message)));
            return Result.WithMessages<ImportOutcome>(snapshot.Messages);
        }

        var current = await store.CurrentAsync(cancellationToken);
        return Result.For(RulesetBuilder.Build(book.Value, snapshot.Value, current.IsValid ? current.Value : null, command.PublishedBy, Now()));
    }

    /// <summary>Now, from Qowaiv's clock, so the specs can pin it.</summary>
    /// <returns>The moment, in UTC.</returns>
    internal static DateTimeOffset Now() => new (Clock.UtcNow(), TimeSpan.Zero);
}

/// <summary>Imports from VOID and publishes the result, unless the report says it cannot be published.</summary>
/// <param name="preview">The dry run.</param>
/// <param name="store">The published rulesets.</param>
/// <param name="logger">The log.</param>
public sealed class PublishRulesetHandler(PreviewImportHandler preview, IRulesetStore store, ILogger<PublishRulesetHandler> logger)
{
    private readonly PreviewImportHandler preview = preview ?? throw new ArgumentNullException(nameof(preview));
    private readonly IRulesetStore store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly ILogger<PublishRulesetHandler> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Publishes.</summary>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">Cancels it.</param>
    /// <returns>What was published, or why not.</returns>
    public Task<Result<ImportOutcome>> HandleAsync(PublishRuleset command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return PublishAsync(command, cancellationToken);
    }

    private async Task<Result<ImportOutcome>> PublishAsync(PublishRuleset command, CancellationToken cancellationToken)
    {
        var outcome = await preview.HandleAsync(new PreviewImport { AccessToken = command.AccessToken, PublishedBy = command.PublishedBy }, cancellationToken);
        if (!outcome.IsValid)
        {
            return outcome;
        }

        if (!outcome.Value.Report.CanPublish)
        {
            return Result.WithMessages<ImportOutcome>(PreviewImportHandler.Messages(outcome.Value.Report.Errors));
        }

        var stored = await store.AddAsync(outcome.Value.Ruleset, cancellationToken);
        if (!stored.IsValid)
        {
            return Result.WithMessages<ImportOutcome>(stored.Messages);
        }

        logger.LogInformation("Ruleset {RulesetId} published by {User} from {Source}", outcome.Value.Ruleset.Id, command.PublishedBy, outcome.Value.Ruleset.Source);
        return outcome;
    }
}

/// <summary>
/// On the first start, publishes the content with the offline snapshot, so the build screen is never
/// empty before anyone imported from VOID (DESIGN.md §4.2). Does nothing once a ruleset exists.
/// </summary>
/// <param name="content">The content per ruleset.</param>
/// <param name="offline">The offline snapshots.</param>
/// <param name="store">The published rulesets.</param>
/// <param name="logger">The log.</param>
public sealed class SeedRulesetHandler(IContentSource content, IOfflineVoidSnapshot offline, IRulesetStore store, ILogger<SeedRulesetHandler> logger)
{
    /// <summary>Who the seed is published by.</summary>
    public const string FirstStart = "first start";

    private readonly IContentSource content = content ?? throw new ArgumentNullException(nameof(content));
    private readonly IOfflineVoidSnapshot offline = offline ?? throw new ArgumentNullException(nameof(offline));
    private readonly IRulesetStore store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly ILogger<SeedRulesetHandler> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Publishes the seed when no ruleset exists yet.</summary>
    /// <param name="cancellationToken">Cancels it.</param>
    /// <returns>OK, also when there was nothing to do; otherwise why the seed could not be published.</returns>
    public async Task<Result> HandleAsync(CancellationToken cancellationToken)
    {
        if ((await store.CurrentAsync(cancellationToken)).IsValid)
        {
            return Result.OK;
        }

        var book = await PreviewImportHandler.LatestContentAsync(content, cancellationToken);
        if (!book.IsValid)
        {
            return Result.WithMessages(book.Messages);
        }

        var snapshot = await offline.LoadAsync(book.Value.Ruleset, cancellationToken);
        if (!snapshot.IsValid)
        {
            return Result.WithMessages(snapshot.Messages);
        }

        var outcome = RulesetBuilder.Build(book.Value, snapshot.Value, null, FirstStart, PreviewImportHandler.Now());
        if (!outcome.Report.CanPublish)
        {
            return Result.WithMessages(PreviewImportHandler.Messages(outcome.Report.Errors));
        }

        var stored = await store.AddAsync(outcome.Ruleset, cancellationToken);
        if (stored.IsValid)
        {
            logger.LogInformation("Ruleset {RulesetId} seeded from the offline snapshot ({Offered} skills)", outcome.Ruleset.Id, outcome.Report.Offered);
        }

        return stored;
    }
}

/// <summary>The newest published ruleset, for the build screen.</summary>
/// <param name="store">The published rulesets.</param>
public sealed class ReadCurrentRulesetHandler(IRulesetStore store)
{
    private readonly IRulesetStore store = store ?? throw new ArgumentNullException(nameof(store));

    /// <summary>Reads it.</summary>
    /// <param name="cancellationToken">Cancels it.</param>
    /// <returns>The ruleset, or why there is none.</returns>
    public Task<Result<PublishedRuleset>> HandleAsync(CancellationToken cancellationToken) => store.CurrentAsync(cancellationToken);
}
