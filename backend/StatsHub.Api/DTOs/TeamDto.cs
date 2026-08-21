namespace StatsHub.Api.DTOs
{
    public class TeamDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        // Only populated when this team appears in the context of a specific
        // player (e.g. PlayerDto.Teams) - a player can wear a different
        // number on each team, so there's no single "the" jersey number here.
        public int? JerseyNumber { get; set; }
    }

    public class CreateTeamDto
    {
        public string Name { get; set; } = string.Empty;
    }

    public class AddPlayerToTeamDto
    {
        // Defaults to the player's profile jersey number when omitted.
        public int? JerseyNumber { get; set; }
    }

    public class UpdatePlayerTeamDto
    {
        public int JerseyNumber { get; set; }
    }
}
