using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using TheCuratool.Infrastructure.Data;

#nullable disable

namespace TheCuratool.Infrastructure.Migrations
{
    [DbContext(typeof(CuratoolDbContext))]
    partial class CuratoolDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "9.0.0")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("TheCuratool.Infrastructure.Entities.GameSessionEntity", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<string>("ActiveLorics")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("timestamp with time zone")
                        .HasDefaultValueSql("CURRENT_TIMESTAMP AT TIME ZONE 'UTC'");

                    b.Property<int>("PlayerCount")
                        .HasColumnType("integer");

                    b.Property<Guid>("ScriptId")
                        .HasColumnType("uuid");

                    b.Property<int>("Status")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer")
                        .HasDefaultValue(1);

                    b.Property<bool>("UseMarionette")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("boolean")
                        .HasDefaultValue(false);

                    b.HasKey("Id");

                    b.HasIndex("ScriptId");

                    b.ToTable("GameSessions");
                });

            modelBuilder.Entity("TheCuratool.Infrastructure.Entities.PlayerSlotEntity", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<string>("ChosenCharacterId")
                        .HasMaxLength(128)
                        .HasColumnType("character varying(128)");

                    b.Property<int>("DraftOrder")
                        .HasColumnType("integer");

                    b.Property<string>("HiddenFlags")
                        .IsRequired()
                        .ValueGeneratedOnAdd()
                        .HasColumnType("text")
                        .HasDefaultValue("{\"isDrunk\":false,\"isLunatic\":false}");

                    b.Property<bool>("IsAtheistCommitmentConfirmed")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("boolean")
                        .HasDefaultValue(false);

                    b.Property<string>("OfferedCharacterIds")
                        .IsRequired()
                        .ValueGeneratedOnAdd()
                        .HasColumnType("text")
                        .HasDefaultValue("[]");

                    b.Property<Guid>("PlayerId")
                        .HasColumnType("uuid");

                    b.Property<Guid>("SessionId")
                        .HasColumnType("uuid");

                    b.HasKey("Id");

                    b.HasIndex("SessionId");

                    b.HasIndex("SessionId", "DraftOrder")
                        .IsUnique();

                    b.ToTable("PlayerSlots");
                });

            modelBuilder.Entity("TheCuratool.Infrastructure.Entities.ScriptEntity", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<string>("Author")
                        .IsRequired()
                        .HasMaxLength(256)
                        .HasColumnType("character varying(256)");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("timestamp with time zone")
                        .HasDefaultValueSql("CURRENT_TIMESTAMP AT TIME ZONE 'UTC'");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(256)
                        .HasColumnType("character varying(256)");

                    b.Property<string>("RawJson")
                        .IsRequired()
                        .HasColumnType("text");

                    b.HasKey("Id");

                    b.ToTable("Scripts");
                });

            modelBuilder.Entity("TheCuratool.Infrastructure.Entities.GameSessionEntity", b =>
                {
                    b.HasOne("TheCuratool.Infrastructure.Entities.ScriptEntity", "Script")
                        .WithMany("GameSessions")
                        .HasForeignKey("ScriptId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.Navigation("Script");
                });

            modelBuilder.Entity("TheCuratool.Infrastructure.Entities.PlayerSlotEntity", b =>
                {
                    b.HasOne("TheCuratool.Infrastructure.Entities.GameSessionEntity", "GameSession")
                        .WithMany("PlayerSlots")
                        .HasForeignKey("SessionId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("GameSession");
                });

            modelBuilder.Entity("TheCuratool.Infrastructure.Entities.GameSessionEntity", b =>
                {
                    b.Navigation("PlayerSlots");
                });

            modelBuilder.Entity("TheCuratool.Infrastructure.Entities.ScriptEntity", b =>
                {
                    b.Navigation("GameSessions");
                });
#pragma warning restore 612, 618
        }
    }
}
