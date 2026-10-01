namespace StatsHub.Api.Models
{
    public class User
    {
        public Guid Id { get; set; } = Guid.CreateVersion7();
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string? GoogleId { get; set; }
        public string? PasswordHash { get; set; }
        // Copied into every login token; changing it ends every session at
        // once ("Log out of all devices", or a password set by someone who
        // didn't own the email being removed). Null for accounts from before
        // this existed - their tokens stay valid until it's first set.
        public string? SecurityStamp { get; set; }
        public string? ProfilePictureUrl { get; set; }
        public string Role { get; set; } = "Parent"; // Parent or Player
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public ICollection<Player> Players { get; set; } = new List<Player>();
        public ICollection<Season> Seasons { get; set; } = new List<Season>();
    }
}
