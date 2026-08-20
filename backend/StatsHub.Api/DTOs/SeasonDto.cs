namespace StatsHub.Api.DTOs
{
    public class SeasonDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Sport { get; set; } = string.Empty;
        public int Year { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public int TotalGames { get; set; }
    }

    public class CreateSeasonDto
    {
        public string Name { get; set; } = string.Empty;
        public string Sport { get; set; } = "Basketball";
        public int Year { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
    }

    public class UpdateSeasonDto
    {
        public string? Name { get; set; }
        public int? Year { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
    }
}
