namespace StatsHub.Api.DTOs
{
    public class ShotDto
    {
        public Guid Id { get; set; }
        public Guid GameStatsId { get; set; }
        public Guid GameId { get; set; }
        public Guid PlayerId { get; set; }
        public int Quarter { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public bool Made { get; set; }
        public int Value { get; set; }
    }

    public class CreateShotDto
    {
        public Guid GameStatsId { get; set; }
        public int Quarter { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public bool Made { get; set; }
        public int Value { get; set; }
    }
}
