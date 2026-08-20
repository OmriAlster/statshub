using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using StatsHub.Api.Models;

namespace StatsHub.Api.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<Player> Players { get; set; }
        public DbSet<Season> Seasons { get; set; }
        public DbSet<Team> Teams { get; set; }
        public DbSet<PlayerTeam> PlayerTeams { get; set; }
        public DbSet<PlayerParent> PlayerParents { get; set; }
        public DbSet<Game> Games { get; set; }
        public DbSet<GameStats> GameStats { get; set; }
        public DbSet<Shot> Shots { get; set; }
        public DbSet<ShareLink> ShareLinks { get; set; }

        // SQLite never validated DateTime.Kind, so call sites across the app
        // freely mix DateTime.UtcNow with Kind-less values (new DateTime(...),
        // or dates deserialized from client JSON without a timezone). Postgres's
        // timestamptz columns reject anything that isn't explicitly Utc, so every
        // DateTime is normalized to Utc here rather than auditing every call site.
        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
            configurationBuilder.Properties<DateTime?>().HaveConversion<UtcNullableDateTimeConverter>();
        }

        private class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
        {
            public UtcDateTimeConverter() : base(
                toProvider => DateTime.SpecifyKind(toProvider, DateTimeKind.Utc),
                fromProvider => DateTime.SpecifyKind(fromProvider, DateTimeKind.Utc))
            { }
        }

        private class UtcNullableDateTimeConverter : ValueConverter<DateTime?, DateTime?>
        {
            public UtcNullableDateTimeConverter() : base(
                toProvider => toProvider.HasValue ? DateTime.SpecifyKind(toProvider.Value, DateTimeKind.Utc) : toProvider,
                fromProvider => fromProvider.HasValue ? DateTime.SpecifyKind(fromProvider.Value, DateTimeKind.Utc) : fromProvider)
            { }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // User configuration
            modelBuilder.Entity<User>()
                .HasKey(u => u.Id);
            modelBuilder.Entity<User>()
                .HasMany(u => u.Players)
                .WithOne(p => p.User)
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Player configuration
            modelBuilder.Entity<Player>()
                .HasKey(p => p.Id);
            modelBuilder.Entity<Player>()
                .HasMany(p => p.GameStats)
                .WithOne(gs => gs.Player)
                .HasForeignKey(gs => gs.PlayerId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<Player>()
                .HasIndex(p => p.UserId);
            modelBuilder.Entity<Player>()
                .HasIndex(p => p.LinkedUserId);
            modelBuilder.Entity<Player>()
                .HasIndex(p => p.InviteCode)
                .IsUnique();
            modelBuilder.Entity<Player>()
                .HasOne(p => p.LinkedUser)
                .WithMany()
                .HasForeignKey(p => p.LinkedUserId)
                .OnDelete(DeleteBehavior.SetNull);
            modelBuilder.Entity<Player>()
                .HasIndex(p => p.ParentInviteCode)
                .IsUnique();

            // PlayerParent configuration (household access, many-to-many)
            modelBuilder.Entity<PlayerParent>()
                .HasKey(pp => pp.Id);
            modelBuilder.Entity<PlayerParent>()
                .HasIndex(pp => new { pp.PlayerId, pp.UserId })
                .IsUnique();
            modelBuilder.Entity<PlayerParent>()
                .HasOne(pp => pp.Player)
                .WithMany(p => p.Parents)
                .HasForeignKey(pp => pp.PlayerId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<PlayerParent>()
                .HasOne(pp => pp.User)
                .WithMany()
                .HasForeignKey(pp => pp.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Season configuration
            modelBuilder.Entity<Season>()
                .HasKey(s => s.Id);
            modelBuilder.Entity<Season>()
                .HasMany(s => s.Teams)
                .WithOne(t => t.Season)
                .HasForeignKey(t => t.SeasonId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<Season>()
                .HasIndex(s => s.UserId);

            // Team configuration
            modelBuilder.Entity<Team>()
                .HasKey(t => t.Id);
            modelBuilder.Entity<Team>()
                .HasIndex(t => t.SeasonId);
            modelBuilder.Entity<Team>()
                .HasMany(t => t.Games)
                .WithOne(g => g.Team)
                .HasForeignKey(g => g.TeamId)
                .OnDelete(DeleteBehavior.Cascade);

            // PlayerTeam configuration (roster membership, many-to-many)
            modelBuilder.Entity<PlayerTeam>()
                .HasKey(pt => pt.Id);
            modelBuilder.Entity<PlayerTeam>()
                .HasIndex(pt => new { pt.PlayerId, pt.TeamId })
                .IsUnique();
            modelBuilder.Entity<PlayerTeam>()
                .HasOne(pt => pt.Player)
                .WithMany(p => p.PlayerTeams)
                .HasForeignKey(pt => pt.PlayerId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<PlayerTeam>()
                .HasOne(pt => pt.Team)
                .WithMany(t => t.PlayerTeams)
                .HasForeignKey(pt => pt.TeamId)
                .OnDelete(DeleteBehavior.Cascade);

            // Game configuration
            modelBuilder.Entity<Game>()
                .HasKey(g => g.Id);
            modelBuilder.Entity<Game>()
                .HasMany(g => g.GameStats)
                .WithOne(gs => gs.Game)
                .HasForeignKey(gs => gs.GameId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<Game>()
                .HasIndex(g => g.TeamId);

            // GameStats configuration
            modelBuilder.Entity<GameStats>()
                .HasKey(gs => gs.Id);
            modelBuilder.Entity<GameStats>()
                .HasIndex(gs => new { gs.GameId, gs.PlayerId })
                .IsUnique();

            // Shot configuration
            modelBuilder.Entity<Shot>()
                .HasKey(s => s.Id);
            modelBuilder.Entity<Shot>()
                .HasOne(s => s.GameStats)
                .WithMany(gs => gs.Shots)
                .HasForeignKey(s => s.GameStatsId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<Shot>()
                .HasIndex(s => s.GameStatsId);

            // ShareLink configuration
            modelBuilder.Entity<ShareLink>()
                .HasKey(sl => sl.Id);
            modelBuilder.Entity<ShareLink>()
                .HasIndex(sl => sl.Token)
                .IsUnique();
            modelBuilder.Entity<ShareLink>()
                .HasOne(sl => sl.Player)
                .WithMany(p => p.ShareLinks)
                .HasForeignKey(sl => sl.PlayerId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<ShareLink>()
                .HasOne(sl => sl.Game)
                .WithMany()
                .HasForeignKey(sl => sl.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
