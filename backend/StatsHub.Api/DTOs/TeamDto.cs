namespace StatsHub.Api.DTOs
{
    public class TeamDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class CreateTeamDto
    {
        public string Name { get; set; } = string.Empty;
    }
}
