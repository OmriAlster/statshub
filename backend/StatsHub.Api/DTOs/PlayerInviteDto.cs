namespace StatsHub.Api.DTOs
{
    public class PlayerInviteDto
    {
        public string InviteCode { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
    }

    public class CreatePlayerInviteDto
    {
        public string? Password { get; set; }
    }

    public class ParentInviteDto
    {
        public string InviteCode { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
    }

    public class CreateParentInviteDto
    {
        public string? Password { get; set; }
    }

    public class ClaimParentInviteDto
    {
        public string InviteCode { get; set; } = string.Empty;
        public string? Password { get; set; }
    }
}
