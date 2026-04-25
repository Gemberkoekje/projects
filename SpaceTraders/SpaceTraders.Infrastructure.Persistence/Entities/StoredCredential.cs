namespace SpaceTraders.Infrastructure.Persistence.Entities;

public sealed class StoredCredential
{
    public required string Key { get; set; }

    public required string Value { get; set; }

    public DateTimeOffset StoredAt { get; set; }
}
