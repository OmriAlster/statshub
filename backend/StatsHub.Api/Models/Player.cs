namespace StatsHub.Api.Models
{
    public class Player
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public int JerseyNumber { get; set; }
        public string Position { get; set; } = string.Empty; // PG, SG, SF, PF, C
        public int? Height { get; set; } // in cm
        public int? Weight { get; set; } // in kg
        public DateTime DateOfBirth { get; set; }
        public string? ProfilePictureUrl { get; set; }

        // Player account linking: lets the player log in with their own
        // Google account and see a read-only view of their own stats.
        public int? LinkedUserId { get; set; }
        public string? InviteCode { get; set; }
        public DateTime? InviteCodeExpiresAt { get; set; }
        public string? InvitePasswordHash { get; set; }

        // A second (or third) parent/guardian can claim access with this code,
        // separate from the player's own login invite above.
        public string? ParentInviteCode { get; set; }
        public DateTime? ParentInviteCodeExpiresAt { get; set; }
        public string? ParentInvitePasswordHash { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public User User { get; set; } = null!;
        public User? LinkedUser { get; set; }
        public ICollection<GameStats> GameStats { get; set; } = new List<GameStats>();
        public ICollection<PlayerTeam> PlayerTeams { get; set; } = new List<PlayerTeam>();
        public ICollection<ShareLink> ShareLinks { get; set; } = new List<ShareLink>();
        public ICollection<PlayerParent> Parents { get; set; } = new List<PlayerParent>();
    }
}
