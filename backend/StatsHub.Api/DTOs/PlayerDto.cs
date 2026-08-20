namespace StatsHub.Api.DTOs
{
    public class PlayerDto
    {
        public int Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public int JerseyNumber { get; set; }
        public string Position { get; set; } = string.Empty;
        public int? Height { get; set; }
        public int? Weight { get; set; }
        public DateTime DateOfBirth { get; set; }
        public string? ProfilePictureUrl { get; set; }
        public List<TeamDto> Teams { get; set; } = new List<TeamDto>();
        public List<ParentDto> Parents { get; set; } = new List<ParentDto>();
    }

    public class ParentDto
    {
        public int UserId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
    }

    public class CreatePlayerDto
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public int JerseyNumber { get; set; }
        public string Position { get; set; } = string.Empty;
        public int? Height { get; set; }
        public int? Weight { get; set; }
        public DateTime DateOfBirth { get; set; }
        public string? ProfilePictureUrl { get; set; }
    }

    public class UpdatePlayerDto
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public int? JerseyNumber { get; set; }
        public string? Position { get; set; }
        public int? Height { get; set; }
        public int? Weight { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? ProfilePictureUrl { get; set; }
    }
}
