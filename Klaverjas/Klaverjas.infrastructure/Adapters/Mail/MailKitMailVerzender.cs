using Klaverjas.application.Mail;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using System.IO;
using System.Net.Sockets;

namespace Klaverjas.infrastructure.Adapters.Mail;

/// <summary>The mail account the app sends from: on the cluster the site keeper's Gmail (Q1).</summary>
public sealed record SmtpInstellingen
{
    /// <summary>The mail server, such as <c>smtp.gmail.com</c>.</summary>
    public string Host { get; init; } = string.Empty;

    /// <summary>Its port: 587 with STARTTLS for Gmail.</summary>
    public int Port { get; init; } = 587;

    /// <summary>Whether to require STARTTLS (always on the cluster; off only for a local test server).</summary>
    public bool StartTls { get; init; } = true;

    /// <summary>The account to sign in with; empty for a local test server without sign-in.</summary>
    public string Gebruiker { get; init; } = string.Empty;

    /// <summary>The account's (app) password.</summary>
    public string Wachtwoord { get; init; } = string.Empty;

    /// <summary>The address the mail comes from (Gmail insists on the account's own address).</summary>
    public string Van { get; init; } = string.Empty;

    /// <summary>Whether enough is set to send mail.</summary>
    public bool IsCompleet => Host.Length > 0 && Van.Length > 0 && (Gebruiker.Length == 0 || Wachtwoord.Length > 0);
}

/// <summary>Sends the mails through SMTP with MailKit, over one connection; each mail's outcome is reported.</summary>
public sealed class MailKitMailVerzender(SmtpInstellingen smtp, ILogger<MailKitMailVerzender> logger) : IMailVerzender
{
    private readonly SmtpInstellingen smtp = smtp ?? throw new ArgumentNullException(nameof(smtp));
    private readonly ILogger<MailKitMailVerzender> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public bool IsIngesteld => smtp.IsCompleet;

    /// <inheritdoc />
    public Task<IReadOnlyList<MailUitslag>> VerstuurAsync(IReadOnlyList<MailBericht> berichten, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(berichten);

        return VersturenAsync(berichten, cancellationToken);
    }

    private static bool IsMailFout(Exception fout)
        => fout is SmtpCommandException or SmtpProtocolException or ServiceNotConnectedException or AuthenticationException or SslHandshakeException or IOException or SocketException;

    private async Task<IReadOnlyList<MailUitslag>> VersturenAsync(IReadOnlyList<MailBericht> berichten, CancellationToken cancellationToken)
    {
        using var client = new SmtpClient();

        try
        {
            await client.ConnectAsync(smtp.Host, smtp.Port, smtp.StartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, cancellationToken);
            if (smtp.Gebruiker.Length > 0)
            {
                await client.AuthenticateAsync(smtp.Gebruiker, smtp.Wachtwoord, cancellationToken);
            }
        }
        catch (Exception fout) when (IsMailFout(fout))
        {
            logger.LogError(fout, "De mailserver {Host} was niet bereikbaar of weigerde de aanmelding.", smtp.Host);
            return [.. berichten.Select(_ => new MailUitslag { Fout = "De mailserver was niet bereikbaar: " + fout.Message })];
        }

        var uitslagen = new List<MailUitslag>();
        foreach (var bericht in berichten)
        {
            uitslagen.Add(await VerstuurEenAsync(client, bericht, cancellationToken));
        }

        if (client.IsConnected)
        {
            await client.DisconnectAsync(quit: true, cancellationToken);
        }

        return uitslagen;
    }

    private async Task<MailUitslag> VerstuurEenAsync(SmtpClient client, MailBericht bericht, CancellationToken cancellationToken)
    {
        try
        {
            using var mail = Maak(bericht);
            await client.SendAsync(mail, cancellationToken);
            return MailUitslag.Verstuurd;
        }
        catch (Exception fout) when (IsMailFout(fout) || fout is ParseException)
        {
            logger.LogWarning(fout, "Mail aan {Naam} niet verstuurd.", bericht.NaarNaam);
            return new MailUitslag { Fout = fout.Message };
        }
    }

    private MimeMessage Maak(MailBericht bericht)
    {
        var mail = new MimeMessage();
        mail.From.Add(new MailboxAddress(bericht.AfzenderNaam, smtp.Van));
        mail.To.Add(new MailboxAddress(bericht.NaarNaam, bericht.Naar));
        if (bericht.Antwoordadres.Length > 0)
        {
            mail.ReplyTo.Add(new MailboxAddress(bericht.AfzenderNaam, bericht.Antwoordadres));
        }

        mail.Subject = bericht.Onderwerp;

        var inhoud = new BodyBuilder { HtmlBody = bericht.Html, TextBody = bericht.Tekst };
        if (!bericht.Bijlage.IsEmpty)
        {
#pragma warning disable IDISP004 // The message owns the attachment: disposing the message disposes it.
            inhoud.Attachments.Add(bericht.BijlageNaam, bericht.Bijlage.ToArray(), new ContentType("application", "pdf"));
#pragma warning restore IDISP004
        }

        mail.Body = inhoud.ToMessageBody();
        return mail;
    }
}
