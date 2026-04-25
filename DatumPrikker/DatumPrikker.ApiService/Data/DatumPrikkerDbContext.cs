using DatumPrikker.ApiService.Domain;
using Microsoft.EntityFrameworkCore;

namespace DatumPrikker.ApiService.Data;

public sealed class DatumPrikkerDbContext(DbContextOptions<DatumPrikkerDbContext> options) : DbContext(options)
{
    public DbSet<Poll> Polls => Set<Poll>();

    public DbSet<PollOption> PollOptions => Set<PollOption>();

    public DbSet<PollResponse> Responses => Set<PollResponse>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Poll>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).HasMaxLength(200);
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.OwnerIdentityId).HasMaxLength(200);
            entity.Property(x => x.ShareToken).HasMaxLength(16);
            entity.HasIndex(x => x.ShareToken).IsUnique();
            entity.HasMany(x => x.Options)
                .WithOne(x => x.Poll)
                .HasForeignKey(x => x.PollId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PollOption>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.PollId, x.Date, x.StartTime, x.EndTime }).IsUnique();
            entity.HasMany(x => x.Responses)
                .WithOne(x => x.PollOption)
                .HasForeignKey(x => x.PollOptionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PollResponse>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.RespondentKey).HasMaxLength(200);
            entity.Property(x => x.RespondentName).HasMaxLength(200);
            entity.Property(x => x.Reason).HasMaxLength(1000);
            entity.HasIndex(x => new { x.PollOptionId, x.RespondentKey }).IsUnique();
        });
    }
}
