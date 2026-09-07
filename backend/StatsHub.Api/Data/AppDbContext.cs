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
        public DbSet<PlayerIbbaLink> PlayerIbbaLinks { get; set; }
        public DbSet<IbbaTeam> IbbaTeams { get; set; }
        public DbSet<PlayerIbbaTeam> PlayerIbbaTeams { get; set; }
        public DbSet<PushSubscription> PushSubscriptions { get; set; }

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

            // Game configuration. HomeTeamId/AwayTeamId deliberately have no
            // formal FK here - IbbaGameCode says which table they actually mean
            // (IbbaTeams for a synced game, Teams for a manual one), and EF
            // can't express a single column that conditionally references two
            // different tables. Every read resolves them explicitly instead
            // (GameService et al.), so deleting a Team or IbbaTeam row never
            // cascades into Games at the DB level - GameService.
            // DeleteGamesExclusiveToTeamAsync is what decides, per game,
            // whether it survives a Team's deletion.
            modelBuilder.Entity<Game>()
                .HasKey(g => g.Id);
            modelBuilder.Entity<Game>()
                .HasMany(g => g.GameStats)
                .WithOne(gs => gs.Game)
                .HasForeignKey(gs => gs.GameId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<Game>()
                .HasIndex(g => g.HomeTeamId);
            modelBuilder.Entity<Game>()
                .HasIndex(g => g.AwayTeamId);

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

            // PlayerIbbaLink configuration (one per player)
            modelBuilder.Entity<PlayerIbbaLink>()
                .HasKey(pil => pil.Id);
            modelBuilder.Entity<PlayerIbbaLink>()
                .HasIndex(pil => pil.PlayerId)
                .IsUnique();
            modelBuilder.Entity<PlayerIbbaLink>()
                .HasOne(pil => pil.Player)
                .WithMany()
                .HasForeignKey(pil => pil.PlayerId)
                .OnDelete(DeleteBehavior.Cascade);

            // IbbaTeam configuration - one row per real IBBA team, shared across
            // every player/game that references it. Doubles as its current
            // league standing and crest cache.
            modelBuilder.Entity<IbbaTeam>()
                .HasKey(t => t.Id);
            modelBuilder.Entity<IbbaTeam>()
                .HasIndex(t => t.TeamUrl)
                .IsUnique();
            modelBuilder.Entity<IbbaTeam>()
                .HasIndex(t => t.LeagueUrl);

            // The link lives on Team, not IbbaTeam - many app Teams (one per
            // player/parent) can point at the same shared IbbaTeam row, so no
            // uniqueness here.
            modelBuilder.Entity<Team>()
                .HasIndex(t => t.IbbaTeamId);
            modelBuilder.Entity<Team>()
                .HasOne(t => t.IbbaTeam)
                .WithMany()
                .HasForeignKey(t => t.IbbaTeamId)
                .OnDelete(DeleteBehavior.SetNull);

            // PlayerIbbaTeam configuration (which teams a player currently plays for)
            modelBuilder.Entity<PlayerIbbaTeam>()
                .HasKey(pit => pit.Id);
            modelBuilder.Entity<PlayerIbbaTeam>()
                .HasIndex(pit => new { pit.PlayerIbbaLinkId, pit.IbbaTeamId })
                .IsUnique();
            modelBuilder.Entity<PlayerIbbaTeam>()
                .HasOne(pit => pit.PlayerIbbaLink)
                .WithMany(pil => pil.Teams)
                .HasForeignKey(pit => pit.PlayerIbbaLinkId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<PlayerIbbaTeam>()
                .HasOne(pit => pit.IbbaTeam)
                .WithMany()
                .HasForeignKey(pit => pit.IbbaTeamId)
                .OnDelete(DeleteBehavior.Cascade);

            // Unique per real fixture - guards against two concurrent syncs (e.g. two
            // different parents each following a teammate on the same IBBA team)
            // racing past the "does this game already exist" check and both
            // inserting it. Filtered so manually-created games (IbbaGameCode null)
            // are unaffected - Postgres and SQLite both treat NULLs as distinct in a
            // unique index regardless, but the filter also keeps this off the
            // (much larger) set of non-IBBA rows.
            modelBuilder.Entity<Game>()
                .HasIndex(g => g.IbbaGameCode)
                .IsUnique()
                .HasFilter("\"IbbaGameCode\" IS NOT NULL");

            // PushSubscription configuration
            modelBuilder.Entity<PushSubscription>()
                .HasKey(ps => ps.Id);
            modelBuilder.Entity<PushSubscription>()
                .HasIndex(ps => ps.Endpoint)
                .IsUnique();
            modelBuilder.Entity<PushSubscription>()
                .HasIndex(ps => ps.UserId);
            modelBuilder.Entity<PushSubscription>()
                .HasOne(ps => ps.User)
                .WithMany()
                .HasForeignKey(ps => ps.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
