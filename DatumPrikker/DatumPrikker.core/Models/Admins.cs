namespace DatumPrikker.core.Models;

/// <summary>
/// The admin accounts, by account id (D16), as configured in <c>Admin:Accounts</c>. Ids rather than
/// emails, because an email claim can be spoofed and admin is worth more than making polls.
/// </summary>
public sealed class Admins
{
    /// <summary>The refusal for somebody who is not an admin.</summary>
    public const string OnlyMessage = "Alleen een beheerder kan dit.";

    private static readonly char[] Separators = [',', ';', ' ', '\t', '\r', '\n'];

    private readonly HashSet<string> ids;

    private Admins(IEnumerable<string> ids) => this.ids = new HashSet<string>(ids, StringComparer.Ordinal);

    /// <summary>No admins at all.</summary>
    public static Admins None { get; } = new Admins([]);

    /// <summary>How many admins there are.</summary>
    public int Count => ids.Count;

    /// <summary>Reads a list such as <c>google:1234, microsoft:abcd</c>.</summary>
    /// <param name="configured">The configured value; empty means no admins.</param>
    /// <returns>The admins.</returns>
    public static Admins Parse(string configured)
        => string.IsNullOrWhiteSpace(configured)
            ? None
            : new Admins(configured.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>True when the account is an admin.</summary>
    /// <param name="account">The account.</param>
    /// <returns>True for an admin; never for somebody anonymous.</returns>
    public bool Includes(Account account)
    {
        ArgumentNullException.ThrowIfNull(account);

        return !account.IsAnonymous && ids.Contains(account.Id);
    }

    /// <summary>OK for an admin, <see cref="OnlyMessage"/> for anybody else.</summary>
    /// <param name="account">The account.</param>
    /// <returns>The check's result.</returns>
    public Result Require(Account account)
        => Includes(account) ? Result.OK : Result.WithMessages(ValidationMessage.Error(OnlyMessage, nameof(account)));
}
