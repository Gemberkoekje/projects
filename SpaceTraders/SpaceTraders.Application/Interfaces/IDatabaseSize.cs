namespace SpaceTraders.Application.Interfaces;

/// <summary>The size of the bot's database.</summary>
public interface IDatabaseSize
{
    Task<long> GetBytesAsync(CancellationToken cancellationToken = default);
}
