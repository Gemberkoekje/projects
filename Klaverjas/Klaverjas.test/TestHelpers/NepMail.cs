using Klaverjas.application.Mail;

namespace Klaverjas.test.TestHelpers;

/// <summary>Collects the mails instead of sending them; addresses in <see cref="Weigert"/> fail.</summary>
internal sealed class NepMailVerzender : IMailVerzender
{
    public bool IsIngesteld { get; set; } = true;

    public HashSet<string> Weigert { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public List<MailBericht> Verstuurd { get; } = [];

    public Task<IReadOnlyList<MailUitslag>> VerstuurAsync(IReadOnlyList<MailBericht> berichten, CancellationToken cancellationToken = default)
    {
        var uitslagen = new List<MailUitslag>();
        foreach (var bericht in berichten)
        {
            if (Weigert.Contains(bericht.Naar))
            {
                uitslagen.Add(new MailUitslag { Fout = "550 Mailbox unavailable" });
            }
            else
            {
                Verstuurd.Add(bericht);
                uitslagen.Add(MailUitslag.Verstuurd);
            }
        }

        return Task.FromResult<IReadOnlyList<MailUitslag>>(uitslagen);
    }
}

/// <summary>A PDF maker that only counts.</summary>
internal sealed class NepPdfMaker : IStandPdfMaker
{
    public int Gemaakt { get; private set; }

    public ReadOnlyMemory<byte> Maak(OnlineStand online, Instellingen instellingen)
    {
        Gemaakt++;
        return "%PDF-nep"u8.ToArray();
    }
}
