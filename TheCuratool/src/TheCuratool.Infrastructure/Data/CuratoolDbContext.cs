namespace TheCuratool.Infrastructure.Data;

public sealed class CuratoolDbContext(DbContextOptions<CuratoolDbContext> options) : DbContext(options)
{
    public DbSet<ScriptEntity> Scripts => Set<ScriptEntity>();

    public DbSet<GameSessionEntity> GameSessions => Set<GameSessionEntity>();

    public DbSet<PlayerSlotEntity> PlayerSlots => Set<PlayerSlotEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureScriptEntity(modelBuilder);
        ConfigureGameSessionEntity(modelBuilder);
        ConfigurePlayerSlotEntity(modelBuilder);
    }

    private static void ConfigureScriptEntity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ScriptEntity>()
            .HasKey(s => s.Id);

        modelBuilder.Entity<ScriptEntity>()
            .Property(s => s.Name)
            .IsRequired()
            .HasMaxLength(256);

        modelBuilder.Entity<ScriptEntity>()
            .Property(s => s.Author)
            .IsRequired()
            .HasMaxLength(256);

        modelBuilder.Entity<ScriptEntity>()
            .Property(s => s.RawJson)
            .IsRequired();

        modelBuilder.Entity<ScriptEntity>()
            .Property(s => s.CreatedAt)
            .HasDefaultValueSql("CURRENT_TIMESTAMP AT TIME ZONE 'UTC'");

        modelBuilder.Entity<ScriptEntity>()
            .HasMany(s => s.GameSessions)
            .WithOne(g => g.Script)
            .HasForeignKey(g => g.ScriptId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureGameSessionEntity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GameSessionEntity>()
            .HasKey(g => g.Id);

        modelBuilder.Entity<GameSessionEntity>()
            .Property(g => g.ScriptId)
            .IsRequired();

        modelBuilder.Entity<GameSessionEntity>()
            .Property(g => g.PlayerCount)
            .IsRequired();

        modelBuilder.Entity<GameSessionEntity>()
            .Property(g => g.ActiveLorics)
            .IsRequired()
            .HasDefaultValue("[]");

        modelBuilder.Entity<GameSessionEntity>()
            .Property(g => g.UseMarionette)
            .HasDefaultValue(false);

        modelBuilder.Entity<GameSessionEntity>()
            .Property(g => g.IsLegionGame)
            .HasDefaultValue(false);

        modelBuilder.Entity<GameSessionEntity>()
            .Property(g => g.LegionCount)
            .HasDefaultValue(0);

        modelBuilder.Entity<GameSessionEntity>()
            .Property(g => g.Status)
            .IsRequired()
            .HasDefaultValue(1); // GameStatus.Drafting

        modelBuilder.Entity<GameSessionEntity>()
            .Property(g => g.CreatedAt)
            .HasDefaultValueSql("CURRENT_TIMESTAMP AT TIME ZONE 'UTC'");

        modelBuilder.Entity<GameSessionEntity>()
            .HasMany(g => g.PlayerSlots)
            .WithOne(p => p.GameSession)
            .HasForeignKey(p => p.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigurePlayerSlotEntity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlayerSlotEntity>()
            .HasKey(p => p.Id);

        modelBuilder.Entity<PlayerSlotEntity>()
            .Property(p => p.SessionId)
            .IsRequired();

        modelBuilder.Entity<PlayerSlotEntity>()
            .Property(p => p.DraftOrder)
            .IsRequired();

        modelBuilder.Entity<PlayerSlotEntity>()
            .Property(p => p.PlayerId)
            .IsRequired();

        modelBuilder.Entity<PlayerSlotEntity>()
            .Property(p => p.ChosenCharacterId)
            .HasMaxLength(128);

        modelBuilder.Entity<PlayerSlotEntity>()
            .Property(p => p.IsStAssigned)
            .HasDefaultValue(false);

        modelBuilder.Entity<PlayerSlotEntity>()
            .Property(p => p.BorrowedAbilityCharacterId)
            .HasMaxLength(128)
            .HasDefaultValue(string.Empty);

        modelBuilder.Entity<PlayerSlotEntity>()
            .Property(p => p.OfferedCharacterIds)
            .IsRequired()
            .HasDefaultValue("[]");

        modelBuilder.Entity<PlayerSlotEntity>()
            .Property(p => p.HiddenFlags)
            .IsRequired()
            .HasDefaultValue(@"{""isDrunk"":false,""isLunatic"":false}");

        modelBuilder.Entity<PlayerSlotEntity>()
            .Property(p => p.IsAtheistCommitmentConfirmed)
            .HasDefaultValue(false);

        // Ensure unique constraint on (SessionId, DraftOrder)
        modelBuilder.Entity<PlayerSlotEntity>()
            .HasIndex(p => new { p.SessionId, p.DraftOrder })
            .IsUnique();
    }
}
