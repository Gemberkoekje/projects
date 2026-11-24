using Klaverjas.application.Mail;

namespace Klaverjas.application.Ports;

/// <summary>Sends mail (D2): the standings to each member, or the test mail to Dad.</summary>
public interface IMailVerzender
{
    /// <summary>Whether the server has a mail account set up; without one, mailing is switched off.</summary>
    bool IsIngesteld { get; }

    /// <summary>Sends the mails over one connection, one by one; one failure does not stop the rest.</summary>
    /// <param name="berichten">The mails.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>How each mail went, in the same order.</returns>
    Task<IReadOnlyList<MailUitslag>> VerstuurAsync(IReadOnlyList<MailBericht> berichten, CancellationToken cancellationToken = default);
}
