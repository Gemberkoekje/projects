namespace Klaverjas.application.Commands;

/// <summary>"Ook e-mailen" (D2): the standings online go to each member by mail, with the PDF; or first a test mail to Dad.</summary>
public sealed record StandMailen : ICommand
{
    /// <summary>Whether this is the test mail to Dad's own address.</summary>
    public bool Proef { get; init; }

    /// <summary>The players' page, for the link in the mail, such as <c>https://klaverjas.gemberkoekje.nl</c>.</summary>
    public string SiteAdres { get; init; } = string.Empty;
}
