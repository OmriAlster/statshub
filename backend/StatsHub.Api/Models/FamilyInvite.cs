namespace StatsHub.Api.Models
{
    // A link a parent sends on WhatsApp: /i/{Code}. A "Parent" link makes
    // whoever opens it a parent of the chosen kids (and of kids added later -
    // see PlayerService.CreatePlayerAsync), and any number of people can use
    // it until it expires. A "Player" link is one kid's own login, used once.
    public class FamilyInvite
    {
        public const string ParentKind = "Parent";
        public const string PlayerKind = "Player";

        public Guid Id { get; set; } = Guid.CreateVersion7();
        public string Code { get; set; } = string.Empty;
        public string Kind { get; set; } = ParentKind;
        public Guid CreatedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAt { get; set; }
        public DateTime? RevokedAt { get; set; }
        // A player link, once the kid has used it.
        public DateTime? UsedAt { get; set; }

        public User CreatedBy { get; set; } = null!;
        public ICollection<FamilyInvitePlayer> Players { get; set; } = new List<FamilyInvitePlayer>();
        public ICollection<FamilyInviteJoin> Joins { get; set; } = new List<FamilyInviteJoin>();

        public bool IsOpenAt(DateTime now) => RevokedAt == null && ExpiresAt > now && (Kind == ParentKind || UsedAt == null);
    }

    public class FamilyInvitePlayer
    {
        public Guid Id { get; set; } = Guid.CreateVersion7();
        public Guid InviteId { get; set; }
        public Guid PlayerId { get; set; }

        public FamilyInvite Invite { get; set; } = null!;
        public Player Player { get; set; } = null!;
    }

    // Who joined through the link - shown to the parent who sent it.
    public class FamilyInviteJoin
    {
        public Guid Id { get; set; } = Guid.CreateVersion7();
        public Guid InviteId { get; set; }
        public Guid UserId { get; set; }
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

        public FamilyInvite Invite { get; set; } = null!;
        public User User { get; set; } = null!;
    }
}
