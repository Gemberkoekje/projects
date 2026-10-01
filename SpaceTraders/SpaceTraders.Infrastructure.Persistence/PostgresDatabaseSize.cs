using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.Interfaces;

namespace SpaceTraders.Infrastructure.Persistence;

public sealed class PostgresDatabaseSize(SpaceTradersDbContext db) : IDatabaseSize
{
    public Task<long> GetBytesAsync(CancellationToken cancellationToken = default)
        => db.Database
            .SqlQuery<long>($"SELECT pg_database_size(current_database()) AS \"Value\"")
            .SingleAsync(cancellationToken);
}
