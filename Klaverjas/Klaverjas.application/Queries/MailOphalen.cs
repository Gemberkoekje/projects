namespace Klaverjas.application.Queries;

/// <summary>The mail card on the Stand screen.</summary>
public sealed record MailOverzichtOphalen : IQuery<MailOverzicht>;

/// <summary>"Zo ziet de e-mail eruit".</summary>
public sealed record MailVoorbeeldOphalen : IQuery<MailVoorbeeld>
{
    /// <summary>The players' page, for the link in the mail.</summary>
    public string SiteAdres { get; init; } = string.Empty;
}

/// <summary>"PDF bekijken": the standings online as a PDF.</summary>
public sealed record StandPdfOphalen : IQuery<StandPdf>;
